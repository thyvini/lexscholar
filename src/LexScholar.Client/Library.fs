/// Shared library and the active project's associated documents: pure state,
/// no HTTP and no rendering (see App.fs). Reader concerns live elsewhere.
module LexScholar.Client.Library

open System
open Microsoft.AspNetCore.Components.Forms
open LexScholar.Domain

type Document = { Id: Guid; FileName: string; PageCount: int }
type Association = { Document: Document; Role: DocumentRole }

type Failure =
    | NotAPdf
    | CorruptPdf
    | EncryptedPdf
    | TooLarge
    | TooManyPages
    | DuplicateAssociation
    | InvalidRole
    | NotFound
    | DocumentNotFound
    | RequestFailed

/// The outcome of a successful upload.
type UploadNotice =
    | Imported of Document
    | Reused of Document

/// A mutation in progress; only one runs at a time.
type Operation =
    | Uploading
    | Associating
    | Removing

type Model =
    { Documents: Document list
      HasLoadedLibrary: bool
      LoadingLibrary: bool
      /// The active project whose associations are shown.
      Project: Guid option
      Associations: Association list
      HasLoadedProject: bool
      LoadingProject: bool
      Busy: Operation option
      Notice: UploadNotice option
      Error: Failure option
      /// The latest request number issued for each list. Only the response to
      /// the latest request is shown, so an older response arriving last cannot
      /// overwrite a newer list.
      LibraryRequest: int
      ProjectRequest: int }

type Message =
    | LoadLibrary
    | LibraryLoaded of request: int * Result<Document list, Failure>
    | ActiveProjectChanged of Guid option
    | ProjectLoaded of projectId: Guid * request: int * Result<Association list, Failure>
    /// Reloads the library and the active project's documents, for example to retry after a failure.
    | Refresh
    | Upload of IBrowserFile
    | Uploaded of Result<UploadNotice, Failure>
    | Associate of documentId: Guid * DocumentRole
    | Associated of projectId: Guid * Result<unit, Failure>
    | Remove of documentId: Guid
    | Removed of projectId: Guid * Result<unit, Failure>

type Effect =
    | FetchLibrary of request: int
    | FetchProject of projectId: Guid * request: int
    /// Both lists at once, for Refresh.
    | FetchAll of library: int * project: (Guid * int) option
    | SendUpload of IBrowserFile
    | SendAssociate of projectId: Guid * documentId: Guid * DocumentRole
    | SendRemove of projectId: Guid * documentId: Guid

let initial =
    { Documents = []
      HasLoadedLibrary = false
      LoadingLibrary = false
      Project = None
      Associations = []
      HasLoadedProject = false
      LoadingProject = false
      Busy = None
      Notice = None
      Error = None
      LibraryRequest = 0
      ProjectRequest = 0 }

/// Maps an HTTP problem response to a failure; anything unexpected is a generic failure.
let parseFailure (status: int) (body: string) : Failure =
    try
        use json = System.Text.Json.JsonDocument.Parse(body)
        let mutable code = Unchecked.defaultof<System.Text.Json.JsonElement>
        if json.RootElement.ValueKind = System.Text.Json.JsonValueKind.Object
           && json.RootElement.TryGetProperty("code", &code)
           && code.ValueKind = System.Text.Json.JsonValueKind.String then
            match status, code.GetString() with
            | (400 | 422), "not_a_pdf" -> NotAPdf
            | 422, "corrupt_pdf" -> CorruptPdf
            | 422, "encrypted_pdf" -> EncryptedPdf
            | 413, "upload_too_large" -> TooLarge
            | 422, "page_limit_exceeded" -> TooManyPages
            | 409, "duplicate_association" -> DuplicateAssociation
            | 400, "invalid_role" -> InvalidRole
            | 404, "not_found" -> NotFound
            | 404, "document_not_found" -> DocumentNotFound
            | _ -> RequestFailed
        else RequestFailed
    with _ -> RequestFailed

/// Starts a new library request; earlier ones become stale.
let private nextLibrary model =
    let request = model.LibraryRequest + 1
    { model with LibraryRequest = request; LoadingLibrary = true }, request

/// Starts a new request for the active project's documents; earlier ones become stale.
let private nextProject model =
    let request = model.ProjectRequest + 1
    { model with ProjectRequest = request; LoadingProject = true }, request

/// Refreshes the active project after a successful mutation for it. A result
/// for a project that is no longer active changes nothing that is shown.
let private mutated (projectId: Guid) (result: Result<unit, Failure>) model =
    let model = { model with Busy = None }
    match result with
    | _ when model.Project <> Some projectId -> model, None
    | Ok () ->
        let model, request = nextProject model
        model, Some (FetchProject (projectId, request))
    | Error error -> { model with Error = Some error }, None

let update (message: Message) (model: Model) : Model * Effect option =
    match message with
    | LoadLibrary ->
        let model, request = nextLibrary model
        model, Some (FetchLibrary request)
    | LibraryLoaded (_, Ok documents) ->
        { model with Documents = documents; HasLoadedLibrary = true; LoadingLibrary = false }, None
    | LibraryLoaded (_, Error error) -> { model with LoadingLibrary = false; Error = Some error }, None
    | ActiveProjectChanged project when project <> model.Project ->
        // Clear at once so one project's documents never appear under another.
        let model = { model with Project = project; Associations = []; HasLoadedProject = false; LoadingProject = false }
        match project with
        | None -> model, None
        | Some projectId ->
            let model, request = nextProject model
            model, Some (FetchProject (projectId, request))
    | ProjectLoaded (projectId, _, result) when model.Project = Some projectId ->
        match result with
        | Ok associations -> { model with Associations = associations; HasLoadedProject = true; LoadingProject = false }, None
        | Error error -> { model with LoadingProject = false; Error = Some error }, None
    | Upload file when model.Busy.IsNone ->
        { model with Busy = Some Uploading; Notice = None; Error = None }, Some (SendUpload file)
    | Uploaded result when model.Busy = Some Uploading ->
        match result with
        | Ok notice ->
            let model, request = nextLibrary { model with Busy = None; Notice = Some notice }
            model, Some (FetchLibrary request)
        | Error error -> { model with Busy = None; Error = Some error }, None
    | Associate (documentId, role) when model.Busy.IsNone && model.Project.IsSome ->
        { model with Busy = Some Associating; Notice = None; Error = None },
        Some (SendAssociate (model.Project.Value, documentId, role))
    | Associated (projectId, result) when model.Busy = Some Associating -> mutated projectId result model
    | Remove documentId when model.Busy.IsNone && model.Project.IsSome ->
        { model with Busy = Some Removing; Notice = None; Error = None }, Some (SendRemove (model.Project.Value, documentId))
    | Removed (projectId, result) when model.Busy = Some Removing -> mutated projectId result model
    | _ -> model, None
