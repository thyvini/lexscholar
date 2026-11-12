/// Summary of a document (F003, contracts/summary-api.yaml). T001 has the route
/// only: 503 without a configured service, 501 with one until T002 onward.
module LexScholar.Server.SummaryEndpoints

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

let private problem status code title detail : IResult =
    Results.Problem(statusCode = Nullable(status), title = title, detail = detail, extensions = dict [ "code", box code ])

let map (app: WebApplication) (configuration: WorkspaceConfiguration) =
    let summary = configuration.Summary
    app.MapPost("/api/projects/{projectId:guid}/documents/{documentId:guid}/summary",
                Func<Guid, Guid, IResult>(fun projectId documentId ->
        // Route values are bound by name; T002 uses them.
        ignore (projectId, documentId)
        match summary with
        | None ->
            problem 503 "summary_service_not_configured" "No summary service is configured"
                "Set LexScholar:Summary:Endpoint and LexScholar:Summary:Model (see the quickstart) and restart LexScholar."
        | Some _ -> problem 501 "not_implemented" "Summaries are not available yet" null)) |> ignore
