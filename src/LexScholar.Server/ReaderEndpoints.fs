/// Reading and classifying sources (T038): project-scoped role changes and
/// inline, range-capable delivery of stored originals.
module LexScholar.Server.ReaderEndpoints

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open LexScholar.Application
open LexScholar.Infrastructure

let map (app: WebApplication) (configuration: WorkspaceConfiguration) =
    let directory = configuration.DataDirectory
    let changeRole = DocumentRoles.changeRole (LibraryRepository.changeRole directory)
    let openContent = VerifiedDocumentContent.openRead directory

    app.MapPatch("/api/projects/{projectId:guid}/documents/{documentId:guid}",
                 Func<Guid, Guid, HttpContext, Task<IResult>>(fun projectId documentId context -> task {
        match! LibraryEndpoints.associationRequest context.Request with
        | None, _ -> return LibraryEndpoints.problem 400 "invalid_role" "Choose a role: FocusedSource or ReferenceSource."
        | Some role, _ ->
            match changeRole projectId documentId role with
            | Ok view -> return Results.Ok(LibraryEndpoints.associationDto view)
            | Error DocumentRoles.RoleChangeError.AssociationNotFound ->
                return LibraryEndpoints.problem 404 "not_found" "Association not found" })) |> ignore

    app.MapGet("/api/documents/{documentId:guid}/content", Func<Guid, HttpContext, Task<IResult>>(fun documentId context -> task {
        match! openContent documentId context.RequestAborted with
        | Error DocumentNotFound -> return LibraryEndpoints.problem 404 "document_not_found" "Document not found"
        | Error DocumentUnavailable ->
            return LibraryEndpoints.problem 503 "document_unavailable"
                       "The document's stored file is missing or unreadable. Its library record and associations are unchanged."
        | Ok content ->
            context.Response.Headers.ContentDisposition <- "inline"
            context.Response.Headers.XContentTypeOptions <- "nosniff"
            // Disposes the stream after the response; handles Range, 206 and 416.
            return Results.Stream(content.Stream, "application/pdf", enableRangeProcessing = true) })) |> ignore
