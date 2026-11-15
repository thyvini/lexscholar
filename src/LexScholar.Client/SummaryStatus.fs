/// The state of a background summary and the status list behind the list markers (F004).
/// 
module LexScholar.Client.SummaryStatus

open System.Text.Json

type Point = { Text: string; Pages: int list }

type Finished =
    { Points: Point list
      PagesCovered: int list
      UnreadablePages: int list
      OmittedPoints: int
      GeneratedAt: System.DateTimeOffset
      ServiceHost: string
      Model: string }

/// `SummaryState` of the contract.
type SummaryState =
    | Running of pagesProcessed: int option * totalPages: int option * combining: bool
    | Finished of Finished
    | Failed of code: string * detail: string option * omittedPoints: int option

type Status = LexScholar.Domain.SummaryStatus

/// One entry of `GET /api/summaries`.
type StatusEntry = { ProjectId: System.Guid; DocumentId: System.Guid; Status: Status }

/// The list markers' state: the last statuses read, and the number of the polling loop that may continue.
type Statuses = { Entries: StatusEntry list; Watch: int }

type StatusMessage =
    | Refresh
    | StatusesLoaded of watch: int * Result<StatusEntry list, unit>

type StatusEffect = FetchStatuses of watch: int * delay: bool

let initialStatuses = { Entries = []; Watch = 0 }

let private anyRunning (entries: StatusEntry list) = entries |> List.exists (fun entry -> entry.Status = Status.Running)

/// A refresh starts a new polling loop (its number is `Watch`); a loop whose number is no longer current ends at its next answer.
/// While any summary runs, the loop asks again after the polling delay; when none runs it stops (research §4).
let updateStatuses (message: StatusMessage) (model: Statuses) : Statuses * StatusEffect option =
    match message with
    | Refresh ->
        let model = { model with Watch = model.Watch + 1 }
        model, Some (FetchStatuses (model.Watch, false))
    | StatusesLoaded (watch, _) when watch <> model.Watch -> model, None
    | StatusesLoaded (watch, result) ->
        let model = match result with Ok entries -> { model with Entries = entries } | Error () -> model
        model, (if anyRunning model.Entries then Some (FetchStatuses (watch, true)) else None)

/// A change of the active project or the open document fetches the statuses again (AR 4: a tab that polls nothing
/// catches up here).
let followView (before: System.Guid option * System.Guid option) (after: System.Guid option * System.Guid option) (model: Statuses) : Statuses * StatusEffect option =
    if before = after then model, None else updateStatuses Refresh model

/// The status list of `GET /api/summaries`; None for a body that is not one.
let parseStatuses (body: string) : StatusEntry list option =
    try
        use json = JsonDocument.Parse body
        let status (name: string) =
            match name with
            | "running" -> Status.Running
            | "finished" -> Status.Finished
            | _ -> Status.Failed
        [ for element in json.RootElement.EnumerateArray() ->
            { ProjectId = element.GetProperty("projectId").GetGuid()
              DocumentId = element.GetProperty("documentId").GetGuid()
              Status = status (element.GetProperty("status").GetString()) } ]
        |> Some
    with _ -> None

let documentStatus (entries: StatusEntry list) (project: System.Guid) (document: System.Guid) : Status option =
    entries |> List.tryFind (fun entry -> entry.ProjectId = project && entry.DocumentId = document) |> Option.map (fun entry -> entry.Status)

let projectStatus (entries: StatusEntry list) (project: System.Guid) : Status option =
    entries |> List.filter (fun entry -> entry.ProjectId = project) |> List.map (fun entry -> entry.Status) |> LexScholar.Domain.Summaries.rollUp

// Reading the summary endpoint's JSON (error bodies, job states) and writing its request, moved here from the
// NDJSON reader (F004 T003). Pure parsing, no HTTP.

let private intList (element: JsonElement) =
    element.EnumerateArray() |> Seq.map (fun value -> value.GetInt32()) |> Seq.toList

let failureOfCode (code: string) : Summary.Failure =
    match code with
    | "summary_service_not_configured" -> Summary.NotConfigured
    | "document_unavailable" -> Summary.Unavailable
    | "no_text" -> Summary.NoText
    | "not_enough_text" -> Summary.NotEnoughText
    | "too_many_pages" -> Summary.TooManyPages
    | "service_failed" -> Summary.ServiceFailed None
    | "usage_limit_reached" -> Summary.UsageLimitReached
    // The problem and the event mean the same: the sign-in is gone.
    | "signed_out" | "chatgpt_signed_out" -> Summary.SignedOut
    | "no_plan_access" -> Summary.NoPlanAccess
    | "destination_required" -> Summary.DestinationRequired
    | _ -> Summary.RequestFailed

let private isPlan (root: JsonElement) =
    match root.TryGetProperty "destination" with
    | true, value -> value.ValueKind = JsonValueKind.String && value.GetString() = "chatgpt"
    | _ -> false

/// An error response's failure. A 409 `confirmation_required` carries the host and
/// model to show; anything else maps by its `code`. An unreadable body is a plain failure.
let failureOfBody (body: string) : Summary.Failure =
    try
        use json = JsonDocument.Parse(body)
        let root = json.RootElement
        match root.GetProperty("code").GetString() with
        | "confirmation_required" when isPlan root ->
            Summary.ConfirmationRequired (Summary.Plan (root.GetProperty("account").GetString()))
        | "confirmation_required" ->
            Summary.ConfirmationRequired (Summary.Service (root.GetProperty("serviceHost").GetString(), root.GetProperty("model").GetString()))
        | code -> failureOfCode code
    with _ -> Summary.RequestFailed

let generated (element: JsonElement) : Summary.Generated =
    { DocumentId = element.GetProperty("documentId").GetGuid()
      Points =
          element.GetProperty("points").EnumerateArray()
          |> Seq.map (fun point -> { Summary.Text = point.GetProperty("text").GetString(); Summary.Pages = intList (point.GetProperty "pages") })
          |> Seq.toList
      PagesCovered = intList (element.GetProperty "pagesCovered")
      UnreadablePages = intList (element.GetProperty "unreadablePages")
      OmittedPoints = element.GetProperty("omittedPoints").GetInt32()
      GeneratedAt = element.GetProperty("generatedAt").GetDateTimeOffset()
      ServiceHost = element.GetProperty("serviceHost").GetString()
      Model = element.GetProperty("model").GetString() }

/// `confirmSend` names the service the researcher confirmed, so the server can check it is still the configured one.
let requestBody (destination: Summary.Destination option) (confirmed: Summary.Confirmed option) : string =
    let destinationName =
        match destination with
        | Some Summary.UsePlan -> Some "chatgpt"
        | Some Summary.UseConfigured -> Some "configured"
        | None -> None
    let fields =
        [ yield! destinationName |> Option.map (fun name -> "destination", box name) |> Option.toList
          match confirmed with
          | Some (Summary.ConfirmedService (host, model)) ->
              yield "confirmSend", box true
              yield "confirmedHost", box host
              yield "confirmedModel", box model
          | Some Summary.ConfirmedPlan -> yield "confirmSend", box true
          | None -> yield "confirmSend", box false ]
    JsonSerializer.Serialize(dict fields)

/// The job state of `GET .../summary` or of an answered POST (`SummaryState` of the contract), as the open
/// document's summary reads it; None for a body that is not one.
let parseRemote (body: string) : Summary.Remote option =
    try
        use json = System.Text.Json.JsonDocument.Parse body
        let root = json.RootElement
        match root.GetProperty("status").GetString() with
        | "running" ->
            let number name =
                match root.TryGetProperty (name: string) with
                | true, value when value.ValueKind = System.Text.Json.JsonValueKind.Number -> Some (value.GetInt32())
                | _ -> None
            let combining =
                match root.TryGetProperty "combining" with
                | true, value -> value.ValueKind = System.Text.Json.JsonValueKind.True
                | _ -> false
            match number "pagesProcessed", number "totalPages" with
            | Some processed, Some total -> Some (Summary.RemoteRunning (Some (Summary.PagesProcessed (processed, total))))
            | _ when combining -> Some (Summary.RemoteRunning (Some Summary.Combining))
            | _ -> Some (Summary.RemoteRunning None)
        | "finished" -> Some (Summary.RemoteFinished (generated (root.GetProperty "summary")))
        | "failed" ->
            match failureOfCode (root.GetProperty("code").GetString()) with
            | Summary.ServiceFailed _ ->
                let detail =
                    match root.TryGetProperty "detail" with
                    | true, value when value.ValueKind = System.Text.Json.JsonValueKind.String -> Some (value.GetString())
                    | _ -> None
                Some (Summary.RemoteFailed (Summary.ServiceFailed detail))
            | failure -> Some (Summary.RemoteFailed failure)
        | _ -> None
    with _ -> None
