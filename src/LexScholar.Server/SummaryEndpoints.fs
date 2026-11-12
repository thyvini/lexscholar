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

let private ndjson (event: obj) : IResult =
    Results.Text(JsonSerializer.Serialize(event, JsonSerializerOptions.Web) + "\n", "application/x-ndjson")

let private failed code detail (omitted: int option) : IResult =
    ndjson (box {| ``type`` = "failed"; code = code; detail = detail; omittedPoints = omitted |})

let private summarizeOnce (context: HttpContext) (summary: SummaryConfiguration) documentId (pages: ReadablePages) : Task<IResult> = task {
    // The outgoing handler comes from DI when one is registered (tests), else a plain one.
    let injected = context.RequestServices.GetService<HttpMessageHandler>()
    use client =
        if isNull injected then new HttpClient(new SocketsHttpHandler(AllowAutoRedirect = false), true)
        else new HttpClient(injected, false)
    let settings : ChatCompletions.Settings =
        { Endpoint = summary.Endpoint; Model = summary.Model; ApiKey = summary.ApiKey
          Temperature = summary.Temperature; MaxOutputTokens = summary.MaxOutputTokens }
    let capabilities : Summarize.Capabilities =
        { ExtractPages = fun _ -> failwith "pages are extracted by the endpoint"
          Complete = ChatCompletions.complete client settings
          Report = fun _ -> Task.FromResult() }
    match! Summarize.summarize capabilities pages context.RequestAborted with
    | Ok validated ->
        let covered = pages.Readable |> List.map (fun page -> page.Number)
        return ndjson (box {| ``type`` = "summary"
                              documentId = documentId
                              points = validated.Points |> List.map (fun point -> {| text = point.Text; pages = point.Pages |})
                              pagesCovered = covered
                              unreadablePages = pages.Unreadable
                              omittedPoints = validated.Omitted
                              generatedAt = DateTimeOffset.UtcNow
                              serviceHost = summary.Endpoint.Authority
                              model = summary.Model |})
    | Error (Summarize.SummaryFailure.NotEnoughText omitted) ->
        return failed "not_enough_text" "The service returned no point with valid citations." (Some omitted)
    | Error (Summarize.SummaryFailure.ServiceFailed _) ->
        return failed "service_failed" "The summary service did not give a usable answer. Try again." None }

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
                | Ok readable -> return! summarizeOnce context configured documentId readable })) |> ignore
