/// The state of a background summary and the status list behind the list markers (F004).
/// Types only until T002 and T010 add the state machines.
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

type Status =
    | Running
    | Finished
    | Failed

/// One entry of `GET /api/summaries`.
type StatusEntry = { ProjectId: System.Guid; DocumentId: System.Guid; Status: Status }

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
        | "running" -> Some (Summary.RemoteRunning None)
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
