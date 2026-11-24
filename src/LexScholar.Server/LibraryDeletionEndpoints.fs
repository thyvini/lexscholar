/// Deletion preview; intentionally not composed into the production host.
module LexScholar.Server.LibraryDeletionEndpoints

open System
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
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

/// Only isolated test hosts call this until the complete deletion controls ship.
let mapForTests (app: WebApplication) (directory: string) =
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
