/// Summary of a document (F003, contracts/summary-api.yaml). T002 runs the local
/// checks, in contract order, before anything could be sent; a document that
/// passes them gets 501 until T003 adds the service call.
module LexScholar.Server.SummaryEndpoints

open System
open System.Threading.Tasks
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
        | Some _ ->
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
                | Ok _ -> return problem 501 "not_implemented" "Summaries are not available yet" null })) |> ignore
