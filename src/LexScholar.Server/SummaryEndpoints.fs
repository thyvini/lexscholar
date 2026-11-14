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

/// Writes one NDJSON event and flushes it, so progress reaches the client as it happens.
let private write (context: HttpContext) (event: obj) : Task<unit> = task {
    do! context.Response.WriteAsync(JsonSerializer.Serialize(event, JsonSerializerOptions.Web) + "\n", context.RequestAborted)
    do! context.Response.Body.FlushAsync(context.RequestAborted) }

let private failed context code detail (omitted: int option) : Task<unit> =
    write context (box {| ``type`` = "failed"; code = code; detail = detail; omittedPoints = omitted |})

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

let private summaryEvent documentId (serviceHost: string) (model: string) (pages: ReadablePages) (validated: ValidatedPoints) : obj =
    box {| ``type`` = "summary"
           documentId = documentId
           points = validated.Points |> List.map (fun point -> {| text = point.Text; pages = point.Pages |})
           pagesCovered = pages.Readable |> List.map (fun page -> page.Number)
           unreadablePages = pages.Unreadable
           omittedPoints = validated.Omitted
           generatedAt = DateTimeOffset.UtcNow
           serviceHost = serviceHost
           model = model |}

/// Runs the summary through `complete` and writes the NDJSON events. The caller has passed every check.
let private summarizeOnce (context: HttpContext) (token: System.Threading.CancellationToken) (ended: Summarize.ServiceError -> Task<unit>) (maxChunkCharacters: int) (complete: Summarize.ChatMessage list -> System.Threading.CancellationToken -> Task<Result<string, Summarize.ServiceError>>) (serviceHost: string) (model: string) documentId (pages: ReadablePages) : Task<IResult> = task {
    let logger = summaryLogger context
    context.Response.ContentType <- "application/x-ndjson"
    let capabilities : Summarize.Capabilities =
        { ExtractPages = fun _ -> failwith "pages are extracted by the endpoint"
          Complete = complete
          Report = function
              | Summarize.Progress.PagesProcessed (processed, total) ->
                  write context (box {| ``type`` = "progress"; pagesProcessed = processed; totalPages = total |})
              | Summarize.Progress.Combining -> write context (box {| ``type`` = "combining"  |}) }
    logger.LogInformation("Summarizing document {DocumentId}: {Readable} readable pages, {Unreadable} unreadable",
                          box documentId, pages.Readable.Length, pages.Unreadable.Length)
    try
      let! result = Summarize.summarize capabilities maxChunkCharacters pages token
      // A sign-out that came after the last call still ends the run, whatever the result: no summary and no
      // service error is reported once she is signed out, so the client reloads the signed-out state.
      if token.IsCancellationRequested && not context.RequestAborted.IsCancellationRequested then
          do! failed context "signed_out" signedOutDetail None
      else
      match result with
      | Ok validated -> do! write context (summaryEvent documentId serviceHost model pages validated)
      | Error (Summarize.SummaryFailure.NotEnoughText omitted) ->
          do! failed context "not_enough_text" "The service returned no point with valid citations." (Some omitted)
      | Error (Summarize.SummaryFailure.ServiceFailed error) ->
          // Nothing was written but progress: a failure mid-run (usage limit included) never leaves a partial summary.
          do! ended error
          do! failed context (failureCode error) (Summarize.failureDetail error) None
    with
    | :? OperationCanceledException when context.RequestAborted.IsCancellationRequested ->
        // The researcher abandoned the request: the service call was cancelled and nothing more is sent.
        // Only the document id is logged, never its text or the service's answer (FR-009, FR-014).
        logger.LogInformation("Summary of document {DocumentId} abandoned", box documentId)
    | :? OperationCanceledException when token.IsCancellationRequested ->
        // She signed out (or the sign-in ended) while the summary ran: the call was cancelled, nothing more is sent.
        logger.LogInformation("Summary of document {DocumentId} ended by sign-out", box documentId)
        do! failed context "signed_out" signedOutDetail None
    return Results.Empty }

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
/// another type counts as absent, so it can never confirm anything.
type private Body =
    { Destination: Requested
      /// `"confirmSend": true`, the JSON boolean.
      ConfirmSend: bool
      /// The (host, model) a `configured` confirmation names.
      ConfirmedService: (string * string) option }

let private emptyBody = { Destination = NotNamed; ConfirmSend = false; ConfirmedService = None }

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
    { Destination = destination
      ConfirmSend = confirmSend
      ConfirmedService =
        if confirmSend then Option.map2 (fun host model -> host, model) (stringProperty "confirmedHost" root) (stringProperty "confirmedModel" root)
        else None }

let private readBody (context: HttpContext) : Task<Body> = task {
    match! readBoundedBody context 4096 with
    | None -> return emptyBody
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
        detail = "The text of this document will leave this machine. Nothing was sent.",
        extensions = dict [ "code", box "confirmation_required"
                            "serviceHost", box summary.Endpoint.Authority
                            "model", box summary.Model ])

let private chatgptConfirmationRequired (account: string) : IResult =
    Results.Problem(
        statusCode = Nullable 409,
        title = "Confirmation needed",
        detail = "The text of this document will be sent to your ChatGPT plan. Nothing was sent.",
        extensions = dict [ "code", box "confirmation_required"
                            "destination", box "chatgpt"
                            "account", box account ])

let private chatgptSignedOut () =
    problem 503 "chatgpt_signed_out" "Not signed in to ChatGPT" "Sign in with ChatGPT to summarize with your plan. Nothing was sent."

/// Chunk size for the plan: F003's default.
let private chatgptChunkCharacters = 12_000

/// A failure before any summary event: the stream starts with one `failed` event.
let private streamFailed (context: HttpContext) code detail : Task<IResult> = task {
    context.Response.ContentType <- "application/x-ndjson"
    do! failed context code detail None
    return Results.Empty }

/// The outgoing client for the ChatGPT API and the sign-in service: the injected handler (tests) or a real one.
let private chatgptClient (context: HttpContext) (real: unit -> HttpMessageHandler) =
    match context.RequestServices.GetService<HttpMessageHandler>() with
    | null -> new HttpClient(real (), true)
    | injected -> new HttpClient(injected, false)

/// The summary through her plan. `prepareSend` is the only way to a token: confirmation, refresh
/// and save happen there under one lock, and nothing is sent before it returns `Prepared`.
let private summarizeViaPlan (context: HttpContext) (vault: ChatGptAccess.Vault) (body: Body) documentId (pages: ReadablePages) : Task<IResult> = task {
    let clock () = DateTimeOffset.UtcNow
    use authHttp = chatgptClient context (fun () -> OpenAiAuth.createHandler ())
    match! ChatGptAccess.prepareSend vault (ChatGptEndpoints.authClient authHttp clock) clock body.ConfirmSend context.RequestAborted with
    | ChatGptAccess.AwaitingConfirmation account ->
        (summaryLogger context).LogInformation "Summary needs confirmation to use the ChatGPT plan"
        return chatgptConfirmationRequired account
    | ChatGptAccess.NoSignIn -> return chatgptSignedOut ()
    | ChatGptAccess.SignInEnded -> return! streamFailed context "signed_out" signedOutDetail
    | ChatGptAccess.RefreshUnreachable ->
        return! streamFailed context "service_failed" "ChatGPT could not be reached to renew your sign-in. Try again."
    | ChatGptAccess.Prepared (credentials, run) ->
        use _ = run
        let token = run.Token
        // A refusal ends the sign-in (record deleted, other runs cancelled) before the failure is reported.
        let ended (error: Summarize.ServiceError) : Task<unit> =
            match error with
            | Summarize.ServiceError.SignedOut | Summarize.ServiceError.NoPlanAccess -> ChatGptAccess.endSignIn vault credentials
            | _ -> Task.FromResult()
        use client = chatgptClient context (fun () -> ResponsesApi.createHandler ())
        try
            match! ResponsesApi.listModels client credentials.AccessToken token with
            | Error error ->
                // Checked before `ended`, which cancels this run's own token.
                if token.IsCancellationRequested && not context.RequestAborted.IsCancellationRequested then
                    return! streamFailed context "signed_out" signedOutDetail
                else
                    do! ended error
                    return! streamFailed context (failureCode error) (Summarize.failureDetail error)
            | Ok models ->
                match ChatGptSignIn.pickModel models with
                | None -> return! streamFailed context "service_failed" (Summarize.failureDetail Summarize.ServiceError.UnusableOutput)
                | Some model ->
                    let settings : ResponsesApi.Settings = { Model = model; AccessToken = credentials.AccessToken }
                    return! summarizeOnce context token ended chatgptChunkCharacters (ResponsesApi.complete (summaryLogger context) client settings) "ChatGPT plan" model documentId pages
        with :? OperationCanceledException when token.IsCancellationRequested && not context.RequestAborted.IsCancellationRequested ->
            // Signed out while the model list was requested: nothing was sent.
            return! streamFailed context "signed_out" signedOutDetail }

/// The service from her settings file: F003's rule, unchanged. Elsewhere needs this request's confirmation.
let private summarizeConfigured (context: HttpContext) (configured: SummaryConfiguration) (body: Body) documentId (pages: ReadablePages) : Task<IResult> = task {
    // FR-014: for a service elsewhere nothing is sent without this request's confirmation
    // of the host and model currently configured.
    let current = configured.Endpoint.Authority, configured.Model
    if ServiceLocation.classify configured.Endpoint.Host = Elsewhere && body.ConfirmedService <> Some current then
        (summaryLogger context).LogInformation("Summary needs confirmation for {Host}, model {Model}", configured.Endpoint.Authority, configured.Model)
        return confirmationRequired configured
    else
        use client = serviceClient context configured
        let settings : ChatCompletions.Settings =
            { Endpoint = configured.Endpoint; Model = configured.Model; ApiKey = configured.ApiKey
              Temperature = configured.Temperature; MaxOutputTokens = configured.MaxOutputTokens }
        return! summarizeOnce context context.RequestAborted (fun _ -> Task.FromResult()) configured.MaxChunkCharacters (ChatCompletions.complete (summaryLogger context) client settings)
                    configured.Endpoint.Authority configured.Model documentId pages }

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

let map (app: WebApplication) (configuration: WorkspaceConfiguration) (vault: ChatGptAccess.Vault) =
    let directory = configuration.DataDirectory
    app.MapPost("/api/projects/{projectId:guid}/documents/{documentId:guid}/summary",
                Func<Guid, Guid, HttpContext, Task<IResult>>(fun projectId documentId context -> task {
        let! body = readBody context
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
                        | Chatgpt -> return! summarizeViaPlan context vault body documentId readable
                        | Configured -> return! summarizeConfigured context configuration.Summary.Value body documentId readable })) |> ignore
