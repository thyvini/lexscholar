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

/// Only isolated test hosts call this until the complete deletion controls ship.
let mapForTests (app: WebApplication) (directory: string) =
    let coordination = app.Services.GetRequiredService<LibraryCoordination>()
    app.MapPost("/api/library/deletions", Func<HttpRequest, Task<IResult>>(fun request -> task {
        match! confirmation request with
        | None -> return invalid ()
        | Some (operation, ids, revision) ->
            match LibraryDeletionRepository.admit coordination directory operation ids revision with
            | Ok id -> return Results.Accepted(value = {| operationId = id |})
            | Error LibraryDeletion.AdmissionError.InvalidSelection -> return invalid ()
            | Error LibraryDeletion.AdmissionError.DocumentNotFound ->
                return LibraryEndpoints.problem 404 "document_not_found" "Documento não encontrado."
            | Error LibraryDeletion.AdmissionError.OperationConflict ->
                return LibraryEndpoints.problem 409 "operation_conflict" "Operação já usada para outra seleção."
            | Error LibraryDeletion.AdmissionError.DeletionInProgress ->
                return LibraryEndpoints.problem 409 "deletion_in_progress" "Exclusão já em andamento."
            | Error (LibraryDeletion.AdmissionError.ImpactChanged preview) ->
                return Results.Problem(statusCode = Nullable 409, title = "O impacto mudou. Confirme novamente.",
                                       extensions = dict [ "code", box "impact_changed"; "preview", box preview ]) })) |> ignore
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
