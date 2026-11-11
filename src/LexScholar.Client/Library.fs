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
      Error: Failure option }

type Message =
    | LoadLibrary
    | LibraryLoaded of Result<Document list, Failure>
    | ActiveProjectChanged of Guid option
    | ProjectLoaded of Guid * Result<Association list, Failure>
    | Upload of IBrowserFile
    | Uploaded of Result<UploadNotice, Failure>
    | Associate of documentId: Guid * DocumentRole
    | Associated of projectId: Guid * Result<unit, Failure>
    | Remove of documentId: Guid
    | Removed of projectId: Guid * Result<unit, Failure>

type Effect =
    | FetchLibrary
    | FetchProject of Guid
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
      Error = None }

/// Maps an HTTP problem response to a failure; anything unexpected is a generic failure.
let parseFailure (status: int) (body: string) : Failure =
    ignore (status, body)
    RequestFailed

let update (message: Message) (model: Model) : Model * Effect option =
    ignore message
    model, None
