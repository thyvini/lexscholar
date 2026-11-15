/// The state of a background summary and the status list behind the list markers (F004).
/// Types only until T002 and T010 add the state machines.
module LexScholar.Client.SummaryStatus

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

/// The job state of `GET .../summary` or of an answered POST (`SummaryState` of the contract), as the open
/// document's summary reads it; None for a body that is not one.
let parseRemote (body: string) : Summary.Remote option =
    try
        use json = System.Text.Json.JsonDocument.Parse body
        let root = json.RootElement
        match root.GetProperty("status").GetString() with
        | "running" -> Some Summary.RemoteRunning
        | "finished" -> Some (Summary.RemoteFinished (SummaryStream.generated (root.GetProperty "summary")))
        | "failed" ->
            match SummaryStream.failureOfCode (root.GetProperty("code").GetString()) with
            | Summary.ServiceFailed _ ->
                let detail =
                    match root.TryGetProperty "detail" with
                    | true, value when value.ValueKind = System.Text.Json.JsonValueKind.String -> Some (value.GetString())
                    | _ -> None
                Some (Summary.RemoteFailed (Summary.ServiceFailed detail))
            | failure -> Some (Summary.RemoteFailed failure)
        | _ -> None
    with _ -> None
