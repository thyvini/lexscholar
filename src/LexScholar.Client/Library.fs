/// Shared library and the active project's associated documents: pure state,
/// no HTTP and no rendering (see App.fs).
module LexScholar.Client.Library

open System
open Microsoft.AspNetCore.Components.Forms
open LexScholar.Domain

type Document = { Id: Guid; FileName: string; PageCount: int; ContentUrl: string }
type Association = { Document: Document; Role: DocumentRole }

/// Purpose-specific deletion state. A lost admission answer retains the exact
/// confirmed request; neither a failed lookup nor a 404 authorizes a new UUID.
module Deletion =
    type Project = { Id: Guid; Name: string; Role: string; HasSummary: bool }
    type Document = { Id: Guid; Name: string; Projects: Project list }
    type Preview = { Documents: Document list; Revision: string }
    type Confirmation = { OperationId: Guid; DocumentIds: Guid list; Revision: string }
    type Item = { DocumentId: Guid; Name: string; Outcome: string; Reason: string }
    type Outcome = { OperationId: Guid; Status: string; Items: Item list }
    type Failure = Unavailable | ImpactChanged of Preview
    type Model =
        { Selected: Set<Guid>; Preview: Preview option; Pending: Confirmation option
          Busy: bool; Uncertain: bool; Changed: bool; Error: bool
          Outcomes: Outcome list; Request: int; Restored: bool }
    type Message =
        | Select of Guid
        | Preview
        | Previewed of Result<Preview, Failure>
        | Cancel
        | Confirm of Guid
        | Confirmed of Guid * Result<unit, Failure>
        | RepeatConfirmation
        | Load
        | Loaded of int * Result<Outcome list, Failure> * Confirmation option
        | Retry of Guid
        | Acknowledge of Guid
        | Mutated of Result<unit, Failure>
    type Effect =
        | FetchPreview of Guid list
        | Submit of Confirmation
        | Fetch of int * delayed: bool
        | RetryOperation of Guid
        | AcknowledgeOperation of Guid
    let initial =
        { Selected = Set.empty; Preview = None; Pending = None; Busy = false
          Uncertain = false; Changed = false; Error = false; Outcomes = []; Request = 0; Restored = false }
    let private fetch delayed model =
        let request = model.Request + 1
        { model with Request = request }, Some (Fetch (request, delayed))
    let update message model =
        match message with
        | Select id when model.Restored && not model.Busy && model.Preview.IsNone && model.Pending.IsNone ->
            { model with Selected = if model.Selected.Contains id then model.Selected.Remove id else model.Selected.Add id }, None
        | Preview when model.Restored && not model.Busy && model.Pending.IsNone && not model.Selected.IsEmpty ->
            { model with Busy = true; Error = false; Changed = false }, Some (FetchPreview (Set.toList model.Selected))
        | Previewed result when model.Busy && model.Pending.IsNone ->
            match result with
            | Ok preview -> { model with Busy = false; Preview = Some preview }, None
            | Error _ -> { model with Busy = false; Error = true }, None
        | Cancel when not model.Busy && model.Pending.IsNone ->
            { model with Preview = None; Changed = false; Error = false }, None
        | Confirm id when model.Restored && not model.Busy && model.Preview.IsSome && model.Pending.IsNone ->
            let preview = model.Preview.Value
            let pending = { OperationId = id; DocumentIds = preview.Documents |> List.map _.Id; Revision = preview.Revision }
            { model with Busy = true; Pending = Some pending; Error = false }, Some (Submit pending)
        | RepeatConfirmation when not model.Busy && model.Pending.IsSome ->
            { model with Busy = true; Error = false }, Some (Submit model.Pending.Value)
        | Confirmed (id, result) when model.Pending |> Option.exists (fun p -> p.OperationId = id) ->
            match result with
            | Ok () ->
                fetch false { model with Busy = false; Pending = None; Preview = None; Uncertain = false; Selected = Set.empty }
            | Error (ImpactChanged preview) ->
                { model with Busy = false; Pending = None; Preview = Some preview; Uncertain = false; Changed = true }, None
            | Error _ -> { model with Busy = false; Uncertain = true; Error = true }, None
        | Load -> fetch false model
        | Loaded (request, result, restored) when request = model.Request ->
            let model =
                if model.Restored then model
                else
                    { model with Restored = Result.isOk result
                                 Pending = model.Pending |> Option.orElse restored
                                 Uncertain = model.Uncertain || restored.IsSome }
            match result with
            | Error _ -> { model with Error = true }, None
            | Ok outcomes ->
                let model = { model with Outcomes = outcomes; Error = model.Error && model.Pending.IsSome }
                if outcomes |> List.exists (fun o -> o.Status = "running") then fetch true model
                else model, None
        | Retry id when not model.Busy && (model.Outcomes |> List.exists (fun o -> o.OperationId = id && o.Status = "partial")) ->
            { model with Busy = true; Error = false }, Some (RetryOperation id)
        | Acknowledge id when not model.Busy && (model.Outcomes |> List.exists (fun o -> o.OperationId = id && o.Status = "complete")) ->
            { model with Busy = true; Error = false }, Some (AcknowledgeOperation id)
        | Mutated result ->
            fetch false { model with Busy = false; Error = Result.isError result }
        | _ -> model, None

type ExportFailure =
    | EmptyExport
    | ExportDocumentUnavailable of string
    | ExportRequestFailed

type ExportState =
    | ExportIdle
    | Exporting
    | ExportReceived
    | ExportError of ExportFailure

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
      ExportState: ExportState
      Deletion: Deletion.Model
      HasLoadedLibrary: bool
      LoadingLibrary: bool
      /// The active project whose associations are shown.
      Project: Guid option
      Associations: Association list
      Opened: Guid option
      Options: Guid option
      ChangingRole: Guid option
      RoleStale: bool
      RoleError: Failure option
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
    | DeletionMsg of Deletion.Message
    | Export
    | Exported of Result<unit, ExportFailure>
    | LoadLibrary
    | LibraryLoaded of request: int * Result<Document list, Failure>
    | ActiveProjectChanged of Guid option
    | ProjectLoaded of projectId: Guid * request: int * Result<Association list, Failure>
    /// Reloads the library and the active project's documents, for example to retry after a failure.
    | Refresh
    /// Reloads only the active project's documents, for example after a role change.
    | ReloadProject
    | OpenDocument of Guid
    | RevealOptions of Guid
    | HideOptions
    | ChangeRole of Guid * DocumentRole
    | RoleChanged of Guid * Result<unit, Failure>
    | Upload of IBrowserFile
    | Uploaded of Result<UploadNotice, Failure>
    | Associate of documentId: Guid * DocumentRole
    | Associated of projectId: Guid * Result<unit, Failure>
    | Remove of documentId: Guid
    | Removed of projectId: Guid * documentId: Guid * Result<unit, Failure>

type Effect =
    | DeleteEffect of Deletion.Effect
    | SendExport
    | FetchLibrary of request: int
    | FetchProject of projectId: Guid * request: int
    /// Both lists at once, for Refresh.
    | FetchAll of library: int * project: (Guid * int) option
    | SendUpload of IBrowserFile
    | SendAssociate of projectId: Guid * documentId: Guid * DocumentRole
    | SendRemove of projectId: Guid * documentId: Guid
    | SendRole of projectId: Guid * documentId: Guid * DocumentRole

let initial =
    { Documents = []
      ExportState = ExportIdle
      Deletion = Deletion.initial
      HasLoadedLibrary = false
      LoadingLibrary = false
      Project = None
      Associations = []
      Opened = None
      Options = None
      ChangingRole = None
      RoleStale = false
      RoleError = None
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

let parseExportFailure status (body: string) =
    try
        use json = System.Text.Json.JsonDocument.Parse(body)
        let root = json.RootElement
        match status, root.GetProperty("code").GetString() with
        | 409, "empty_library" -> EmptyExport
        | 409, "document_unavailable" ->
            match root.GetProperty("name").GetString() with
            | null | "" -> ExportRequestFailed
            | name -> ExportDocumentUnavailable name
        | _ -> ExportRequestFailed
    with _ -> ExportRequestFailed

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
    | DeletionMsg message ->
        let deletion, effect = Deletion.update message model.Deletion
        // Drop only documents whose durable result says deleted; incomplete
        // items may already lack metadata, but must never be called deleted.
        let deleted =
            deletion.Outcomes |> List.collect _.Items
            |> List.filter (fun i -> i.Outcome = "deleted") |> List.map _.DocumentId |> Set.ofList
        { model with Deletion = deletion
                     Documents = model.Documents |> List.filter (fun d -> not (deleted.Contains d.Id))
                     Associations = model.Associations |> List.filter (fun a -> not (deleted.Contains a.Document.Id))
                     Opened = model.Opened |> Option.filter (deleted.Contains >> not)
                     Options = model.Options |> Option.filter (deleted.Contains >> not) },
        effect |> Option.map DeleteEffect
    | Export when model.ExportState <> Exporting ->
        { model with ExportState = Exporting }, Some SendExport
    | Exported result when model.ExportState = Exporting ->
        { model with ExportState = match result with Ok () -> ExportReceived | Error failure -> ExportError failure }, None
    | LoadLibrary ->
        let model, request = nextLibrary model
        model, Some (FetchLibrary request)
    | LibraryLoaded (request, result) when request = model.LibraryRequest ->
        match result with
        | Ok documents -> { model with Documents = documents; HasLoadedLibrary = true; LoadingLibrary = false }, None
        | Error error -> { model with LoadingLibrary = false; Error = Some error }, None
    | ActiveProjectChanged project when project <> model.Project ->
        // Clear at once so one project's documents never appear under another.
        let model = { model with Project = project; Associations = []; Opened = None; Options = None; RoleStale = model.ChangingRole.IsSome; RoleError = None; HasLoadedProject = false; LoadingProject = false }
        match project with
        | None -> model, None
        | Some projectId ->
            let model, request = nextProject model
            model, Some (FetchProject (projectId, request))
    | ProjectLoaded (projectId, request, result) when model.Project = Some projectId && request = model.ProjectRequest ->
        match result with
        | Ok associations ->
            let opened = model.Opened |> Option.filter (fun id -> associations |> List.exists (fun item -> item.Document.Id = id))
            { model with Associations = associations; Opened = opened; Options = model.Options |> Option.filter (fun id -> opened = Some id); HasLoadedProject = true; LoadingProject = false }, None
        | Error error -> { model with LoadingProject = false; Error = Some error }, None
    | Refresh ->
        let model, library = nextLibrary { model with Error = None }
        match model.Project with
        | None -> model, Some (FetchAll (library, None))
        | Some projectId ->
            let model, project = nextProject model
            model, Some (FetchAll (library, Some (projectId, project)))
    | ReloadProject when model.Project.IsSome ->
        let model, request = nextProject model
        model, Some (FetchProject (model.Project.Value, request))
    | OpenDocument id when model.Project.IsSome && (model.Associations |> List.exists (fun item -> item.Document.Id = id)) ->
        { model with Opened = Some id; Options = None; RoleError = None }, None
    | RevealOptions id when model.Opened = Some id && (model.Associations |> List.exists (fun item -> item.Document.Id = id)) ->
        { model with Options = Some id }, None
    | HideOptions -> { model with Options = None }, None
    | ChangeRole (id, role) when model.Project.IsSome && model.ChangingRole.IsNone ->
        { model with ChangingRole = Some id; RoleStale = false; RoleError = None },
        Some (SendRole (model.Project.Value, id, role))
    | RoleChanged (projectId, result) when model.ChangingRole.IsSome ->
        let stale = model.RoleStale
        let model = { model with ChangingRole = None; RoleStale = false }
        if model.Project <> Some projectId then model, None
        else
            match result with
            | Ok () ->
                let model, request = nextProject model
                model, Some (FetchProject (projectId, request))
            | Error _ when stale -> model, None
            | Error error -> { model with RoleError = Some error }, None
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
    | Removed (projectId, documentId, result) when model.Busy = Some Removing ->
        // The server removed it: drop it now rather than waiting for the reload,
        // which may fail, so the opened document closes at once.
        let model =
            match result with
            | Ok () when model.Project = Some projectId ->
                { model with Associations = model.Associations |> List.filter (fun item -> item.Document.Id <> documentId)
                             Opened = if model.Opened = Some documentId then None else model.Opened
                             Options = if model.Options = Some documentId then None else model.Options }
            | _ -> model
        mutated projectId result model
    | _ -> model, None
