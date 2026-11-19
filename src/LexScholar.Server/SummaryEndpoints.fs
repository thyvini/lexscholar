/// Summary of a document (F003, contracts/summary-api.yaml, F005 contracts/chatgpt-api.yaml).
/// The destination is resolved first, then the local checks run in contract order, then the
/// confirmation; only after that does text go to the configured service or to her ChatGPT plan.
module LexScholar.Server.SummaryEndpoints

open System
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open LexScholar.Application
open LexScholar.Domain
open LexScholar.Infrastructure

let private problem status code title detail : IResult =
    Results.Problem(statusCode = Nullable(status), title = title, detail = detail, extensions = dict [ "code", box code ])

let private notAssociated directory projectId documentId =
    match LibraryRepository.listForProject directory projectId with
    | Ok associations -> not (associations |> List.exists (fun (item, _) -> item.DocumentId = documentId))
    | Error _ -> true

/// The `failed` code of a service error (contract: F003's `service_failed` plus the plan's three).
let private failureCode : Summarize.ServiceError -> string = function
    | Summarize.ServiceError.UsageLimitReached -> "usage_limit_reached"
    | Summarize.ServiceError.SignedOut -> "signed_out"
    | Summarize.ServiceError.NoPlanAccess -> "no_plan_access"
    | _ -> "service_failed"

let private signedOutDetail = Summarize.failureDetail Summarize.ServiceError.SignedOut

let private summaryLogger (context: HttpContext) =
    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger "LexScholar.Summary"

/// The outgoing handler comes from DI when one is registered (tests), else a plain one.
let private serviceClient (context: HttpContext) (summary: SummaryConfiguration) =
    match context.RequestServices.GetService<HttpMessageHandler>() with
    | null -> new HttpClient(ChatCompletions.createHandler summary.Endpoint, true)
    | injected -> new HttpClient(injected, false)

/// `SummaryState` of the contract for a job's state.
let private stateJson (state: JobState) : obj =
    match state with
    | JobState.Running None -> box {| status = "running" |}
    | JobState.Running (Some (Progress.Pages (processed, total))) -> box {| status = "running"; pagesProcessed = processed; totalPages = total |}
    | JobState.Running (Some Progress.Combining) -> box {| status = "running"; combining = true |}
    | JobState.Finished summary ->
        box {| status = "finished"
               summary =
                {| documentId = summary.DocumentId
                   points = summary.Points |> List.map (fun point -> {| text = point.Text; pages = point.Pages |})
                   pagesCovered = summary.PagesCovered
                   unreadablePages = summary.UnreadablePages
                   omittedPoints = summary.OmittedPoints
                   generatedAt = summary.GeneratedAt
                   serviceHost = summary.ServiceHost
                   model = summary.Model |} |}
    | JobState.Failed (code, detail, omitted) ->
        box {| status = "failed"; code = code; detail = detail; omittedPoints = (match omitted with Some count -> Nullable count | None -> Nullable()) |}

let private stateResult (status: int) (state: JobState) : IResult = Results.Json(stateJson state, statusCode = Nullable status)

/// The job's body (research §1, §3): runs the summary on the job's own token and reports into the registry.
/// Nothing here touches the request, which is long gone. A change of state is ignored when the job was replaced or stopped.
let private runJob (logger: ILogger) (jobs: SummaryJobs) (key: SummaryKey) (job: JobHandle) (ended: Summarize.ServiceError -> Task<unit>) (maxChunkCharacters: int) (complete: Summarize.ChatMessage list -> System.Threading.CancellationToken -> Task<Result<string, Summarize.ServiceError>>) (serviceHost: string) (model: string) (pages: ReadablePages) (instruction: string option) : Task<unit> = task {
    let capabilities : Summarize.Capabilities =
        { ExtractPages = fun _ -> failwith "pages are extracted by the endpoint"
          Complete = complete
          // A report after the job was replaced or stopped changes nothing (the registry checks the id).
          Report = fun progress ->
              let progress =
                  match progress with
                  | Summarize.Progress.PagesProcessed (processed, total) -> Progress.Pages (processed, total)
                  | Summarize.Progress.Combining -> Progress.Combining
              jobs.Progress(key, job.Id, progress) |> ignore
              Task.FromResult() }
    logger.LogInformation("Summarizing document {DocumentId}: {Readable} readable pages, {Unreadable} unreadable",
                          box key.Document, pages.Readable.Length, pages.Unreadable.Length)
    try
        let! result = Summarize.summarizeWithInstruction capabilities maxChunkCharacters pages instruction job.Token
        // Checked before `ended`, which cancels a plan run's own token: a sign-out that came after the last call still ends
        // the run, whatever the result, so no service error is reported once she is signed out (FR-007, as F005).
        // A finished summary is checked by `Complete` itself, inside the registry lock.
        let signedOut = job.Token.IsCancellationRequested
        match result with
        | Error _ when signedOut -> jobs.Fail(key, job.Id, "signed_out", signedOutDetail, None) |> ignore
        | Ok validated ->
            let summary : StoredSummary =
                { DocumentId = key.Document
                  Points = validated.Points
                  PagesCovered = pages.Readable |> List.map (fun page -> page.Number)
                  UnreadablePages = pages.Unreadable
                  OmittedPoints = validated.Omitted
                  GeneratedAt = DateTimeOffset.UtcNow
                  ServiceHost = serviceHost
                  Model = model }
            jobs.Complete(key, job.Id, summary) |> ignore
        | Error (Summarize.SummaryFailure.NotEnoughText omitted) ->
            jobs.Fail(key, job.Id, "not_enough_text", "The service returned no point with valid citations.", Some omitted) |> ignore
        | Error (Summarize.SummaryFailure.ServiceFailed error) ->
            // A failure mid-run (usage limit included) never leaves a partial summary.
            do! ended error
            jobs.Fail(key, job.Id, failureCode error, Summarize.failureDetail error, None) |> ignore
    with
    | :? OperationCanceledException when job.Token.IsCancellationRequested ->
        // The app is stopping, the job was stopped, or (plan) she signed out: the call was cancelled, nothing more is
        // sent and nothing is kept (AR 2). The failure only shows when the job is still the current one.
        logger.LogInformation("Summary of document {DocumentId} ended by cancellation", box key.Document)
        jobs.Fail(key, job.Id, "signed_out", signedOutDetail, None) |> ignore
    | error ->
        // Only the document id and the error type are logged, never text or the service's answer (FR-009, FR-014).
        logger.LogError("Summary of document {DocumentId} failed unexpectedly: {Error}", box key.Document, error.GetType().Name)
        jobs.Fail(key, job.Id, "service_failed", Summarize.failureDetail Summarize.ServiceError.UnusableOutput, None) |> ignore }

/// The request body when it is at most `limit` bytes, else None (an oversized body is never parsed).
let private readBoundedBody (context: HttpContext) (limit: int) : Task<ReadOnlyMemory<byte> option> = task {
    let buffer = Array.zeroCreate<byte> (limit + 1)
    let mutable total = 0
    let mutable finished = false
    while not finished && total < buffer.Length do
        let! read = context.Request.Body.ReadAsync(buffer.AsMemory(total), context.RequestAborted)
        if read = 0 then finished <- true else total <- total + read
    return if finished then Some (ReadOnlyMemory(buffer, 0, total)) else None }

let private stringProperty (name: string) (root: JsonElement) =
    match root.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.String -> Some (value.GetString())
    | _ -> None

type private Destination =
    | Chatgpt
    | Configured

/// What the request names as its destination.
type private Requested =
    | NotNamed
    | Named of Destination
    /// Present but not `chatgpt` or `configured` (or not a string).
    | Unrecognized

/// The request body as far as it is understood: anything missing, malformed, oversized or of
/// another type cannot confirm anything. Invalid instructions and oversized bodies are rejected.
type private Body =
    { Destination: Requested
      /// `"confirmSend": true`, the JSON boolean.
      ConfirmSend: bool
      /// The (host, model) a `configured` confirmation names.
      ConfirmedService: (string * string) option
      Instruction: string option
      InvalidInstruction: bool }

let private emptyBody = { Destination = NotNamed; ConfirmSend = false; ConfirmedService = None; Instruction = None; InvalidInstruction = false }

let private parseBody (root: JsonElement) : Body =
    let confirmSend =
        match root.TryGetProperty "confirmSend" with
        | true, flag -> flag.ValueKind = JsonValueKind.True
        | _ -> false
    let destination =
        match root.TryGetProperty "destination" with
        | true, value when value.ValueKind = JsonValueKind.String ->
            match value.GetString() with
            | "chatgpt" -> Named Chatgpt
            | "configured" -> Named Configured
            | _ -> Unrecognized
        | true, _ -> Unrecognized
        | _ -> NotNamed
    let instruction, invalid =
        match root.TryGetProperty "instruction" with
        | false, _ -> None, false
        | true, value when value.ValueKind = JsonValueKind.String ->
            let text = value.GetString()
            if text.Length > SummaryInstruction.maximumCharacters then None, true
            else SummaryInstruction.normalize text, false
        | _ -> None, true
    { Destination = destination
      Instruction = instruction
      InvalidInstruction = invalid
      ConfirmSend = confirmSend
      ConfirmedService =
        if confirmSend then Option.map2 (fun host model -> host, model) (stringProperty "confirmedHost" root) (stringProperty "confirmedModel" root)
        else None }

let private readBody (context: HttpContext) : Task<Body> = task {
    // Covers 2000 UTF-16 characters even when JSON escapes every character.
    match! readBoundedBody context 16384 with
    | None -> return { emptyBody with InvalidInstruction = true }
    | Some body ->
        try
            use document = JsonDocument.Parse body
            let root = document.RootElement
            return if root.ValueKind = JsonValueKind.Object then parseBody root else emptyBody
        with :? JsonException -> return emptyBody }

let private extractionProblem : Summarize.ExtractionError -> IResult = function
    | Summarize.ExtractionError.DocumentNotFound -> problem 404 "document_not_found" "Document not found" null
    | Summarize.ExtractionError.DocumentUnavailable ->
        problem 503 "document_unavailable" "The document's stored file is missing, unreadable or not the recorded size."
            "Its library record and associations are unchanged."
    | Summarize.ExtractionError.TooManyPages ->
        problem 422 "too_many_pages" "The document has too many pages"
            $"Only documents of up to {PdfText.maximumPages} pages can be summarized."

let private readabilityProblem : NoTextReason -> IResult = function
    | NoReadableText ->
        problem 422 "no_text" "The document has no readable text"
            "No page has a text layer, so there is nothing to summarize. Nothing was sent."
    | NotEnoughText ->
        problem 422 "not_enough_text" "The document has too little text"
            "The readable text is too short to summarize. Nothing was sent."

let private confirmationRequired (summary: SummaryConfiguration) : IResult =
    Results.Problem(
        statusCode = Nullable 409,
        title = "Confirmation needed",
        detail = "The text of this document and any optional instruction will leave this machine. Nothing was sent.",
        extensions = dict [ "code", box "confirmation_required"
                            "serviceHost", box summary.Endpoint.Authority
                            "model", box summary.Model ])

let private chatgptConfirmationRequired (account: string) : IResult =
    Results.Problem(
        statusCode = Nullable 409,
        title = "Confirmation needed",
        detail = "The text of this document and any optional instruction will be sent to your ChatGPT plan. Nothing was sent.",
        extensions = dict [ "code", box "confirmation_required"
                            "destination", box "chatgpt"
                            "account", box account ])

let private chatgptSignedOut () =
    problem 503 "chatgpt_signed_out" "Not signed in to ChatGPT" "Sign in with ChatGPT to summarize with your plan. Nothing was sent."

/// Chunk size for the plan: F003's default.
let private chatgptChunkCharacters = 12_000

/// The outgoing client for the ChatGPT API and the sign-in service: the injected handler (tests) or a real one.
let private chatgptClient (injected: HttpMessageHandler) (real: unit -> HttpMessageHandler) =
    match injected with
    | null -> new HttpClient(real (), true)
    | handler -> new HttpClient(handler, false)

/// The answer to a `Start`: the new job's state (202), or the one that already exists (200).
let private startAnswer (jobs: SummaryJobs) (key: SummaryKey) (outcome: StartOutcome) : IResult =
    match outcome with
    | Started -> stateResult 202 (jobs.State key |> Option.defaultValue (JobState.Running None))
    | AlreadyRunning | AlreadyFinished -> stateResult 200 (jobs.State key |> Option.defaultValue (JobState.Running None))
    | NotAssociated -> problem 404 "not_found" "Association not found" null
    | NoSavedSummary -> problem 404 "no_summary" "No saved summary to replace" null

/// The summary through her plan, as a background job. `prepareSend` is the only way to a token: confirmation, refresh
/// and save happen there under one lock, and nothing is sent before it returns `Prepared`. The job's token source exists
/// before it and is handed to the registry after, so the run it registers ends with the job and with sign-out (research §2).
let private summarizeViaPlan regenerate (context: HttpContext) (vault: ChatGptAccess.Vault) (jobs: SummaryJobs) (body: Body) projectId documentId (pages: ReadablePages) : Task<IResult> = task {
    let clock () = DateTimeOffset.UtcNow
    let logger = summaryLogger context
    let injected = context.RequestServices.GetService<HttpMessageHandler>()
    let stopping = context.RequestServices.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>().ApplicationStopping
    let key = { Project = projectId; Document = documentId }
    let source = System.Threading.CancellationTokenSource.CreateLinkedTokenSource stopping
    let mutable handedOver = false
    try
        use authHttp = chatgptClient injected (fun () -> OpenAiAuth.createHandler ())
        match! ChatGptAccess.prepareSend vault (ChatGptEndpoints.authClient authHttp clock) clock body.ConfirmSend context.RequestAborted source.Token with
        | ChatGptAccess.AwaitingConfirmation account ->
            logger.LogInformation "Summary needs confirmation to use the ChatGPT plan"
            return chatgptConfirmationRequired account
        | ChatGptAccess.NoSignIn -> return chatgptSignedOut ()
        // Decided before anything was sent: the job is created already failed, so the client reads it like any other.
        | ChatGptAccess.SignInEnded -> return startAnswer jobs key (jobs.StartFailed(key, "signed_out", signedOutDetail, regenerate = regenerate))
        | ChatGptAccess.RefreshUnreachable ->
            return startAnswer jobs key (jobs.StartFailed(key, "service_failed", "ChatGPT could not be reached to renew your sign-in. Try again.", regenerate = regenerate))
        | ChatGptAccess.Prepared (credentials, run) ->
            // A refusal ends the sign-in (record deleted, other runs cancelled) before the failure is reported.
            let ended (error: Summarize.ServiceError) : Task<unit> =
                match error with
                | Summarize.ServiceError.SignedOut | Summarize.ServiceError.NoPlanAccess -> ChatGptAccess.endSignIn vault credentials
                | _ -> Task.FromResult()
            let work (job: JobHandle) : Task<unit> = task {
                use _ = run
                use client = chatgptClient injected (fun () -> ResponsesApi.createHandler ())
                try
                    match! ResponsesApi.listModels client credentials.AccessToken job.Token with
                    | Error error ->
                        // Checked before `ended`, which cancels this run's own token.
                        if job.Token.IsCancellationRequested then
                            jobs.Fail(key, job.Id, "signed_out", signedOutDetail, None) |> ignore
                        else
                            do! ended error
                            jobs.Fail(key, job.Id, failureCode error, Summarize.failureDetail error, None) |> ignore
                    | Ok models ->
                        match ChatGptSignIn.pickModel models with
                        // A sign-out that came while the list was being read wins over an unusable list.
                        | None when job.Token.IsCancellationRequested -> jobs.Fail(key, job.Id, "signed_out", signedOutDetail, None) |> ignore
                        | None -> jobs.Fail(key, job.Id, "service_failed", Summarize.failureDetail Summarize.ServiceError.UnusableOutput, None) |> ignore
                        | Some model ->
                            let settings : ResponsesApi.Settings = { Model = model; AccessToken = credentials.AccessToken }
                            do! runJob logger jobs key job ended chatgptChunkCharacters (ResponsesApi.complete logger client settings) "ChatGPT plan" model pages body.Instruction
                with
                | :? OperationCanceledException when job.Token.IsCancellationRequested ->
                    // Signed out (or stopped) while the model list was requested: nothing was sent.
                    jobs.Fail(key, job.Id, "signed_out", signedOutDetail, None) |> ignore
                | error ->
                    // Anything else (a credential file that cannot be deleted, say) must not leave the job running for good.
                    // Only the error type is logged, never text or the service's answer (FR-009, FR-014).
                    logger.LogError("Summary of document {DocumentId} failed unexpectedly: {Error}", box key.Document, error.GetType().Name)
                    jobs.Fail(key, job.Id, "service_failed", Summarize.failureDetail Summarize.ServiceError.UnusableOutput, None) |> ignore }
            match jobs.StartWith(key, source, run.Token, work, regenerate = regenerate) with
            | Started ->
                handedOver <- true
                return startAnswer jobs key Started
            | refused ->
                (run :> IDisposable).Dispose()
                return startAnswer jobs key refused
    finally
        if not handedOver then source.Dispose() }

/// The service from her settings file: F003's rule, unchanged. Elsewhere needs this request's confirmation.
/// After the checks the summary runs as a background job (F004): the answer is immediate and closing the request does not stop it.
let private summarizeConfigured regenerate (context: HttpContext) (jobs: SummaryJobs) (configured: SummaryConfiguration) (body: Body) projectId documentId (pages: ReadablePages) : Task<IResult> = task {
    // FR-014: for a service elsewhere nothing is sent without this request's confirmation
    // of the host and model currently configured.
    let current = configured.Endpoint.Authority, configured.Model
    if ServiceLocation.classify configured.Endpoint.Host = Elsewhere && body.ConfirmedService <> Some current then
        (summaryLogger context).LogInformation("Summary needs confirmation for {Host}, model {Model}", configured.Endpoint.Authority, configured.Model)
        return confirmationRequired configured
    else
        // Everything the job needs is taken from the request now: it outlives it.
        let logger = summaryLogger context
        let injected = context.RequestServices.GetService<HttpMessageHandler>()
        let stopping = context.RequestServices.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>().ApplicationStopping
        let key = { Project = projectId; Document = documentId }
        let run (job: JobHandle) : Task<unit> = task {
            use client = match injected with null -> new HttpClient(ChatCompletions.createHandler configured.Endpoint, true) | handler -> new HttpClient(handler, false)
            let settings : ChatCompletions.Settings =
                { Endpoint = configured.Endpoint; Model = configured.Model; ApiKey = configured.ApiKey
                  Temperature = configured.Temperature; MaxOutputTokens = configured.MaxOutputTokens }
            return! runJob logger jobs key job (fun _ -> Task.FromResult()) configured.MaxChunkCharacters (ChatCompletions.complete logger client settings)
                        configured.Endpoint.Authority configured.Model pages body.Instruction }
        return startAnswer jobs key (jobs.Start(key, stopping, run, regenerate = regenerate)) }

/// Resolves the destination before anything else (research §10): `Error` is the answer to send.
let private resolveDestination (configuration: WorkspaceConfiguration) (signedIn: bool) (requested: Requested) : Result<Destination, IResult> =
    let notConfigured () =
        problem 503 "summary_service_not_configured" "No summary service is configured"
            "Set LexScholar:Summary:Endpoint and LexScholar:Summary:Model (see the quickstart) and restart LexScholar."
    let required () =
        problem 400 "destination_required" "A destination is needed"
            "Say whether to use chatgpt or the configured service. Nothing was sent."
    match configuration.Summary.IsSome, signedIn, requested with
    | false, false, _ -> Error(notConfigured ())
    | _, _, Unrecognized -> Error(required ())
    | true, true, NotNamed -> Error(required ())
    | true, false, NotNamed | true, _, Named Configured -> Ok Configured
    | false, true, NotNamed -> Ok Chatgpt
    | _, false, Named Chatgpt -> Error(chatgptSignedOut ())
    | _, true, Named Chatgpt -> Ok Chatgpt
    | false, _, Named Configured -> Error(notConfigured ())

/// The state, stop/dismiss and status routes of F004.
let private mapBackground (app: WebApplication) (jobs: SummaryJobs) (listStored: unit -> SummaryKey list) =
    let path = "/api/projects/{projectId:guid}/documents/{documentId:guid}/summary"
    app.MapGet(path, Func<Guid, Guid, IResult>(fun projectId documentId ->
        match jobs.State { Project = projectId; Document = documentId } with
        | Some state -> stateResult 200 state
        | None -> problem 404 "no_summary" "No summary" "Nothing is running, finished or failed for this document in this project.")) |> ignore
    // Stops a running summary, or dismisses a finished or failed one (and deletes the stored one); idempotent.
    app.MapDelete(path, Func<Guid, Guid, IResult>(fun projectId documentId ->
        jobs.Stop { Project = projectId; Document = documentId }
        Results.NoContent())) |> ignore
    app.MapPost(path + "/cancel", Func<Guid, Guid, IResult>(fun projectId documentId ->
        jobs.Cancel { Project = projectId; Document = documentId }
        Results.NoContent())) |> ignore
    // The durable version is independently readable while the replacement operation runs or has failed.
    app.MapGet(path + "/saved", Func<Guid, Guid, IResult>(fun projectId documentId ->
        jobs.Locked(fun () ->
            match jobs.Capabilities.Find { Project = projectId; Document = documentId } with
            | Some summary -> stateResult 200 (JobState.Finished summary)
            | None -> problem 404 "no_summary" "No saved summary" null))) |> ignore
    // One entry per pair with a running, finished or failed summary, for the list markers.
    app.MapGet("/api/summaries", Func<IResult>(fun () ->
        let statusName (status: SummaryStatus) =
            match status with
            | SummaryStatus.Running -> "running"
            | SummaryStatus.Finished -> "finished"
            | SummaryStatus.Failed -> "failed"
        let entries =
            jobs.Snapshot listStored
            |> List.map (fun (key, status) -> {| projectId = key.Project; documentId = key.Document; status = statusName status |})
        Results.Json entries)) |> ignore

let map (app: WebApplication) (configuration: WorkspaceConfiguration) (vault: ChatGptAccess.Vault) (jobs: SummaryJobs) =
    let directory = configuration.DataDirectory
    mapBackground app jobs (fun () -> SummaryRepository.listKeys directory)
    // Published so tests can wait for a job to end (a handshake instead of polling).
    (app :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] <- box jobs
    let mapRequest regenerate path =
      app.MapPost(path,
                Func<Guid, Guid, HttpContext, Task<IResult>>(fun projectId documentId context -> task {
        let! body = readBody context
        // One summary per document in a project (FR-011): a running or finished (stored included) summary for a pair that is
        // still in the project answers with its state before any destination, confirmation or sign-in check, since nothing
        // new is sent. A failed one is not returned: asking again replaces it, through the checks below.
        let key = { Project = projectId; Document = documentId }
        let existing =
            match jobs.State key with
            | Some (JobState.Running _ as state) when not (notAssociated directory projectId documentId) -> Some state
            | Some (JobState.Finished _ as state) when not regenerate && not (notAssociated directory projectId documentId) -> Some state
            | _ -> None
        match existing with
        | _ when body.InvalidInstruction ->
            return problem 400 "invalid_instruction" "Invalid instruction" "Use an optional instruction of at most 2000 characters. Nothing was sent."
        | Some state -> return stateResult 200 state
        | None ->
            // Reading the vault waits for a refresh in flight, so a request that explicitly names its
            // configured service (which exists) never reads it: a local summary does not wait for ChatGPT.
            let! signedIn =
                task {
                    match body.Destination with
                    | Named Configured when configuration.Summary.IsSome -> return false
                    | _ ->
                        let! record = vault.Read()
                        return record.IsSome
                }
            match resolveDestination configuration signedIn body.Destination with
            | Error answer -> return answer
            | Ok destination ->
                if notAssociated directory projectId documentId then
                    if LibraryRepository.list directory |> List.exists (fun document -> document.Id = documentId) then
                        return problem 404 "not_found" "Association not found" null
                    else
                        return problem 404 "document_not_found" "Document not found" null
                else
                    match! PdfText.extractPages directory documentId context.RequestAborted with
                    | Error error -> return extractionProblem error
                    | Ok pages ->
                        match Summaries.readability pages with
                        | Error failure -> return readabilityProblem failure
                        | Ok readable ->
                            // These two calls are the only paths to `summarizeOnce`, and both run after the local checks
                            // and after their own confirmation rule.
                            match destination with
                            | Chatgpt -> return! summarizeViaPlan regenerate context vault jobs body projectId documentId readable
                            | Configured -> return! summarizeConfigured regenerate context jobs configuration.Summary.Value body projectId documentId readable })) |> ignore
    let path = "/api/projects/{projectId:guid}/documents/{documentId:guid}/summary"
    mapRequest false path
    mapRequest true (path + "/regenerate")
