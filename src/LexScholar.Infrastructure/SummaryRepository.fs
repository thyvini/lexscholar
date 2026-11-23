/// Stored finished summaries (F004, research §5): one row per (project, document) in `DocumentSummaries`.
/// `Content` is the F003 summary as JSON with `"version": 1`; the time is the row's own column. Only a validated
/// `DocumentSummary` is ever written. The functions are synchronous: the job registry calls them inside its lock.
module LexScholar.Infrastructure.SummaryRepository

open System
open System.IO
open System.Linq
open System.Text.Json
open Microsoft.EntityFrameworkCore
open LexScholar.Domain

let private version = 1

let private serialize (summary: StoredSummary) : string =
    JsonSerializer.Serialize(
        {| version = version
           points = summary.Points |> List.map (fun point -> {| text = point.Text; pages = point.Pages |})
           pagesCovered = summary.PagesCovered
           unreadablePages = summary.UnreadablePages
           omittedPoints = summary.OmittedPoints
           serviceHost = summary.ServiceHost
           model = summary.Model |})

let private ints (element: JsonElement) = [ for item in element.EnumerateArray() -> item.GetInt32() ]

/// Reads a stored row; a content version this build does not know is refused rather than guessed at.
let internal deserialize (key: SummaryKey) (generatedAt: DateTimeOffset) (content: string) : StoredSummary =
    use json = JsonDocument.Parse content
    let root = json.RootElement
    match root.TryGetProperty "version" with
    | true, found when found.ValueKind = JsonValueKind.Number && found.GetInt32() = version ->
        { DocumentId = key.Document
          Points = [ for point in root.GetProperty("points").EnumerateArray() ->
                        { Text = point.GetProperty("text").GetString(); Pages = ints (point.GetProperty "pages") } ]
          PagesCovered = ints (root.GetProperty "pagesCovered")
          UnreadablePages = ints (root.GetProperty "unreadablePages")
          OmittedPoints = root.GetProperty("omittedPoints").GetInt32()
          GeneratedAt = generatedAt
          ServiceHost = root.GetProperty("serviceHost").GetString()
          Model = root.GetProperty("model").GetString() }
    | _ -> raise (InvalidDataException "The stored summary has a content version this build does not support.")

let private rowsOf (db: WorkspaceDb) (key: SummaryKey) =
    query { for row in db.Set<DocumentSummaryRow>() do
            where (row.ProjectId = key.Project && row.DocumentId = key.Document)
            select row }

/// Stores the summary for the pair, replacing any earlier one.
let save (dataDirectory: string) (key: SummaryKey) (summary: StoredSummary) : unit =
    use db = WorkspaceDb.create dataDirectory
    match rowsOf db key |> Seq.tryHead with
    | Some row ->
        row.Content <- serialize summary
        row.GeneratedAt <- summary.GeneratedAt
    | None ->
        db.Add(DocumentSummaryRow(ProjectId = key.Project, DocumentId = key.Document, Content = serialize summary, GeneratedAt = summary.GeneratedAt)) |> ignore
    db.SaveChanges() |> ignore

let find (dataDirectory: string) (key: SummaryKey) : StoredSummary option =
    use db = WorkspaceDb.create dataDirectory
    rowsOf db key |> Seq.tryHead |> Option.map (fun row -> deserialize key row.GeneratedAt row.Content)

/// Deletes the pair's summary; nothing there is not an error.
let delete (dataDirectory: string) (key: SummaryKey) : unit =
    use db = WorkspaceDb.create dataDirectory
    (rowsOf db key).ExecuteDelete() |> ignore

let listKeys (dataDirectory: string) : SummaryKey list =
    use db = WorkspaceDb.create dataDirectory
    query { for row in db.Set<DocumentSummaryRow>().AsNoTracking() do select (row.ProjectId, row.DocumentId) }
    |> Seq.map (fun (project, document) -> { Project = project; Document = document })
    |> Seq.toList
