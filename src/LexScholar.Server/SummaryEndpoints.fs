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

let private summarizeOnce (context: HttpContext) (summary: SummaryConfiguration) documentId (pages: ReadablePages) : Task<IResult> = task {
    // The outgoing handler comes from DI when one is registered (tests), else a plain one.
    let injected = context.RequestServices.GetService<HttpMessageHandler>()
    use client =
        if isNull injected then new HttpClient(ChatCompletions.createHandler summary.Endpoint, true)
        else new HttpClient(injected, false)
    let settings : ChatCompletions.Settings =
        { Endpoint = summary.Endpoint; Model = summary.Model; ApiKey = summary.ApiKey
          Temperature = summary.Temperature; MaxOutputTokens = summary.MaxOutputTokens }
    context.Response.ContentType <- "application/x-ndjson"
    let capabilities : Summarize.Capabilities =
        { ExtractPages = fun _ -> failwith "pages are extracted by the endpoint"
          Complete = ChatCompletions.complete (context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger "LexScholar.Summary") client settings
          Report = function
              | Summarize.Progress.PagesProcessed (processed, total) ->
                  write context (box {| ``type`` = "progress"; pagesProcessed = processed; totalPages = total |})
              | Summarize.Progress.Combining -> write context (box {| ``type`` = "combining" |}) }
    match! Summarize.summarize capabilities summary.MaxChunkCharacters pages context.RequestAborted with
    | Ok validated ->
        let covered = pages.Readable |> List.map (fun page -> page.Number)
        do! write context (box {| ``type`` = "summary"
                                  documentId = documentId
                                  points = validated.Points |> List.map (fun point -> {| text = point.Text; pages = point.Pages |})
                                  pagesCovered = covered
                                  unreadablePages = pages.Unreadable
                                  omittedPoints = validated.Omitted
                                  generatedAt = DateTimeOffset.UtcNow
                                  serviceHost = summary.Endpoint.Authority
                                  model = summary.Model |})
    | Error (Summarize.SummaryFailure.NotEnoughText omitted) ->
        do! failed context "not_enough_text" "The service returned no point with valid citations." (Some omitted)
    | Error (Summarize.SummaryFailure.ServiceFailed _) ->
        do! failed context "service_failed" "The summary service did not give a usable answer. Try again." None
    return Results.Empty }

/// Whether the body is a JSON object with `"confirmSend": true`. Anything else (missing,
/// malformed, oversized, another type) is not a confirmation.
let private confirmed (context: HttpContext) : Task<bool> = task {
    try
        use buffer = new IO.MemoryStream()
        let chunk = Array.zeroCreate<byte> 1024
        let mutable total = 0
        let mutable finished = false
        while not finished && total <= 4096 do
            let! read = context.Request.Body.ReadAsync(chunk, 0, chunk.Length, context.RequestAborted)
            if read = 0 then finished <- true
            else
                buffer.Write(chunk, 0, read)
                total <- total + read
        if not finished then return false
        else
            use document = JsonDocument.Parse(buffer.ToArray())
            match document.RootElement.ValueKind with
            | JsonValueKind.Object ->
                match document.RootElement.TryGetProperty "confirmSend" with
                | true, value -> return value.ValueKind = JsonValueKind.True
                | _ -> return false
            | _ -> return false
    with :? JsonException -> return false }

let map (app: WebApplication) (configuration: WorkspaceConfiguration) =
    let summary = configuration.Summary
    let directory = configuration.DataDirectory
    app.MapPost("/api/projects/{projectId:guid}/documents/{documentId:guid}/summary",
                Func<Guid, Guid, HttpContext, Task<IResult>>(fun projectId documentId context -> task {
        match summary with
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
            | Error Summarize.ExtractionError.DocumentNotFound -> return problem 404 "document_not_found" "Document not found" null
            | Error Summarize.ExtractionError.DocumentUnavailable ->
                return problem 503 "document_unavailable" "The document's stored file is missing, unreadable or not the recorded size."
                           "Its library record and associations are unchanged."
            | Error Summarize.ExtractionError.TooManyPages ->
                return problem 422 "too_many_pages" "The document has too many pages"
                           $"Only documents of up to {PdfText.maximumPages} pages can be summarized."
            | Ok pages ->
                match Summaries.readability pages with
                | Error NoReadableText ->
                    return problem 422 "no_text" "The document has no readable text"
                               "No page has a text layer, so there is nothing to summarize. Nothing was sent."
                | Error NotEnoughText ->
                    return problem 422 "not_enough_text" "The document has too little text"
                               "The readable text is too short to summarize. Nothing was sent."
                | Ok readable ->
                    // FR-014: for a service elsewhere nothing is sent without this request's confirmation.
                    // This is the only path to `summarizeOnce`, and it runs after the local checks.
                    let! confirmedSend = confirmed context
                    if ServiceLocation.classify configured.Endpoint.Host = Elsewhere && not confirmedSend then
                        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("LexScholar.Summary")
                            .LogInformation("Summary needs confirmation for {Host}, model {Model}", configured.Endpoint.Authority, configured.Model)
                        return Results.Problem(
                            statusCode = Nullable 409,
                            title = "Confirmation needed",
                            detail = "The text of this document will leave this machine. Nothing was sent.",
                            extensions = dict [ "code", box "confirmation_required"
                                                "serviceHost", box configured.Endpoint.Authority
                                                "model", box configured.Model ])
                    else return! summarizeOnce context configured documentId readable })) |> ignore
