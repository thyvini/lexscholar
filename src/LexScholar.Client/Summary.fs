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

/// What a running request has reported so far.
type Progress =
    | PagesProcessed of processed: int * total: int
    | Combining

type Failure =
    | NotConfigured
    | Unavailable
    | NoText
    | NotEnoughText
    | TooManyPages
    /// The server's own explanation, when it gave one.
    | ServiceFailed of detail: string option
    | RequestFailed
    /// The service is elsewhere and the researcher has not confirmed: its host and the model.
    | ConfirmationRequired of host: string * model: string

type State =
    | Idle
    | Confirming of host: string * model: string
    | Generating of request: int
    | Shown of Generated
    | Failed of Failure

type Model =
    { /// The open document the summary belongs to.
      Project: Guid option
      Document: Guid option
      State: State
      /// The latest progress of the running request, if any.
      Progress: Progress option
      /// The latest request; only its result is used.
      Request: int }

type Message =
    | Summarize
    | Dismiss
    | ConfirmSend
    | CancelSend
    | Progressed of request: int * Progress
    | Responded of request: int * Result<Generated, Failure>
    /// The reader's project and open document, after any change.
    | ReaderChanged of project: Guid option * document: Guid option

/// The service the researcher confirmed, as the dialog showed it.
type Confirmed = { Host: string; Model: string }

type Effect =
    | Send of request: int * project: Guid * document: Guid * confirmed: Confirmed option
    /// Abandon the running request: its fetch is aborted and the server stops (FR-009).
    | Cancel of request: int

let initial = { Project = None; Document = None; State = Idle; Progress = None; Request = 0 }

let update (message: Message) (model: Model) : Model * Effect option =
    // A late result is dropped because only `Generating request` accepts its own number,
    // and every new request takes a fresh one.
    let reset model = { model with State = Idle; Progress = None }
    // Leaving a running request aborts it (FR-009); other states have nothing in flight.
    let abandon model =
        reset model, (match model.State with Generating request -> Some (Cancel request) | _ -> None)
    match message with
    | Summarize ->
        match model.Project, model.Document, model.State with
        | Some project, Some document, (Idle | Shown _ | Failed _) ->
            let request = model.Request + 1
            { model with State = Generating request; Progress = None; Request = request }, Some (Send (request, project, document, None))
        | _ -> model, None
    | Dismiss -> abandon model
    | ConfirmSend ->
        match model.Project, model.Document, model.State with
        | Some project, Some document, Confirming (host, name) ->
            let request = model.Request + 1
            { model with State = Generating request; Progress = None; Request = request }, Some (Send (request, project, document, Some { Host = host; Model = name }))
        | _ -> model, None
    // Cancelling sends nothing: no effect. A late answer is dropped because only `Generating` accepts one.
    | CancelSend ->
        match model.State with
        | Confirming _ -> reset model, None
        | _ -> model, None
    | Progressed (request, progress) when model.State = Generating request -> { model with Progress = Some progress }, None
    | Progressed _ -> model, None
    | Responded (request, result) when model.State = Generating request ->
        match result with
        | Ok generated -> { model with State = Shown generated; Progress = None }, None
        | Error (ConfirmationRequired (host, name)) -> { model with State = Confirming (host, name); Progress = None }, None
        | Error failure -> { model with State = Failed failure; Progress = None }, None
    | ReaderChanged (project, document) when (project, document) <> (model.Project, model.Document) ->
        let abandoned, effect = abandon model
        { abandoned with Project = project; Document = document }, effect
    | _ -> model, None

/// Follows the reader's project and open document; leaving a running request's document cancels it.
let followReader (reader: Reader.Model) (model: Model) : Model * Effect option =
    update (ReaderChanged (reader.Project, reader.Open |> Option.map (fun document -> document.Id))) model

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

/// What the researcher reads for a failure.
let failureText (failure: Failure) : string =
    match failure with
    | NotConfigured ->
        "Summaries need a summarization service. Set LexScholar:Summary:Endpoint and LexScholar:Summary:Model in the app's settings file, then restart LexScholar."
    | Unavailable ->
        "This document's stored file is missing, unreadable or not the recorded size. Its library record and project associations are unchanged."
    | NoText -> "This document has no readable text to summarize (for example, a scanned PDF without a text layer). Nothing was sent."
    | NotEnoughText -> "This document doesn't contain enough readable text for a summary."
    | TooManyPages -> "Documents of more than 100 pages aren't supported yet. Nothing was sent."
    | ServiceFailed _ -> "The summary could not be produced. Please try again."
    | RequestFailed | ConfirmationRequired _ -> "The request failed. Please try again."
