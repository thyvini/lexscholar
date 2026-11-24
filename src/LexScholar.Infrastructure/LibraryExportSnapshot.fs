/// A consistent read of export metadata on one SQLite connection.
module LexScholar.Infrastructure.LibraryExportSnapshot

open System.Linq
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open LexScholar.Domain

type Snapshot =
    { Documents: LibraryDocumentRow list
      Projects: ResearchProjectRow list
      Associations: ProjectDocumentRow list
      Summaries: (SummaryKey * StoredSummary) list }

/// The callback runs inside a deferred read transaction whose first read has
/// already established the SQLite snapshot. The context must not escape it.
let withSnapshot directory (read: WorkspaceDb -> 'a) =
    use db = WorkspaceDb.create directory
    db.Database.OpenConnection()
    let connection = db.Database.GetDbConnection() :?> SqliteConnection
    use transaction = connection.BeginTransaction(deferred = true)
    db.Database.UseTransaction(transaction) |> ignore
    db.Set<LibraryDocumentRow>().AsNoTracking().Count() |> ignore
    read db

let read (db: WorkspaceDb) =
    let associations = db.Set<ProjectDocumentRow>().AsNoTracking().ToList() |> Seq.toList
    let projectIds = associations |> List.map (fun item -> item.ProjectId) |> Set.ofList
    { Documents = db.Set<LibraryDocumentRow>().AsNoTracking().ToList() |> Seq.toList
      Projects = db.Set<ResearchProjectRow>().AsNoTracking().ToList() |> Seq.filter (fun project -> projectIds.Contains project.Id) |> Seq.toList
      Associations = associations
      Summaries =
        db.Set<DocumentSummaryRow>().AsNoTracking().ToList()
        |> Seq.map (fun row ->
            let key = { Project = row.ProjectId; Document = row.DocumentId }
            key, SummaryRepository.deserialize key row.GeneratedAt row.Content)
        |> Seq.toList }

let capture directory = withSnapshot directory read
