/// Summary of a document (F003, contracts/summary-api.yaml). The local checks run
/// in contract order before anything is sent; then one request goes to the
/// configured service and the answer is one NDJSON event (progress, confirmation
/// and chunking arrive in later slices).
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

let private summaryLogger (context: HttpContext) =
    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger "LexScholar.Summary"

/// The outgoing handler comes from DI when one is registered (tests), else a plain one.
let private serviceClient (context: HttpContext) (summary: SummaryConfiguration) =
    match context.RequestServices.GetService<HttpMessageHandler>() with
    | null -> new HttpClient(ChatCompletions.createHandler summary.Endpoint, true)
    | injected -> new HttpClient(injected, false)

let private summaryEvent documentId (summary: SummaryConfiguration) (pages: ReadablePages) (validated: ValidatedPoints) : obj =
    box {| ``type`` = "summary"
           documentId = documentId
           points = validated.Points |> List.map (fun point -> {| text = point.Text; pages = point.Pages |})
           pagesCovered = pages.Readable |> List.map (fun page -> page.Number)
           unreadablePages = pages.Unreadable
           omittedPoints = validated.Omitted
           generatedAt = DateTimeOffset.UtcNow
           serviceHost = summary.Endpoint.Authority
           model = summary.Model |}

let private summarizeOnce (context: HttpContext) (summary: SummaryConfiguration) documentId (pages: ReadablePages) : Task<IResult> = task {
    use client = serviceClient context summary
    let logger = summaryLogger context
    let settings : ChatCompletions.Settings =
        { Endpoint = summary.Endpoint; Model = summary.Model; ApiKey = summary.ApiKey
          Temperature = summary.Temperature; MaxOutputTokens = summary.MaxOutputTokens }
    context.Response.ContentType <- "application/x-ndjson"
    let capabilities : Summarize.Capabilities =
        { ExtractPages = fun _ -> failwith "pages are extracted by the endpoint"
          Complete = ChatCompletions.complete logger client settings
          Report = function
              | Summarize.Progress.PagesProcessed (processed, total) ->
                  write context (box {| ``type`` = "progress"; pagesProcessed = processed; totalPages = total |})
              | Summarize.Progress.Combining -> write context (box {| ``type`` = "combining" |}) }
    logger.LogInformation("Summarizing document {DocumentId}: {Readable} readable pages, {Unreadable} unreadable",
                          box documentId, pages.Readable.Length, pages.Unreadable.Length)
    try
      match! Summarize.summarize capabilities summary.MaxChunkCharacters pages context.RequestAborted with
      | Ok validated -> do! write context (summaryEvent documentId summary pages validated)
      | Error (Summarize.SummaryFailure.NotEnoughText omitted) ->
          do! failed context "not_enough_text" "The service returned no point with valid citations." (Some omitted)
      | Error (Summarize.SummaryFailure.ServiceFailed error) ->
          do! failed context "service_failed" (Summarize.failureDetail error) None
    with :? OperationCanceledException when context.RequestAborted.IsCancellationRequested ->
        // The researcher abandoned the request: the service call was cancelled and nothing more is sent.
        // Only the document id is logged, never its text or the service's answer (FR-009, FR-014).
        logger.LogInformation("Summary of document {DocumentId} abandoned", box documentId)
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

/// The (host, model) a confirmation names, or None when the object is not one.
let private confirmationOf (root: JsonElement) =
    match root.TryGetProperty "confirmSend" with
    | true, flag when flag.ValueKind = JsonValueKind.True ->
        Option.map2 (fun host model -> host, model) (stringProperty "confirmedHost" root) (stringProperty "confirmedModel" root)
    | _ -> None

/// The service the request body says the researcher confirmed, or None. A confirmation is
/// `"confirmSend": true` with string `confirmedHost` and `confirmedModel`; anything else
/// (missing, malformed, oversized, another type) is not one.
let private confirmedService (context: HttpContext) : Task<(string * string) option> = task {
    match! readBoundedBody context 4096 with
    | None -> return None
    | Some body ->
        try
            use document = JsonDocument.Parse body
            let root = document.RootElement
            return if root.ValueKind = JsonValueKind.Object then confirmationOf root else None
        with :? JsonException -> return None }

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

let map (app: WebApplication) (configuration: WorkspaceConfiguration) =
    let directory = configuration.DataDirectory
    app.MapPost("/api/projects/{projectId:guid}/documents/{documentId:guid}/summary",
                Func<Guid, Guid, HttpContext, Task<IResult>>(fun projectId documentId context -> task {
        match configuration.Summary with
        | None ->
            return problem 503 "summary_service_not_configured" "No summary service is configured"
                       "Set LexScholar:Summary:Endpoint and LexScholar:Summary:Model (see the quickstart) and restart LexScholar."
        | Some _ when notAssociated directory projectId documentId ->
            if LibraryRepository.list directory |> List.exists (fun document -> document.Id = documentId) then
                return problem 404 "not_found" "Association not found" null
            else
                return problem 404 "document_not_found" "Document not found" null
        | Some configured ->
            match! PdfText.extractPages directory documentId context.RequestAborted with
            | Error error -> return extractionProblem error
            | Ok pages ->
                match Summaries.readability pages with
                | Error failure -> return readabilityProblem failure
                | Ok readable ->
                    // FR-014: for a service elsewhere nothing is sent without this request's confirmation
                    // of the host and model currently configured. This is the only path to
                    // `summarizeOnce`, and it runs after the local checks.
                    let! confirmation = confirmedService context
                    let current = configured.Endpoint.Authority, configured.Model
                    if ServiceLocation.classify configured.Endpoint.Host = Elsewhere && confirmation <> Some current then
                        (summaryLogger context).LogInformation("Summary needs confirmation for {Host}, model {Model}", configured.Endpoint.Authority, configured.Model)
                        return confirmationRequired configured
                    else return! summarizeOnce context configured documentId readable })) |> ignore
