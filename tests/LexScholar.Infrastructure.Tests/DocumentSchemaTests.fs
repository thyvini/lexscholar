module LexScholar.Infrastructure.Tests.DocumentSchemaTests

open System
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open Microsoft.EntityFrameworkCore.Migrations
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private initial = "20261108000000_Initial"
let private projects = "20261110000000_Projects"
let private documents = "20261111000000_Documents"
let private sha = "0fcd00dc1ad92711575dfde326cda6c56b8fd83b0a01462cab6bfd012dcbe93b"

let private requireSchema (connection: SqliteConnection) =
    for table in [ "LibraryDocuments"; "ProjectDocuments" ] do
        Assert.True(tableExists connection table, $"T025 requires the {table} table in the migrated SQLite schema.")

let private initialized directory =
    WorkspaceDb.initialize directory
    let connection = openConnection directory
    try requireSchema connection
    with _ -> connection.Dispose(); reraise()
    connection

let private addDocumentWith (connection: SqliteConnection) (id: Guid) (hash: string) (storageKey: string) (byteLength: int64) (pageCount: int) =
    execute connection $"INSERT INTO LibraryDocuments (Id, Sha256, StorageKey, OriginalFileName, ByteLength, PageCount, ImportedUtc) VALUES ('{id:D}', '{hash}', '{storageKey}', 'valid.pdf', {byteLength}, {pageCount}, '2026-11-09T12:00:00.0000000Z')"

let private addDocument connection id storageKey = addDocumentWith connection id sha storageKey 753L 2

let private associate (connection: SqliteConnection) (project: Guid) (document: Guid) (role: string) =
    execute connection $"INSERT INTO ProjectDocuments (ProjectId, DocumentId, Role, AddedUtc) VALUES ('{project:D}', '{document:D}', '{role}', '2026-11-09T12:00:00.0000000Z')"

/// A workspace with one project; returns the project id.
let private addProjectInWorkspace connection =
    let workspace, project = Guid.NewGuid(), Guid.NewGuid()
    addWorkspace connection workspace
    addProject connection project workspace "ONE"
    project

let private count (connection: SqliteConnection) table =
    scalar connection $"SELECT count(*) FROM {table}" :?> int64

[<Fact; Trait("Task", "T025")>]
let ``fresh database applies the document migration and snapshot matches`` () =
    use directory = new TemporaryDirectory()
    WorkspaceDb.initialize directory.Path
    use connection = openConnection directory.Path
    requireSchema connection
    use db = WorkspaceDb.create directory.Path
    Assert.Equal<string list>([ initial; projects; documents ], db.Database.GetAppliedMigrations() |> Seq.toList)
    Assert.Empty(db.Database.GetPendingMigrations())
    Assert.False(db.Database.HasPendingModelChanges())

[<Fact; Trait("Task", "T025")>]
let ``upgrade from the project schema preserves projects and initialization is idempotent`` () =
    use directory = new TemporaryDirectory()
    let workspace, project = Guid.NewGuid(), Guid.NewGuid()
    do
        use db = WorkspaceDb.create directory.Path
        db.GetService<IMigrator>().Migrate(projects)
    do
        use connection = openConnection directory.Path
        addWorkspace connection workspace
        addProject connection project workspace "ONE"
    WorkspaceDb.initialize directory.Path
    WorkspaceDb.initialize directory.Path
    use connection = openConnection directory.Path
    requireSchema connection
    Assert.Equal(project.ToString("D"), scalar connection "SELECT Id FROM ResearchProjects" :?> string)
    Assert.Equal(3L, count connection "__EFMigrationsHistory")
    use db = WorkspaceDb.create directory.Path
    Assert.Empty(db.Database.GetPendingMigrations())
    Assert.False(db.Database.HasPendingModelChanges())

[<Fact; Trait("Task", "T025")>]
let ``documents and associations round trip via EF and SQLite`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let workspaceId, projectId = Guid.NewGuid(), Guid.NewGuid()
    let documentId = Guid.Parse("ABCDEF01-2345-6789-ABCD-EF0123456789")
    let time = DateTimeOffset(2026, 11, 9, 12, 34, 56, TimeSpan.Zero).AddTicks(1234567L)
    // Larger than Int32 to prove 64-bit storage; limits are not a schema rule.
    let byteLength = 5_000_000_000L
    do
        use db = WorkspaceDb.create directory.Path
        db.Add(WorkspaceRow(Id = workspaceId, CreatedUtc = time)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(ResearchProjectRow(Id = projectId, WorkspaceId = workspaceId, Name = "One", NormalizedName = "ONE", CreatedUtc = time)) |> ignore
        db.Add(LibraryDocumentRow(Id = documentId, Sha256 = sha, StorageKey = "k-0001", OriginalFileName = "valid.pdf", ByteLength = byteLength, PageCount = 2, ImportedUtc = time)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(ProjectDocumentRow(ProjectId = projectId, DocumentId = documentId, Role = "ReferenceSource", AddedUtc = time)) |> ignore
        db.SaveChanges() |> ignore
    do
        use db = WorkspaceDb.create directory.Path
        let document = db.Set<LibraryDocumentRow>().Find(documentId)
        let association = db.Set<ProjectDocumentRow>().Find(projectId, documentId)
        Assert.Equal(sha, document.Sha256)
        Assert.Equal("k-0001", document.StorageKey)
        Assert.Equal("valid.pdf", document.OriginalFileName)
        Assert.Equal(byteLength, document.ByteLength)
        Assert.Equal(2, document.PageCount)
        Assert.Equal(time, document.ImportedUtc)
        Assert.Equal("ReferenceSource", association.Role)
        Assert.Equal(time, association.AddedUtc)
    Assert.Equal(documentId.ToString("D"), scalar connection "SELECT Id FROM LibraryDocuments" :?> string)
    Assert.Equal(documentId.ToString("D"), scalar connection "SELECT DocumentId FROM ProjectDocuments" :?> string)
    Assert.Equal(byteLength, scalar connection "SELECT ByteLength FROM LibraryDocuments" :?> int64)
    Assert.Equal("2026-11-09T12:34:56.1234567Z", scalar connection "SELECT ImportedUtc FROM LibraryDocuments" :?> string)

[<Fact; Trait("Task", "T025")>]
let ``byte length and page count must be positive`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    assertConstraint (fun () -> addDocumentWith connection (Guid.NewGuid()) sha "k-1" 0L 2)
    assertConstraint (fun () -> addDocumentWith connection (Guid.NewGuid()) sha "k-2" -1L 2)
    assertConstraint (fun () -> addDocumentWith connection (Guid.NewGuid()) sha "k-3" 753L 0)
    assertConstraint (fun () -> addDocumentWith connection (Guid.NewGuid()) sha "k-4" 753L -1)
    Assert.Equal(0L, count connection "LibraryDocuments")

[<Fact; Trait("Task", "T025")>]
let ``storage key is unique and SHA-256 is not`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    addDocument connection (Guid.NewGuid()) "k-1"
    addDocument connection (Guid.NewGuid()) "k-2"
    assertConstraint (fun () -> addDocumentWith connection (Guid.NewGuid()) (String('a', 64)) "k-1" 753L 2)
    Assert.Equal(2L, scalar connection $"SELECT count(*) FROM LibraryDocuments WHERE Sha256 = '{sha}'" :?> int64)

[<Fact; Trait("Task", "T025")>]
let ``association role is exactly FocusedSource or ReferenceSource`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let project = addProjectInWorkspace connection
    let focused, reference, other = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    addDocument connection focused "k-1"
    addDocument connection reference "k-2"
    addDocument connection other "k-3"
    associate connection project focused "FocusedSource"
    associate connection project reference "ReferenceSource"
    assertConstraint (fun () -> associate connection project other "focusedsource")
    assertConstraint (fun () -> associate connection project other "PrimarySource")
    assertConstraint (fun () -> associate connection project other "")
    Assert.Equal(2L, count connection "ProjectDocuments")

[<Fact; Trait("Task", "T025")>]
let ``a document is associated with a project at most once`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let project = addProjectInWorkspace connection
    let document = Guid.NewGuid()
    addDocument connection document "k-1"
    associate connection project document "FocusedSource"
    assertConstraint (fun () -> associate connection project document "ReferenceSource")
    Assert.Equal("FocusedSource", scalar connection "SELECT Role FROM ProjectDocuments" :?> string)

[<Fact; Trait("Task", "T025")>]
let ``associations require an existing project and document`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let project = addProjectInWorkspace connection
    let document = Guid.NewGuid()
    addDocument connection document "k-1"
    assertConstraint (fun () -> associate connection (Guid.NewGuid()) document "FocusedSource")
    assertConstraint (fun () -> associate connection project (Guid.NewGuid()) "FocusedSource")
    Assert.Equal(0L, count connection "ProjectDocuments")

[<Fact; Trait("Task", "T025")>]
let ``associated documents and projects cannot be deleted and removing an association keeps the document`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let project = addProjectInWorkspace connection
    let document = Guid.NewGuid()
    addDocument connection document "k-1"
    associate connection project document "FocusedSource"
    assertConstraint (fun () -> execute connection $"DELETE FROM LibraryDocuments WHERE Id='{document:D}'")
    assertConstraint (fun () -> execute connection $"DELETE FROM ResearchProjects WHERE Id='{project:D}'")
    execute connection $"DELETE FROM ProjectDocuments WHERE ProjectId='{project:D}' AND DocumentId='{document:D}'"
    Assert.Equal(1L, count connection "LibraryDocuments")
    Assert.Equal(1L, count connection "ResearchProjects")

[<Fact; Trait("Task", "T025")>]
let ``explicit rollback to the project schema drops document tables and preserves projects`` () =
    use directory = new TemporaryDirectory()
    let project =
        use connection = initialized directory.Path
        let project = addProjectInWorkspace connection
        let document = Guid.NewGuid()
        addDocument connection document "k-1"
        associate connection project document "FocusedSource"
        project
    do
        use db = WorkspaceDb.create directory.Path
        db.GetService<IMigrator>().Migrate(projects)
    use connection = openConnection directory.Path
    Assert.False(tableExists connection "LibraryDocuments")
    Assert.False(tableExists connection "ProjectDocuments")
    Assert.Equal(project.ToString("D"), scalar connection "SELECT Id FROM ResearchProjects" :?> string)
    Assert.Equal(2L, count connection "__EFMigrationsHistory")
