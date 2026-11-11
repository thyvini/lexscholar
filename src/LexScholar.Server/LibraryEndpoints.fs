/// Shared library listing, PDF import and per-project associations (T031).
module LexScholar.Server.LibraryEndpoints

open System
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.AspNetCore.WebUtilities
open Microsoft.Net.Http.Headers
open LexScholar.Domain
open LexScholar.Application
open LexScholar.Infrastructure

let problem (status: int) code title : IResult =
    Results.Problem(statusCode = Nullable(status), title = title, extensions = dict [ "code", box code ])

let private roleName role =
    match role with
    | FocusedSource -> "FocusedSource"
    | ReferenceSource -> "ReferenceSource"

let private parseRole (value: string) =
    match value with
    | "FocusedSource" -> Some FocusedSource
    | "ReferenceSource" -> Some ReferenceSource
    | _ -> None

/// The `ProjectDocument` schema; the role is serialized by name.
let associationDto (view: ProjectDocumentView) =
    {| projectId = view.ProjectId; document = view.Document; role = roleName view.Role; addedUtc = view.AddedUtc |}

let private rejection reason =
    match reason with
    | TooLarge maxBytes -> problem 413 "upload_too_large" $"The file is larger than the configured limit of {maxBytes} bytes."
    | NotPdf -> problem 422 "not_a_pdf" "The file is not a PDF."
    | Corrupt -> problem 422 "corrupt_pdf" "The PDF is damaged or cannot be read."
    | Encrypted -> problem 422 "encrypted_pdf" "Encrypted or password-protected PDFs are not supported."
    | TooManyPages maxPages -> problem 422 "page_limit_exceeded" $"The PDF has more pages than the configured limit of {maxPages}."

let private noFile () = problem 400 "not_a_pdf" "Upload one PDF in the multipart form field 'file'."

/// The first multipart section, if it is the `file` field. Later sections are
/// never read: moving to another section would drain this one without a bound.
/// The preamble and section headers stay within MultipartReader's default limits.
let private firstFileSection (request: HttpRequest) = task {
    match MediaTypeHeaderValue.TryParse(request.ContentType) with
    | true, mediaType when mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase) ->
        let boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value
        if String.IsNullOrWhiteSpace(boundary) then return None
        else
            let reader = MultipartReader(boundary, request.Body)
            let! section = reader.ReadNextSectionAsync(request.HttpContext.RequestAborted)
            if isNull section then return None
            else
                match ContentDispositionHeaderValue.TryParse(section.ContentDisposition) with
                | true, disposition when disposition.IsFileDisposition()
                                         && HeaderUtilities.RemoveQuotes(disposition.Name).Value = "file" ->
                    let fileName =
                        if disposition.FileNameStar.HasValue then disposition.FileNameStar.Value
                        else HeaderUtilities.RemoveQuotes(disposition.FileName).Value
                    return Some (section.Body, fileName)
                | _ -> return None
    | _ -> return None }

/// The role and document id of an association request, if present and valid.
let associationRequest (request: HttpRequest) = task {
    try
        use! json = JsonDocument.ParseAsync(request.Body, cancellationToken = request.HttpContext.RequestAborted)
        let root = json.RootElement
        let text (name: string) =
            let mutable value = Unchecked.defaultof<JsonElement>
            if root.ValueKind = JsonValueKind.Object && root.TryGetProperty(name, &value) && value.ValueKind = JsonValueKind.String
            then Some (value.GetString()) else None
        let role = text "role" |> Option.bind parseRole
        let documentId =
            text "documentId" |> Option.bind (fun value ->
                match Guid.TryParse(value) with
                | true, id -> Some id
                | _ -> None)
        return role, documentId
    with :? JsonException -> return None, None }

let map (app: WebApplication) (configuration: WorkspaceConfiguration) =
    let directory = configuration.DataDirectory
    let store : Library.Store =
        { ListDocuments = fun () -> LibraryRepository.list directory
          ListForProject = LibraryRepository.listForProject directory
          Associate = LibraryRepository.associate directory
          Remove = LibraryRepository.removeAssociation directory }
    // Created once per host and shared by every request: each import function
    // owns one serialized commit section, so concurrent identical uploads must
    // go through the same one.
    let import =
        ImportDocument.create
            { Limits = configuration.ImportLimits
              Validate = PdfValidator.validate
              OpenStagedWrite = DocumentStore.openStagedWrite directory
              OpenStagedRead = DocumentStore.openStagedRead directory
              Promote = DocumentStore.promote directory
              Discard = DocumentStore.discard directory
              OpenOriginal = DocumentStore.openOriginal directory
              FindBySha256 = LibraryRepository.findBySha256 directory
              Insert = LibraryRepository.insert directory
              Now = fun () -> DateTimeOffset.UtcNow }

    app.MapGet("/api/documents", Func<IResult>(fun () -> Results.Ok(Library.list store))) |> ignore

    app.MapPost("/api/documents", Func<HttpContext, Task<IResult>>(fun context -> task {
        // Kestrel's default 30,000,000-byte request limit is below the default
        // import limit, so it is lifted for this request only. The upload stays
        // bounded: only the first multipart section is read, its headers are
        // limited by MultipartReader, and the import stops reading the file
        // after MaxBytes + 1 bytes, answering 413 without reading the rest.
        let sizeLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>()
        if not (isNull sizeLimit) && not sizeLimit.IsReadOnly then sizeLimit.MaxRequestBodySize <- Nullable()
        match! firstFileSection context.Request with
        | None -> return noFile ()
        | Some (file, fileName) ->
            match! import file fileName context.RequestAborted with
            | Error reason -> return rejection reason
            | Ok outcome ->
                let document = Library.documentView outcome.Document
                if outcome.Reused then return Results.Ok(document)
                else return Results.Created(document.ContentUrl, document) })) |> ignore

    app.MapGet("/api/projects/{projectId:guid}/documents", Func<Guid, IResult>(fun projectId ->
        match Library.listForProject store projectId with
        | Ok views -> Results.Ok(views |> List.map associationDto)
        | Error Library.AssociationError.ProjectNotFound -> problem 404 "not_found" "Project not found"
        // Listing only checks that the project exists.
        | Error other -> invalidOp $"Listing a project's documents cannot fail with {other}.")) |> ignore

    app.MapPost("/api/projects/{projectId:guid}/documents", Func<Guid, HttpContext, Task<IResult>>(fun projectId context -> task {
        match! associationRequest context.Request with
        | None, _ -> return problem 400 "invalid_role" "Choose a role: FocusedSource or ReferenceSource."
        | Some _, None -> return problem 404 "document_not_found" "Document not found"
        | Some role, Some documentId ->
            match Library.associate store projectId documentId role with
            | Ok view -> return Results.Created($"/api/projects/{projectId:D}/documents/{documentId:D}", associationDto view)
            | Error Library.AssociationError.ProjectNotFound -> return problem 404 "not_found" "Project not found"
            | Error Library.AssociationError.DocumentNotFound -> return problem 404 "document_not_found" "Document not found"
            | Error Library.AssociationError.DuplicateAssociation ->
                return problem 409 "duplicate_association" "This document is already associated with the project." })) |> ignore

    app.MapDelete("/api/projects/{projectId:guid}/documents/{documentId:guid}", Func<Guid, Guid, IResult>(fun projectId documentId ->
        match Library.remove store projectId documentId with
        | Ok () -> Results.NoContent()
        | Error Library.RemovalError.AssociationNotFound -> problem 404 "not_found" "Association not found")) |> ignore
