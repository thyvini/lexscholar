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
