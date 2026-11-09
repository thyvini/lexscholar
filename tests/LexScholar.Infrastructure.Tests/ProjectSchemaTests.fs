module LexScholar.Infrastructure.Tests.ProjectSchemaTests

open System
open System.IO
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open Microsoft.EntityFrameworkCore.Migrations
open LexScholar.Infrastructure
open Xunit

type private TemporaryDirectory() =
    let path = Path.Combine(Path.GetTempPath(), "lexscholar-schema-tests", Guid.NewGuid().ToString("N"))
    do Directory.CreateDirectory(path) |> ignore
    member _.Path = path
    interface IDisposable with member _.Dispose() = Directory.Delete(path, true)

let private openConnection directory =
    let connection = new SqliteConnection(WorkspaceDb.connectionString directory)
    connection.Open()
    connection

let private execute (connection: SqliteConnection) sql =
    use command = connection.CreateCommand()
    command.CommandText <- sql
    command.ExecuteNonQuery() |> ignore

let private scalar (connection: SqliteConnection) sql =
    use command = connection.CreateCommand()
    command.CommandText <- sql
    command.ExecuteScalar()

let private requireSchema (connection: SqliteConnection) =
    for table in [ "Workspaces"; "ResearchProjects" ] do
        let count = scalar connection $"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='{table}'" :?> int64
        Assert.True((count = 1L), $"T015 requires the {table} table in the migrated SQLite schema.")

let private initialized directory =
    WorkspaceDb.initialize directory
    let connection = openConnection directory
    try requireSchema connection
    with _ -> connection.Dispose(); reraise()
    connection

let private addWorkspace (connection: SqliteConnection) (id: Guid) =
    execute connection $"INSERT INTO Workspaces (Id, ActiveProjectId, CreatedUtc) VALUES ('{id:D}', NULL, '2026-05-17T12:34:56.0000000Z')"

let private addProject (connection: SqliteConnection) (id: Guid) (workspace: Guid) name =
    execute connection $"INSERT INTO ResearchProjects (Id, WorkspaceId, Name, NormalizedName, CreatedUtc) VALUES ('{id:D}', '{workspace:D}', '{name}', '{name}', '2026-05-17T12:34:56.0000000Z')"

let private assertConstraint action =
    let error = Assert.Throws<SqliteException>(Action(action))
    Assert.Equal(19, error.SqliteErrorCode)

[<Fact; Trait("Task", "T015")>]
let ``fresh database applies project migration and snapshot matches`` () =
    use directory = new TemporaryDirectory()
    WorkspaceDb.initialize directory.Path
    use connection = openConnection directory.Path
    requireSchema connection
    use db = WorkspaceDb.create directory.Path
    Assert.Equal<string list>([ "20261108000000_Initial"; "20261110000000_Projects" ], db.Database.GetAppliedMigrations() |> Seq.toList)
    Assert.Empty(db.Database.GetPendingMigrations())
    Assert.False(db.Database.HasPendingModelChanges())

[<Fact; Trait("Task", "T015")>]
let ``upgrade from T010 preserves data and initialization is idempotent`` () =
    use directory = new TemporaryDirectory()
    do
        use db = WorkspaceDb.create directory.Path
        db.GetService<IMigrator>().Migrate("20261108000000_Initial")
    do
        use connection = openConnection directory.Path
        execute connection "CREATE TABLE test_marker (value TEXT); INSERT INTO test_marker VALUES ('preserve');"
    WorkspaceDb.initialize directory.Path
    WorkspaceDb.initialize directory.Path
    use connection = openConnection directory.Path
    Assert.Equal("preserve", scalar connection "SELECT value FROM test_marker" :?> string)
    requireSchema connection
    Assert.Equal(2L, scalar connection "SELECT count(*) FROM __EFMigrationsHistory" :?> int64)
    use db = WorkspaceDb.create directory.Path
    Assert.Empty(db.Database.GetPendingMigrations())
    Assert.False(db.Database.HasPendingModelChanges())

[<Fact; Trait("Task", "T015")>]
let ``workspace and project UUIDs and timestamps round trip via EF and SQLite`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let workspaceId = Guid.Parse("ABCDEF01-2345-6789-ABCD-EF0123456789")
    let projectId = Guid.Parse("FEDCBA98-7654-3210-FEDC-BA9876543210")
    let time = DateTimeOffset(2026, 5, 17, 12, 34, 56, TimeSpan.Zero).AddTicks(1234567L)
    do
        use db = WorkspaceDb.create directory.Path
        db.Add(WorkspaceRow(Id = workspaceId, CreatedUtc = time)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(ResearchProjectRow(Id = projectId, WorkspaceId = workspaceId, Name = "Family Arbitration", NormalizedName = "FAMILY ARBITRATION", CreatedUtc = time)) |> ignore
        db.SaveChanges() |> ignore
        let workspace = db.Set<WorkspaceRow>().Find(workspaceId)
        workspace.ActiveProjectId <- Nullable(projectId)
        db.SaveChanges() |> ignore
    do
        use db = WorkspaceDb.create directory.Path
        let workspace = db.Set<WorkspaceRow>().Find(workspaceId)
        let project = db.Set<ResearchProjectRow>().Find(projectId)
        Assert.Equal(workspaceId, workspace.Id)
        Assert.Equal(projectId, workspace.ActiveProjectId.Value)
        Assert.Equal(workspaceId, project.WorkspaceId)
        Assert.Equal(time, project.CreatedUtc)
        Assert.Equal(TimeSpan.Zero, workspace.CreatedUtc.Offset)
    Assert.Equal(workspaceId.ToString("D"), scalar connection "SELECT Id FROM Workspaces" :?> string)
    Assert.Equal(projectId.ToString("D"), scalar connection "SELECT ActiveProjectId FROM Workspaces" :?> string)
    Assert.Equal("2026-05-17T12:34:56.1234567Z", scalar connection "SELECT CreatedUtc FROM ResearchProjects" :?> string)
    Assert.Equal("text", scalar connection "SELECT typeof(Id) FROM ResearchProjects" :?> string)

[<Fact; Trait("Task", "T015")>]
let ``project normalized names are unique only within their workspace`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let first, second = Guid.NewGuid(), Guid.NewGuid()
    addWorkspace connection first
    addWorkspace connection second
    addProject connection (Guid.NewGuid()) first "FAMILY ARBITRATION"
    addProject connection (Guid.NewGuid()) second "FAMILY ARBITRATION"
    assertConstraint (fun () -> addProject connection (Guid.NewGuid()) first "FAMILY ARBITRATION")
    Assert.Equal(2L, scalar connection "SELECT count(*) FROM ResearchProjects" :?> int64)

[<Fact; Trait("Task", "T015")>]
let ``project workspace foreign key rejects unknown workspaces`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    assertConstraint (fun () -> addProject connection (Guid.NewGuid()) (Guid.NewGuid()) "ORPHAN")
    Assert.Equal(0L, scalar connection "SELECT count(*) FROM ResearchProjects" :?> int64)

[<Fact; Trait("Task", "T015")>]
let ``active project must belong to its workspace and can be null`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let first, second, project = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    addWorkspace connection first
    addWorkspace connection second
    addProject connection project first "ONE"
    assertConstraint (fun () -> execute connection $"UPDATE Workspaces SET ActiveProjectId='{project:D}' WHERE Id='{second:D}'")
    assertConstraint (fun () -> execute connection $"UPDATE Workspaces SET ActiveProjectId='{Guid.NewGuid():D}' WHERE Id='{second:D}'")
    execute connection $"UPDATE Workspaces SET ActiveProjectId='{project:D}' WHERE Id='{first:D}'"
    Assert.Equal(1L, scalar connection "SELECT count(*) FROM Workspaces WHERE ActiveProjectId IS NULL" :?> int64)

[<Fact; Trait("Task", "T015")>]
let ``referenced workspaces and active projects cannot be deleted`` () =
    use directory = new TemporaryDirectory()
    use connection = initialized directory.Path
    let workspace, project = Guid.NewGuid(), Guid.NewGuid()
    addWorkspace connection workspace
    addProject connection project workspace "ONE"
    execute connection $"UPDATE Workspaces SET ActiveProjectId='{project:D}' WHERE Id='{workspace:D}'"
    assertConstraint (fun () -> execute connection $"DELETE FROM ResearchProjects WHERE Id='{project:D}'")
    assertConstraint (fun () -> execute connection $"DELETE FROM Workspaces WHERE Id='{workspace:D}'")

[<Fact; Trait("Task", "T015")>]
let ``explicit migration rollback handles cyclic references and preserves T010 data`` () =
    use directory = new TemporaryDirectory()
    let workspace, project = Guid.NewGuid(), Guid.NewGuid()
    do
        use connection = initialized directory.Path
        execute connection "CREATE TABLE test_marker (value TEXT); INSERT INTO test_marker VALUES ('preserve');"
        addWorkspace connection workspace
        addProject connection project workspace "ONE"
        execute connection $"UPDATE Workspaces SET ActiveProjectId='{project:D}' WHERE Id='{workspace:D}'"
    do
        use db = WorkspaceDb.create directory.Path
        db.GetService<IMigrator>().Migrate("20261108000000_Initial")
    use connection = openConnection directory.Path
    Assert.Equal("preserve", scalar connection "SELECT value FROM test_marker" :?> string)
    Assert.Equal(0L, scalar connection "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Workspaces'" :?> int64)
    Assert.Equal(1L, scalar connection "SELECT count(*) FROM __EFMigrationsHistory" :?> int64)
