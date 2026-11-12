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

let update (message: Message) (model: Model) : Model * Effect option =
    // A late result is dropped because only `Generating request` accepts its own number,
    // and every new request takes a fresh one.
    let reset model = { model with State = Idle }
    match message with
    | Summarize ->
        match model.Project, model.Document, model.State with
        | Some project, Some document, (Idle | Shown _ | Failed _) ->
            let request = model.Request + 1
            { model with State = Generating request; Request = request }, Some (Send (request, project, document))
        | _ -> model, None
    | Dismiss -> reset model, None
    | Responded (request, result) when model.State = Generating request ->
        match result with
        | Ok generated -> { model with State = Shown generated }, None
        | Error failure -> { model with State = Failed failure }, None
    | ReaderChanged (project, document) when (project, document) <> (model.Project, model.Document) ->
        { reset model with Project = project; Document = document }, None
    | _ -> model, None

/// Follows the reader's project and open document.
let followReader (reader: Reader.Model) (model: Model) : Model =
    update (ReaderChanged (reader.Project, reader.Open |> Option.map (fun document -> document.Id))) model |> fst

/// Page numbers as ranges for the context line: "pages 1–3, 5" (a single page is "page 5").
let pageRanges (pages: int list) : string =
    let ranges =
        pages
        |> List.distinct
        |> List.sort
        |> List.fold (fun ranges page ->
            match ranges with
            | (first, last) :: rest when page = last + 1 -> (first, page) :: rest
            | _ -> (page, page) :: ranges) []
        |> List.rev
        |> List.map (fun (first, last) -> if first = last then string first else $"{first}–{last}")
    match pages |> List.distinct |> List.length with
    | 1 -> $"page {ranges.Head}"
    | _ -> "pages " + String.Join(", ", ranges)
