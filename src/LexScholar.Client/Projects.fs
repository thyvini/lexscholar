module LexScholar.Client.Projects

open System

type Project = { Id: Guid; Name: string }
type Snapshot = { Projects: Project list; Active: Guid option }
type Failure = InvalidName | DuplicateName | NotFound | RequestFailed
type Operation = Loading | Creating | Selecting
type Model = { Snapshot: Snapshot; HasLoaded: bool; Name: string; Busy: Operation option; Error: Failure option }
type Message = Reload | Loaded of Result<Snapshot, Failure> | NameChanged of string | Create | Created of Result<unit, Failure> | Select of Guid | Selected of Result<unit, Failure>
type Effect = Fetch | SendCreate of string | SendSelect of Guid
let initial = { Snapshot = { Projects = []; Active = None }; HasLoaded = false; Name = ""; Busy = None; Error = None }
let update (message: Message) (model: Model) : Model * Effect option =
    match message with
    | Reload when model.Busy.IsNone -> { model with Busy = Some Loading; Error = None }, Some Fetch
    | Loaded result when model.Busy = Some Loading ->
        match result with
        | Ok snapshot -> { model with Snapshot = snapshot; HasLoaded = true; Busy = None; Error = None }, None
        | Error error -> { model with Busy = None; Error = Some error }, None
    | NameChanged name when model.Busy.IsNone -> { model with Name = name; Error = None }, None
    | Create when model.Busy.IsNone ->
        if String.IsNullOrWhiteSpace model.Name then { model with Error = Some InvalidName }, None
        else { model with Busy = Some Creating; Error = None }, Some (SendCreate model.Name)
    | Select id when model.Busy.IsNone -> { model with Busy = Some Selecting; Error = None }, Some (SendSelect id)
    | Created result when model.Busy = Some Creating ->
        match result with
        | Ok () -> { model with Name = ""; Busy = Some Loading }, Some Fetch
        | Error error -> { model with Busy = None; Error = Some error }, None
    | Selected result when model.Busy = Some Selecting ->
        match result with
        | Ok () -> { model with Busy = Some Loading }, Some Fetch
        | Error error -> { model with Busy = None; Error = Some error }, None
    | _ -> model, None
