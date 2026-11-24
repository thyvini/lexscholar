/// Deletion preview and admission; never composed into the production host yet.
module LexScholar.Server.LibraryDeletionEndpoints

open System
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open LexScholar.Application
open LexScholar.Infrastructure

let private selection (request: HttpRequest) = task {
    try
        use! json = JsonDocument.ParseAsync(request.Body, cancellationToken = request.HttpContext.RequestAborted)
        let mutable ids = Unchecked.defaultof<JsonElement>
        if json.RootElement.ValueKind <> JsonValueKind.Object
           || not (json.RootElement.TryGetProperty("documentIds", &ids))
           || ids.ValueKind <> JsonValueKind.Array then return None
        else
            let parsed =
                ids.EnumerateArray() |> Seq.map (fun item ->
                    if item.ValueKind <> JsonValueKind.String then None
                    else match Guid.TryParse(item.GetString()) with true, id -> Some id | _ -> None) |> Seq.toList
            if parsed |> List.exists Option.isNone then return None
            else return Some (parsed |> List.choose id)
    with :? JsonException -> return None }

let private invalid () = LibraryEndpoints.problem 400 "invalid_selection" "Selecione documentos válidos, sem repetições."

let private confirmation (request: HttpRequest) = task {
    try
        use! json = JsonDocument.ParseAsync(request.Body, cancellationToken = request.HttpContext.RequestAborted)
        let root = json.RootElement
        let mutable operation = Unchecked.defaultof<JsonElement>
        let mutable revision = Unchecked.defaultof<JsonElement>
        let mutable ids = Unchecked.defaultof<JsonElement>
        if root.ValueKind <> JsonValueKind.Object
           || not (root.TryGetProperty("operationId", &operation)) || operation.ValueKind <> JsonValueKind.String
           || not (root.TryGetProperty("revision", &revision)) || revision.ValueKind <> JsonValueKind.String
           || not (root.TryGetProperty("documentIds", &ids)) || ids.ValueKind <> JsonValueKind.Array then return None
        else
            let parsed =
                ids.EnumerateArray() |> Seq.map (fun item ->
                    if item.ValueKind <> JsonValueKind.String then None
                    else match Guid.TryParse(item.GetString()) with true, id -> Some id | _ -> None) |> Seq.toList
            match Guid.TryParse(operation.GetString()) with
            | true, operationId when parsed |> List.forall Option.isSome ->
                return Some (operationId, List.choose id parsed, revision.GetString())
            | _ -> return None
    with :? JsonException -> return None }

let private map (app: WebApplication) (directory: string) (autoStart: bool) (runItem: Guid -> Guid -> Task<Result<unit, LibraryDeletion.MetadataError>>) =
    let coordination = app.Services.GetRequiredService<LibraryCoordination>()
    let operations =
        LibraryDeletion.Operations(coordination, (fun () -> LibraryDeletionRepository.results directory),
                                   LibraryDeletionRepository.acknowledge directory,
                                   LibraryDeletionRepository.recordFailure directory, runItem)
    let missing () = LibraryEndpoints.problem 404 "operation_not_found" "Operação não encontrada."
    app.MapGet("/api/library/deletions", Func<IResult>(fun () ->
        Results.Ok(operations.List()))) |> ignore
    app.MapGet("/api/library/deletions/{operation:guid}", Func<Guid, IResult>(fun operation ->
        match operations.Find operation with
        | Some result -> Results.Ok result
        | None -> missing ())) |> ignore
    app.MapPost("/api/library/deletions/{operation:guid}/retry", Func<Guid, IResult>(fun operation ->
        if operations.Start operation then Results.Accepted(value = {| operationId = operation |})
        else missing ())) |> ignore
    app.MapPost("/api/library/deletions/{operation:guid}/acknowledge", Func<Guid, IResult>(fun operation ->
        match operations.Acknowledge operation with
        | Ok () -> Results.NoContent()
        | Error "operation_not_found" -> missing ()
        | Error code -> LibraryEndpoints.problem 409 code "A exclusão ainda não foi concluída.")) |> ignore
    let admit confirmed : IResult =
        match confirmed with
        | None -> invalid ()
        | Some (operation, ids, revision) ->
            let jobs = (app :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
            match LibraryDeletion.admitFenced jobs (LibraryDeletionRepository.admit coordination directory) operation ids revision with
            | Ok id ->
                if autoStart then operations.Start id |> ignore
                Results.Accepted(value = {| operationId = id |})
            | Error LibraryDeletion.AdmissionError.InvalidSelection -> invalid ()
            | Error LibraryDeletion.AdmissionError.DocumentNotFound ->
                LibraryEndpoints.problem 404 "document_not_found" "Documento não encontrado."
            | Error LibraryDeletion.AdmissionError.OperationConflict ->
                LibraryEndpoints.problem 409 "operation_conflict" "Operação já usada para outra seleção."
            | Error LibraryDeletion.AdmissionError.DeletionInProgress ->
                LibraryEndpoints.problem 409 "deletion_in_progress" "Exclusão já em andamento."
            | Error (LibraryDeletion.AdmissionError.ImpactChanged preview) ->
                Results.Problem(statusCode = Nullable 409, title = "O impacto mudou. Confirme novamente.",
                                extensions = dict [ "code", box "impact_changed"; "preview", box preview ])
    app.MapPost("/api/library/deletions", Func<HttpRequest, Task<IResult>>(fun request -> task {
        let! confirmed = confirmation request
        return admit confirmed })) |> ignore
    app.MapPost("/api/library/deletions/preview", Func<HttpRequest, Task<IResult>>(fun request -> task {
        let! ids = selection request
        match ids with
        | None -> return invalid ()
        | Some ids ->
            match LibraryDeletion.preview (LibraryDeletionRepository.capture directory) ids with
            | Error LibraryDeletion.PreviewError.InvalidSelection -> return invalid ()
            | Error LibraryDeletion.PreviewError.DocumentNotFound ->
                return LibraryEndpoints.problem 404 "document_not_found" "Documento não encontrado."
            | Ok preview -> return Results.Ok(preview) })) |> ignore
    operations

/// Explicit isolated-host seam. Tests may wrap the real item processor in
/// handshakes/fault injection; neither request data nor configuration enables it.
let mapWithProcessingForTests app directory runItem =
    map app directory true runItem

/// Preserve admission-only tests; retry still uses the real fenced lifecycle.
/// Nothing in this module is mapped by production until T017.
let mapForTests (app: WebApplication) directory =
    let coordination = app.Services.GetRequiredService<LibraryCoordination>()
    let jobs = (app :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
    let runItem operation document = task {
        return! LibraryDeletion.processItem coordination jobs
                     (LibraryDeletionRepository.hasItem directory)
                     (LibraryDeletionRepository.removeMetadata coordination directory)
                     (LibraryDeletionRepository.finishOriginal coordination directory)
                      operation document app.Lifetime.ApplicationStopping }
    map app directory false runItem |> ignore
