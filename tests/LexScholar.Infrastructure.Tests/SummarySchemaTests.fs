/// Schema of the stored background summaries (F004 T001, research §5).
module LexScholar.Infrastructure.Tests.SummarySchemaTests

open System
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open Microsoft.EntityFrameworkCore.Migrations
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private documents = "20261111000000_Documents"
let private sha = "0fcd00dc1ad92711575dfde326cda6c56b8fd83b0a01462cab6bfd012dcbe93b"
let private time = "2026-11-12T12:00:00.0000000Z"

let private requireTable (connection: SqliteConnection) =
    Assert.True(tableExists connection "DocumentSummaries", "T001 requires the DocumentSummaries table in the migrated SQLite schema.")

let private initialized directory =
    WorkspaceDb.initialize directory
    let connection = openConnection directory
    try requireTable connection
    with _ -> connection.Dispose(); reraise()
    connection

/// One project with one associated document; returns their ids.
let private associated (connection: SqliteConnection) =
    let workspace, project, document = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    addWorkspace connection workspace
    addProject connection project workspace "ONE"
    execute connection $"INSERT INTO LibraryDocuments (Id, Sha256, StorageKey, OriginalFileName, ByteLength, PageCount, ImportedUtc) VALUES ('{document:D}', '{sha}', 'k-1', 'valid.pdf', 753, 2, '{time}')"
    execute connection $"INSERT INTO ProjectDocuments (ProjectId, DocumentId, Role, AddedUtc) VALUES ('{project:D}', '{document:D}', 'FocusedSource', '{time}')"
    project, document

let private addSummary (connection: SqliteConnection) (project: Guid) (document: Guid) =
    execute connection $"INSERT INTO DocumentSummaries (ProjectId, DocumentId, Content, GeneratedAt) VALUES ('{project:D}', '{document:D}', '{{\"version\":1}}', '{time}')"

let private count (connection: SqliteConnection) table =
    scalar connection $"SELECT count(*) FROM {table}" :?> int64

[<Fact; Trait("Task", "T001")>]
let ``summary table has the documented columns and primary key`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let columns =
        [ use command = connection.CreateCommand()
          command.CommandText <- "SELECT name, type, \"notnull\", pk FROM pragma_table_info('DocumentSummaries') ORDER BY cid"
          use reader = command.ExecuteReader()
          while reader.Read() do yield reader.GetString 0, reader.GetString 1, reader.GetInt64 2, reader.GetInt64 3 ]
    Assert.Equal<(string * string * int64 * int64) list>(
        [ "ProjectId", "TEXT", 1L, 1L; "DocumentId", "TEXT", 1L, 2L; "Content", "TEXT", 1L, 0L; "GeneratedAt", "TEXT", 1L, 0L ], columns)

[<Fact; Trait("Task", "T001")>]
let ``a document has at most one summary per project`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let project, document = associated connection
    addSummary connection project document
    assertConstraint (fun () -> addSummary connection project document)
    Assert.Equal(1L, count connection "DocumentSummaries")

[<Fact; Trait("Task", "T001")>]
let ``a summary needs an existing association`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let project, document = associated connection
    assertConstraint (fun () -> addSummary connection (Guid.NewGuid()) document)
    assertConstraint (fun () -> addSummary connection project (Guid.NewGuid()))
    Assert.Equal(0L, count connection "DocumentSummaries")

[<Fact; Trait("Task", "T001")>]
let ``the foreign key to the association cascades on delete`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let project, document = associated connection
    let rule =
        scalar connection "SELECT on_delete FROM pragma_foreign_key_list('DocumentSummaries') WHERE \"table\" = 'ProjectDocuments' LIMIT 1" :?> string
    Assert.Equal("CASCADE", rule)
    addSummary connection project document
    Assert.Equal(1L, count connection "DocumentSummaries")
    // An EF Core connection, as the host uses: foreign keys are enabled there.
    do
        use db = WorkspaceDb.create directory.Path
        db.Database.ExecuteSqlRaw($"DELETE FROM ProjectDocuments WHERE ProjectId = '{project:D}' AND DocumentId = '{document:D}'") |> ignore
    Assert.Equal(0L, count connection "DocumentSummaries")
    Assert.Equal(1L, count connection "LibraryDocuments")

[<Fact; Trait("Task", "T001")>]
let ``the migration applies to an F005-era workspace and keeps its data`` () =
    use directory = new TemporaryDirectory()
    let project, document =
        do
            use db = WorkspaceDb.create directory.Path
            db.GetService<IMigrator>().Migrate(documents)
        use connection = openConnection directory.Path
        Assert.False(tableExists connection "DocumentSummaries")
        associated connection
    WorkspaceDb.initialize directory.Path
    WorkspaceDb.initialize directory.Path
    use connection = openConnection directory.Path
    requireTable connection
    Assert.Equal(1L, count connection "ProjectDocuments")
    addSummary connection project document
    use db = WorkspaceDb.create directory.Path
    Assert.Empty(db.Database.GetPendingMigrations())
    Assert.False(db.Database.HasPendingModelChanges())
