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
    /// Her plan's usage limit (or this app's) is reached.
    | UsageLimitReached
    /// The ChatGPT sign-in ended (server event `signed_out` or problem `chatgpt_signed_out`).
    | SignedOut
    /// The account's plan cannot be used by apps; she is signed out.
    | NoPlanAccess
    /// Two destinations exist and none was named.
    | DestinationRequired
    /// What is about to leave this machine and has not been confirmed.
    | ConfirmationRequired of ConfirmTarget

/// Where the text would go: the configured service (host and model) or her ChatGPT plan (account name).
and ConfirmTarget =
    | Service of host: string * model: string
    | Plan of account: string

type Destination =
    | UsePlan
    | UseConfigured

/// Which destinations the host offers right now, and the one she chose when there are two.
type Available = { Plan: bool; Configured: bool }

/// What the server's job registry holds for the open document (`SummaryState` of the contract).
type Remote =
    | RemoteRunning of Progress option
    | RemoteFinished of Generated
    | RemoteFailed of Failure

type State =
    | Idle
    | Confirming of ConfirmTarget
    /// The POST is in flight.
    | Generating of request: int
    /// The server runs the summary in the background; the client polls for it.
    | Running
    | Shown of Generated
    | ConfirmingDelete of Generated
    | Failed of Failure
    /// The DELETE of a finished, failed, running or still-requested summary (the previous state) is in flight: nothing can be asked until it answers.
    | Dismissing of previous: State

type Replacement =
    | AwaitingConsent of account: string
    | Submitting of request: int
    | Replacing
    | Cancelling
    | Reconciling
    | CancellationFailed
    | ReplacementFailed of Failure

type Model =
    { /// The open document the summary belongs to.
      Project: Guid option
      Document: Guid option
      State: State
      Available: Available
      /// Her choice when both destinations are available.
      Choice: Destination option
      /// The latest progress of the running request, if any.
      Progress: Progress option
      /// The latest request; only its result is used.
      Request: int
      /// The latest watch of the open document's job; only its fetches are used.
      Watch: int
      /// The request whose job answer may still be shown after she left and came back; dismissing voids it (0).
      Claim: int
      /// The last dismissal was refused or failed: the summary is still there and she is told.
      DismissFailed: bool
      /// Pairs whose DELETE has not answered yet, also after she left the document: coming back waits for the answer.
      Pending: (Guid * Guid) list
      /// Requests dismissed while their POST was in flight: when the POST answers, the job it may have created is deleted too.
      Draining: (int * Guid * Guid) list
      /// Pairs with a failed DELETE not reported yet: the job may still run, so the dismissal ends as a failure however the other answers
      /// arrive, also after she left the document (reported when she is back).
      Failures: (Guid * Guid) list
      /// Transient draft for this open document only; never browser storage.
      Instruction: string
      Feedback: string
      Replacement: Replacement option
      CancelledReplacements: int list
      ReplacementCancels: int
      Submissions: (int * Guid * Guid) list
      UncertainReplacements: (Guid * Guid) list }

type Message =
    | EditFeedback of string
    | Regenerate of account: string
    | ReplacementAnswered of request: int * project: Guid * document: Guid * Result<Remote, Failure>
    | ReplacementPolled of watch: int * Remote option
    | CancelReplacement
    | ReplacementCancelled of watch: int * project: Guid * document: Guid * succeeded: bool
    | EditInstruction of string
    | Summarize
    | Dismiss
    | Delete
    | ConfirmDelete
    | CancelDelete
    | Close
    /// Stops the running summary on the server.
    | Stop
    /// The answer to the DELETE of the pair's summary (`DeleteSummary`).
    | Dismissed of project: Guid * document: Guid * succeeded: bool
    | ConfirmSend
    | CancelSend
    | DestinationsChanged of Available
    | Choose of Destination
    | Responded of request: int * Result<Generated, Failure>
    /// The POST was answered with a job's state (202, or 200 when one already exists).
    | Accepted of request: int * project: Guid * document: Guid * Remote
    /// The open document's state was asked for after a view change (`Fetch` effect).
    | Refresh
    /// A state fetched for the open document by watch number; `None` is 404 (nothing there).
    | Polled of watch: int * Remote option
    /// The reader's project and open document, after any change.
    | ReaderChanged of project: Guid option * document: Guid option

/// What the researcher confirmed, as the dialog showed it.
type Confirmed =
    | ConfirmedService of host: string * model: string
    | ConfirmedPlan

type Effect =
    | RegenerateSummary of request: int * project: Guid * document: Guid * feedback: string
    | FetchReplacement of watch: int * project: Guid * document: Guid * delay: bool
    | CancelRegeneration of watch: int * project: Guid * document: Guid
    | Send of request: int * project: Guid * document: Guid * destination: Destination option * confirmed: Confirmed option
    | SendWithInstruction of request: int * project: Guid * document: Guid * destination: Destination option * confirmed: Confirmed option * instruction: string
    /// Fetch the open document's state now (`delay` false) or after the polling interval (`delay` true).
    | Fetch of watch: int * project: Guid * document: Guid * delay: bool
    /// Dismissing a finished or failed summary deletes it on the server (F004 FR-010): otherwise asking again returns the old one.
    | DeleteSummary of project: Guid * document: Guid

let initial = { Project = None; Document = None; State = Idle; Available = { Plan = false; Configured = false }; Choice = None; Progress = None; Request = 0; Watch = 0; Claim = 0; DismissFailed = false; Pending = []; Draining = []; Failures = []; Instruction = ""; Feedback = ""; Replacement = None; CancelledReplacements = []; ReplacementCancels = 0; Submissions = []; UncertainReplacements = [] }

let private updateCore (message: Message) (model: Model) : Model * Effect option =
    // A late result is dropped because only `Generating request` accepts its own number,
    // and every new request takes a fresh one.
    // Any change of view or dismissal also ends the watch of the server's job: its late fetches are dropped.
    let reset model = { model with State = Idle; Progress = None; Watch = model.Watch + 1 }
    // Leaving never aborts the POST: the job is the server's (F004), and the answer is kept for `Claim`.
    let abandon model = reset model, None
    // A DELETE or a POST answer of the pair is still outstanding: she cannot ask again until both have answered.
    let outstanding (model: Model) project document =
        List.contains (project, document) model.Pending
        || model.Draining |> List.exists (fun (_, p, d) -> (p, d) = (project, document))
    let drainingOf (model: Model) request =
        model.Draining |> List.tryPick (fun (r, p, d) -> if r = request then Some (p, d) else None)
    let removeOne pair pending =
        match List.tryFindIndex ((=) pair) pending with
        | Some index -> List.removeAt index pending
        | None -> pending
    // Every request takes a fresh number.
    let start project document destination confirmed =
        let request = model.Request + 1
        let effect =
            match LexScholar.Domain.SummaryInstruction.normalize model.Instruction with
            | None -> Send (request, project, document, destination, confirmed)
            | Some instruction -> SendWithInstruction (request, project, document, destination, confirmed, instruction)
        { model with State = Generating request; Progress = None; Request = request; Claim = request }, Some effect
    match message with
    | EditInstruction value when model.State = Idle ->
        let value = if isNull value then "" else value
        if value.Length <= LexScholar.Domain.SummaryInstruction.maximumCharacters then
            { model with Instruction = value }, None
        else model, None
    | Summarize ->
        match model.Project, model.Document, model.State with
        | Some project, Some document, (Idle | Shown _ | Failed _) when model.Available.Plan ->
            start project document (Some UsePlan) None
        | _ -> model, None
    // Stopping a running summary is a DELETE like dismissing; she waits for its answer and cannot ask meanwhile.
    | Stop ->
        match model.Project, model.Document, model.State with
        | Some project, Some document, Running ->
            let abandoned, _ = abandon model
            { abandoned with State = Dismissing Running; Claim = 0; DismissFailed = false; Pending = (project, document) :: model.Pending }, Some (DeleteSummary (project, document))
        | _ -> model, None
    | Delete ->
        match model.State with
        | Shown generated -> { model with State = ConfirmingDelete generated }, None
        | _ -> model, None
    | CancelDelete ->
        match model.State with
        | ConfirmingDelete generated -> { model with State = Shown generated }, None
        | _ -> model, None
    | ConfirmDelete | Close ->
        match model.Project, model.Document, message, model.State with
        | Some project, Some document, ConfirmDelete, ConfirmingDelete generated ->
            let abandoned, _ = abandon model
            { abandoned with State = Dismissing (Shown generated); Claim = 0; DismissFailed = false; Pending = (project, document) :: model.Pending }, Some (DeleteSummary (project, document))
        | Some project, Some document, Close, Failed _ ->
            let abandoned, _ = abandon model
            { abandoned with State = Dismissing model.State; Claim = 0; DismissFailed = false; Pending = (project, document) :: model.Pending }, Some (DeleteSummary (project, document))
        | _ -> model, None
    | Dismiss when (match model.State with Dismissing _ | Shown _ | ConfirmingDelete _ -> true | _ -> false) -> model, None
    | Dismiss ->
        let abandoned, effect = abandon model
        match model.Project, model.Document, model.State with
        // A finished or failed summary is the server's too: dismissing deletes it there, or asking again returns the old one.
        // Until the server answers she sees "Dismissing" and cannot ask, so the old summary can never come back or be
        // deleted after a new one; a failed DELETE restores the summary and says so.
        | Some project, Some document, Failed _ ->
            { abandoned with State = Dismissing model.State; Claim = 0; DismissFailed = false; Pending = (project, document) :: model.Pending }, Some (DeleteSummary (project, document))
        // The POST is in flight and the job may not exist yet: the DELETE goes now, and again when the POST answers (`Draining`).
        | Some project, Some document, Generating request ->
            { abandoned with State = Dismissing model.State; Claim = 0; DismissFailed = false; Pending = (project, document) :: model.Pending; Draining = (request, project, document) :: model.Draining }, Some (DeleteSummary (project, document))
        | _ -> { abandoned with Claim = 0 }, effect
    | Dismissed (project, document, succeeded) ->
        let model =
            { model with
                Pending = removeOne (project, document) model.Pending
                Failures = if succeeded || List.contains (project, document) model.Failures then model.Failures else (project, document) :: model.Failures }
        let failed = List.contains (project, document) model.Failures
        match model.State with
        // Another DELETE or the POST answer of this document is still to come: keep waiting.
        | Dismissing _ when (model.Project, model.Document) = (Some project, Some document) && outstanding model project document -> model, None
        | Dismissing previous when (model.Project, model.Document) = (Some project, Some document) ->
            let model = { model with Failures = model.Failures |> List.filter (fun pair -> pair <> (project, document)) }
            if not failed then { model with State = Idle; DismissFailed = false; Instruction = "" }, None
            else
                match previous with
                // She left and came back meanwhile (or stopped a running or requested summary): ask the server what is there, which also resumes polling.
                | Idle | Running | Generating _ ->
                    let watch = model.Watch + 1
                    { model with State = Idle; DismissFailed = true; Watch = watch }, Some (Fetch (watch, project, document, false))
                | _ -> { model with State = previous; DismissFailed = true }, None
        | _ -> model, None
    | ConfirmSend ->
        match model.Project, model.Document, model.State with
        | Some project, Some document, Confirming (Plan _) -> start project document (Some UsePlan) (Some ConfirmedPlan)
        | _ -> model, None
    // Cancelling sends nothing: no effect. A late answer is dropped because only `Generating` accepts one.
    | CancelSend ->
        match model.State with
        | Confirming _ -> reset model, None
        | _ -> model, None
    | DestinationsChanged available ->
        // A destination that is no longer offered cannot stay chosen.
        let choice =
            match model.Choice with
            | Some UsePlan when not available.Plan -> None
            | Some UseConfigured when not available.Configured -> None
            | choice -> choice
        { model with Available = available; Choice = choice }, None
    // A failure belongs to the previous destination; choosing another lets her ask again.
    | Choose choice ->
        let state = match model.State with Failed _ -> Idle | state -> state
        { model with Choice = Some choice; State = state }, None
    // The POST of a dismissed request answered, with a job or with a failure: a failure does not prove that no job
    // was created (the answer may be lost after the server started it), so the pair is deleted again either way.
    | Accepted (request, _, _, _) | Responded (request, Error _) when drainingOf model request |> Option.isSome ->
        let project, document = drainingOf model request |> Option.get
        { model with Draining = model.Draining |> List.filter (fun (r, _, _) -> r <> request); Pending = (project, document) :: model.Pending }, Some (DeleteSummary (project, document))
    // The POST was answered with the job's state: show it, or wait for it by polling (the job is the server's).
    // Also when she left and came back meanwhile (state Idle): the answer is for the document now open, so it is kept.
    | Accepted (request, project, document, remote) when (model.Project, model.Document) = (Some project, Some document) && (model.State = Generating request || (model.State = Idle && model.Claim = request)) ->
        match remote with
        | RemoteFinished generated -> { model with State = Shown generated; Progress = None; Claim = 0; Instruction = "" }, None
        | RemoteFailed failure -> { model with State = Failed failure; Progress = None; Claim = 0; Instruction = "" }, None
        | RemoteRunning progress ->
            let watch = model.Watch + 1
            { model with State = Running; Progress = progress; Watch = watch; Claim = 0; Instruction = "" }, Some (Fetch (watch, project, document, true))
    // Back on a document (or asked after a view change): the state of its job, if the server has one.
    | Refresh ->
        match model.Project, model.Document, model.State with
        // Her delete of this summary has not answered yet: wait for it rather than fetch what is being deleted.
        | Some project, Some document, Idle when outstanding model project document ->
            { model with State = Dismissing Idle }, None
        // A delete failed while she was away: the job may still run, so say so and ask what is there.
        | Some project, Some document, Idle when List.contains (project, document) model.Failures ->
            let watch = model.Watch + 1
            { model with Watch = watch; DismissFailed = true; Failures = model.Failures |> List.filter (fun pair -> pair <> (project, document)) }, Some (Fetch (watch, project, document, false))
        | Some project, Some document, Idle ->
            let watch = model.Watch + 1
            { model with Watch = watch }, Some (Fetch (watch, project, document, false))
        | _ -> model, None
    | Polled (watch, remote) when watch = model.Watch && (model.State = Idle || model.State = Running) ->
        match remote, model.Project, model.Document with
        | Some (RemoteRunning progress), Some project, Some document -> { model with State = Running; Progress = progress; Instruction = "" }, Some (Fetch (watch, project, document, true))
        | Some (RemoteFinished generated), _, _ -> { model with State = Shown generated; Progress = None; Instruction = "" }, None
        | Some (RemoteFailed failure), _, _ -> { model with State = Failed failure; Progress = None }, None
        | _ -> { model with State = Idle; Progress = None }, None
    | Polled _ -> model, None
    | Accepted _ -> model, None
    | Responded (request, result) when model.State = Generating request ->
        match result with
        | Ok generated -> { model with State = Shown generated; Progress = None }, None
        | Error (ConfirmationRequired target) -> { model with State = Confirming target; Progress = None }, None
        | Error failure -> { model with State = Failed failure; Progress = None }, None
    | ReaderChanged (project, document) when (project, document) <> (model.Project, model.Document) ->
        let abandoned, effect = abandon model
        { abandoned with Project = project; Document = document; DismissFailed = false; Instruction = "" }, effect
    | _ -> model, None

/// Replacement jobs never enter the legacy destructive dismissal lifecycle.
let update message model =
    let uncertain model =
        match model.Project, model.Document with
        | Some project, Some document -> List.contains (project, document) model.UncertainReplacements
        | _ -> false
    let submission model =
        model.Submissions |> List.tryPick (fun (request, project, document) ->
            if (model.Project, model.Document) = (Some project, Some document) then Some request else None)
    let editable model =
        match model.State, model.Replacement with
        | Shown _, (None | Some Reconciling | Some (ReplacementFailed _)) -> true
        | _ -> false
    let observe model remote =
        match remote, model.Project, model.Document with
        | Some (RemoteRunning progress), Some project, Some document ->
            { model with Replacement = Some (if uncertain model then CancellationFailed else Replacing); Progress = progress; Feedback = "" },
            Some (FetchReplacement (model.Watch, project, document, true))
        | Some (RemoteFinished generated), _, _ ->
            let feedback = if model.State = Shown generated then model.Feedback else ""
            { model with State = Shown generated; Replacement = (if uncertain model then Some CancellationFailed else None); Progress = None; Feedback = feedback }, None
        | Some (RemoteFailed failure), _, _ ->
            { model with Replacement = Some (if uncertain model then CancellationFailed else ReplacementFailed failure); Progress = None }, None
        | _ -> { model with Replacement = (if uncertain model then Some CancellationFailed else None); Progress = None }, None
    match message with
    | EditFeedback value when editable model ->
        let value = if isNull value then "" else value
        if value.Length <= LexScholar.Domain.SummaryInstruction.maximumCharacters then { model with Feedback = value }, None
        else model, None
    | Regenerate account when model.Available.Plan && not (uncertain model) && (submission model).IsNone && model.CancelledReplacements.IsEmpty && model.ReplacementCancels = 0 && (match model.State, model.Replacement with Shown _, (None | Some (ReplacementFailed _)) -> true | _ -> false) ->
        { model with Replacement = Some (AwaitingConsent account) }, None
    | ConfirmSend when (match model.Replacement with Some (AwaitingConsent _) -> true | _ -> false) ->
        match model.Project, model.Document with
        | Some project, Some document ->
            let request = model.Request + 1
            { model with Replacement = Some (Submitting request); Request = request; Watch = model.Watch + 1; Submissions = (request, project, document) :: model.Submissions },
            Some (RegenerateSummary (request, project, document, model.Feedback))
        | _ -> model, None
    | CancelSend when (match model.Replacement with Some (AwaitingConsent _) -> true | _ -> false) ->
        { model with Replacement = None }, None
    | CancelReplacement ->
        match model.Project, model.Document, model.Replacement with
        | Some project, Some document, Some (Submitting _ | Replacing | CancellationFailed) ->
            let cancelled = match model.Replacement with Some (Submitting request) -> request :: model.CancelledReplacements | _ -> model.CancelledReplacements
            let watch = model.Watch + 1
            { model with Replacement = Some Cancelling; Watch = watch; CancelledReplacements = cancelled; ReplacementCancels = model.ReplacementCancels + 1; UncertainReplacements = (project, document) :: model.UncertainReplacements },
            Some (CancelRegeneration (watch, project, document))
        | _ -> model, None
    | ReplacementAnswered (request, project, document, _) when List.contains request model.CancelledReplacements ->
        { model with CancelledReplacements = List.filter ((<>) request) model.CancelledReplacements; ReplacementCancels = model.ReplacementCancels + 1; Submissions = model.Submissions |> List.filter (fun (r, _, _) -> r <> request) },
        Some (CancelRegeneration (model.Watch, project, document))
    | ReplacementAnswered (request, project, document, result) when List.contains (request, project, document) model.Submissions ->
        let updated = { model with Submissions = model.Submissions |> List.filter (fun (r, _, _) -> r <> request) }
        let active = (model.Project, model.Document) = (Some project, Some document)
        match result with
        // A lost response does not prove admission failed. Fence pending registration before any retry.
        | Error RequestFailed ->
            let watch = if active then model.Watch + 1 else model.Watch
            { updated with Watch = watch; Replacement = (if active then Some Cancelling else updated.Replacement); ReplacementCancels = updated.ReplacementCancels + 1; UncertainReplacements = (project, document) :: updated.UncertainReplacements },
            Some (CancelRegeneration (watch, project, document))
        | _ when not active -> updated, None
        | _ ->
            let updated = { updated with Watch = updated.Watch + 1 }
            match updated.State, result with
            | Shown _, Ok remote -> observe updated (Some remote)
            | Shown _, Error failure -> { updated with Replacement = Some (ReplacementFailed failure) }, None
            | _ -> { updated with Replacement = None }, Some (Fetch (updated.Watch, project, document, false))
    | ReplacementPolled (watch, remote) when watch = model.Watch && (submission model).IsNone && (match model.State, model.Replacement with Shown _, (None | Some Replacing | Some Reconciling | Some CancellationFailed) -> true | _ -> false) -> observe model remote
    | ReplacementCancelled (watch, project, document, succeeded) ->
        let model =
            { model with
                ReplacementCancels = max 0 (model.ReplacementCancels - 1)
                UncertainReplacements = if succeeded then model.UncertainReplacements |> List.filter ((<>) (project, document)) else (project, document) :: model.UncertainReplacements }
        if (model.Project, model.Document) <> (Some project, Some document) then model, None
        elif watch <> model.Watch && model.Replacement <> Some CancellationFailed && model.Replacement <> Some Cancelling then model, None
        elif succeeded && (model.ReplacementCancels > 0 || not model.CancelledReplacements.IsEmpty) then model, None
        elif succeeded then
            let watch = model.Watch + 1
            { model with Replacement = Some Reconciling; Watch = watch }, Some (FetchReplacement (watch, project, document, false))
        else { model with Replacement = Some CancellationFailed }, Some (FetchReplacement (model.Watch, project, document, false))
    | ReplacementAnswered _ | ReplacementPolled _ -> model, None
    | Delete when (match model.Replacement with Some (ReplacementFailed _) -> true | _ -> false) ->
        updateCore Delete { model with Replacement = None }
    | Delete | Summarize | Dismiss | Close | Stop when model.Replacement.IsSome -> model, None
    | ReaderChanged (project, document) when (project, document) <> (model.Project, model.Document) ->
        updateCore message { model with Replacement = None; Feedback = "" }
    | _ ->
        let changed, effect = updateCore message model
        match message, changed.State, effect, changed.Project, changed.Document with
        | (Polled _ | Accepted _), Shown _, None, Some project, Some document ->
            match submission changed with
            | Some request -> { changed with Replacement = Some (Submitting request) }, None
            | None ->
                let replacement = if uncertain changed then Some CancellationFailed else Some Reconciling
                { changed with Replacement = replacement }, Some (FetchReplacement (changed.Watch, project, document, false))
        | _ -> changed, effect

/// Follows the active project and opened document, fetching the summary job on change.
let followOpened (library: Library.Model) (model: Model) : Model * Effect list =
    let moved, cancel = update (ReaderChanged (library.Project, library.Opened)) model
    let refreshed, fetch = if moved = model then moved, None else update Refresh moved
    refreshed, List.choose id [ cancel; fetch ]

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
    | NotConfigured -> "Entre no ChatGPT para gerar resumos."
    | Unavailable ->
        "This document's stored file is missing, unreadable or not the recorded size. Its library record and project associations are unchanged."
    | NoText -> "This document has no readable text to summarize (for example, a scanned PDF without a text layer). Nothing was sent."
    | NotEnoughText -> "This document doesn't contain enough readable text for a summary."
    | TooManyPages -> "Documents of more than 100 pages aren't supported yet. Nothing was sent."
    | ServiceFailed (Some detail) -> detail
    | ServiceFailed None -> "The summary could not be produced. Please try again."
    | UsageLimitReached -> "Usage limit reached. Review your plan or this app's limit in ChatGPT settings."
    | SignedOut -> "You are signed out of ChatGPT. Sign in again to summarize with your plan."
    | NoPlanAccess -> "This ChatGPT account's plan can't be used by apps, so you are signed out."
    | DestinationRequired -> "Choose where to send the text, then try again."
    | RequestFailed | ConfirmationRequired _ -> "The request failed. Please try again."

/// Whether a failure means the ChatGPT sign-in is gone, so the destinations must be loaded again.
let reloadsDestinations (failure: Failure) : bool =
    match failure with
    | NotConfigured | SignedOut | NoPlanAccess -> true
    | _ -> false

/// Whether the failure is answered by offering the ChatGPT sign-in.
let offersSignIn (failure: Failure) : bool =
    match failure with
    | NotConfigured | SignedOut | NoPlanAccess -> true
    | _ -> false
