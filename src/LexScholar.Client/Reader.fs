/// Opening a project's document in the browser's native PDF viewer and changing
/// its project-specific role: pure state, no HTTP and no rendering (see App.fs).
module LexScholar.Client.Reader

open System
open LexScholar.Domain

/// A document to open: its content URL comes from the API, never built here.
type Document = { Id: Guid; FileName: string; ContentUrl: string }

type Failure =
    | Unavailable
    | DocumentNotFound
    | AssociationNotFound
    | InvalidRole
    | RequestFailed

type Model =
    { /// The active project; opened documents and role changes belong to it.
      Project: Guid option
      /// A document whose content is being checked before it is shown.
      Opening: Document option
      /// The document shown in the viewer.
      Open: Document option
      /// The page a citation asked for, if any; the frame shows it.
      Page: int option
      /// Counts page requests so the frame is re-created even for a repeated page.
      Navigation: int
      /// The document whose role is being changed; one change at a time.
      ChangingRole: Guid option
      /// Errors from a pending mutation on a previous project visit must not update this visit.
      RoleStale: bool
      Error: Failure option
      /// The latest content check; only its result is used.
      CheckRequest: int }

type Message =
    | ActiveProjectChanged of Guid option
    /// The documents now associated with the active project.
    | AssociationsChanged of Guid list
    | Open of Document
    | ContentChecked of request: int * Result<unit, Failure>
    | Close
    /// Show a cited page of the open document.
    | ShowPage of int
    | ChangeRole of documentId: Guid * DocumentRole
    | RoleChanged of projectId: Guid * Result<unit, Failure>

type Effect =
    | CheckContent of request: int * contentUrl: string
    | SendRole of projectId: Guid * documentId: Guid * DocumentRole
    /// Reload the active project's documents after its roles changed.
    | RefreshProject

let initial =
    { Project = None; Opening = None; Open = None; Page = None; Navigation = 0; ChangingRole = None; RoleStale = false; Error = None; CheckRequest = 0 }

/// Maps an HTTP problem response to a failure; anything unexpected is a generic failure.
let parseFailure (status: int) (body: string) : Failure =
    try
        use json = System.Text.Json.JsonDocument.Parse(body)
        match status, json.RootElement.GetProperty("code").GetString() with
        | 503, "document_unavailable" -> Unavailable
        | 404, "document_not_found" -> DocumentNotFound
        | 404, "not_found" -> AssociationNotFound
        | 400, "invalid_role" -> InvalidRole
        | _ -> RequestFailed
    with _ -> RequestFailed

let update (message: Message) (model: Model) : Model * Effect option =
    let close model =
        { model with Open = None; Opening = None; Page = None; CheckRequest = model.CheckRequest + 1; Error = None }
    match message with
    | ActiveProjectChanged project when project <> model.Project ->
        { close model with Project = project; RoleStale = model.ChangingRole.IsSome }, None
    | AssociationsChanged ids when
        [ model.Open; model.Opening ] |> List.exists (Option.exists (fun document -> not (List.contains document.Id ids))) ->
        close model, None
    | Open document when model.Project.IsSome ->
        let request = model.CheckRequest + 1
        { model with Opening = Some document; Open = None; Page = None; Error = None; CheckRequest = request },
        Some (CheckContent (request, document.ContentUrl))
    | ContentChecked (request, result) when request = model.CheckRequest && model.Opening.IsSome ->
        match result with
        | Ok () -> { model with Open = model.Opening; Opening = None }, None
        | Error error -> { model with Opening = None; Error = Some error }, None
    | Close -> close model, None
    | ShowPage page when model.Open.IsSome -> { model with Page = Some page; Navigation = model.Navigation + 1 }, None
    | ChangeRole (documentId, role) when model.Project.IsSome && model.ChangingRole.IsNone ->
        { model with ChangingRole = Some documentId; RoleStale = false; Error = None },
        Some (SendRole (model.Project.Value, documentId, role))
    | RoleChanged (projectId, result) when model.ChangingRole.IsSome ->
        let stale = model.RoleStale
        let model = { model with ChangingRole = None; RoleStale = false }
        if model.Project <> Some projectId then model, None
        else
            match result with
            // A return visit may have fetched associations before this PATCH committed.
            // Reload authoritative state rather than applying the old response to this visit.
            | Ok () -> model, Some RefreshProject
            | Error _ when stale -> model, None
            | Error error -> { model with Error = Some error }, None
    | _ -> model, None

/// Follows the library's active project and, once loaded, its associations:
/// the viewer closes when its project changes or its document leaves the list.
let followLibrary (library: Library.Model) (model: Model) : Model =
    let model, _ = update (ActiveProjectChanged library.Project) model
    if library.HasLoadedProject then
        update (AssociationsChanged (library.Associations |> List.map (fun item -> item.Document.Id))) model |> fst
    else model
