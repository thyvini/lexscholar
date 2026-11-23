module LexScholar.Infrastructure.Tests.LibraryDeletionSchemaTests

open System
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open Microsoft.EntityFrameworkCore.Migrations
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private time = "2026-11-23T12:00:00.0000000Z"
let private previous = "20261112000000_DocumentSummaries"

let private initialized directory =
    WorkspaceDb.initialize directory
    let connection = openConnection directory
    Assert.True(tableExists connection "DeletionOperations", "Deletion operations must be migrated")
    Assert.True(tableExists connection "DeletionItems", "Deletion items must be migrated")
    connection

let private operation (c: SqliteConnection) id =
    execute c $"INSERT INTO DeletionOperations (Id, CreatedUtc) VALUES ('{id:D}', '{time}')"

let private item (c: SqliteConnection) op document status phase =
    execute c $"INSERT INTO DeletionItems (OperationId, DocumentId, OriginalFileName, StorageKey, Status, Phase) VALUES ('{op:D}', '{document:D}', 'sample.pdf', '0123456789abcdef0123456789abcdef', '{status}', '{phase}')"

[<Fact; Trait("Task", "T001")>]
let ``journal enforces identity status and active document uniqueness`` () =
    use dir = new TemporaryDirectory()
    use c = initialized dir.Path
    let a, b, doc = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    operation c a
    operation c b
    item c a doc "pending" "pending"
    assertConstraint (fun () -> item c a doc "pending" "pending")
    assertConstraint (fun () -> item c b doc "pending" "pending")
    assertConstraint (fun () -> item c b (Guid.NewGuid()) "invalid" "pending")
    assertConstraint (fun () -> item c b (Guid.NewGuid()) "incomplete" "invalid")
    assertConstraint (fun () -> item c b (Guid.NewGuid()) "deleted" "pending")
    assertConstraint (fun () -> item c b (Guid.NewGuid()) "pending" "metadata_removed")
    assertConstraint (fun () -> item c (Guid.NewGuid()) (Guid.NewGuid()) "pending" "pending")
    execute c $"UPDATE DeletionItems SET Status = 'deleted', Phase = 'metadata_removed' WHERE OperationId = '{a:D}'"
    item c b doc "metadata_removed" "metadata_removed"
    execute c $"UPDATE DeletionItems SET Status = 'incomplete' WHERE OperationId = '{b:D}'"
    assertConstraint (fun () -> item c a doc "pending" "pending")

[<Fact; Trait("Task", "T001")>]
let ``migration and restart preserve previous library and journal data`` () =
    use dir = new TemporaryDirectory()
    do
        use db = WorkspaceDb.create dir.Path
        db.GetService<IMigrator>().Migrate(previous)
    let workspace, project, document, op = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    do
        use c = openConnection dir.Path
        addWorkspace c workspace
        addProject c project workspace "EXISTING"
        execute c $"INSERT INTO LibraryDocuments (Id, Sha256, StorageKey, OriginalFileName, ByteLength, PageCount, ImportedUtc) VALUES ('{document:D}', 'hash', 'existing', 'existing.pdf', 42, 1, '{time}')"
        execute c $"INSERT INTO ProjectDocuments (ProjectId, DocumentId, Role, AddedUtc) VALUES ('{project:D}', '{document:D}', 'FocusedSource', '{time}')"
        execute c $"INSERT INTO DocumentSummaries (ProjectId, DocumentId, Content, GeneratedAt) VALUES ('{project:D}', '{document:D}', '{{\"version\":1}}', '{time}')"
    WorkspaceDb.initialize dir.Path
    do
        use c = initialized dir.Path
        operation c op
        item c op document "pending" "pending"
    WorkspaceDb.initialize dir.Path
    use c = openConnection dir.Path
    for table in [ "LibraryDocuments"; "ProjectDocuments"; "DocumentSummaries"; "DeletionOperations"; "DeletionItems" ] do
        Assert.Equal(1L, scalar c $"SELECT count(*) FROM {table}" :?> int64)
    use db = WorkspaceDb.create dir.Path
    Assert.Empty(db.Database.GetPendingMigrations())
    Assert.False(db.Database.HasPendingModelChanges())
