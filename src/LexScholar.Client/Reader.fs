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
      /// The document whose role is being changed; one change at a time.
      ChangingRole: Guid option
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
    | ChangeRole of documentId: Guid * DocumentRole
    | RoleChanged of projectId: Guid * Result<unit, Failure>

type Effect =
    | CheckContent of request: int * contentUrl: string
    | SendRole of projectId: Guid * documentId: Guid * DocumentRole
    /// Reload the active project's documents after its roles changed.
    | RefreshProject

let initial =
    { Project = None; Opening = None; Open = None; ChangingRole = None; Error = None; CheckRequest = 0 }

/// Maps an HTTP problem response to a failure; anything unexpected is a generic failure.
let parseFailure (status: int) (body: string) : Failure =
    ignore (status, body)
    RequestFailed

let update (message: Message) (model: Model) : Model * Effect option =
    ignore message
    model, None
