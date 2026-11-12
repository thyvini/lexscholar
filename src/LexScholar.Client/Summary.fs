/// Requesting, showing and dismissing a generated summary of the open document:
/// pure state, no HTTP and no rendering (see App.fs).
module LexScholar.Client.Summary

open System

type Point = { Text: string; Pages: int list }

/// A summary as the endpoint's `summary` event reports it, with the context needed to reproduce it.
type Generated =
    { DocumentId: Guid
      Points: Point list
      PagesCovered: int list
      UnreadablePages: int list
      OmittedPoints: int
      GeneratedAt: DateTimeOffset
      ServiceHost: string
      Model: string }

type Failure =
    | NotConfigured
    | Unavailable
    | NoText
    | NotEnoughText
    | TooManyPages
    | ServiceFailed
    | RequestFailed

type State =
    | Idle
    | Generating of request: int
    | Shown of Generated
    | Failed of Failure

type Model =
    { /// The open document the summary belongs to.
      Project: Guid option
      Document: Guid option
      State: State
      /// The latest request; only its result is used.
      Request: int }

type Message =
    | Summarize
    | Dismiss
    | Responded of request: int * Result<Generated, Failure>
    /// The reader's project and open document, after any change.
    | ReaderChanged of project: Guid option * document: Guid option

type Effect = Send of request: int * project: Guid * document: Guid

let initial = { Project = None; Document = None; State = Idle; Request = 0 }

let update (message: Message) (model: Model) : Model * Effect option = model, None
