module LexScholar.Infrastructure.Tests.WorkspaceDbTests

open System
open System.Globalization
open System.IO
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open Microsoft.EntityFrameworkCore.Migrations
open LexScholar.Infrastructure
open Xunit

type private TemporaryDirectory() =
    let path = Path.Combine(Path.GetTempPath(), "lexscholar-db-tests", Guid.NewGuid().ToString("N"))
    do Directory.CreateDirectory(path) |> ignore
    member _.Path = path
    interface IDisposable with
        member _.Dispose() = Directory.Delete(path, true)

// These are test-only models; production has no domain tables at T010.
type SampleParent() =
    member val Id = Guid.Empty with get, set

type SampleChild() =
    member val Id = Guid.Empty with get, set
    member val ParentId = Nullable<Guid>() with get, set
    member val CreatedUtc = DateTime.MinValue with get, set
    member val ObservedUtc = Nullable<DateTimeOffset>() with get, set

type SampleDb(options: DbContextOptions) =
    inherit WorkspaceDb(options)
    override _.OnModelCreating(model: ModelBuilder) =
        model.Entity<SampleParent>().ToTable("sample_parent") |> ignore
        let child = model.Entity<SampleChild>()
        child.ToTable("sample_child") |> ignore
        child.HasOne<SampleParent>().WithMany().HasForeignKey("ParentId") |> ignore

// A test-only follow-up migration proves an existing baseline can be upgraded.
type UpgradeDb(options: DbContextOptions) =
    inherit WorkspaceDb(options)

[<DbContext(typeof<UpgradeDb>); Migration("20261109000000_TestUpgrade")>]
type TestUpgrade() =
    inherit Migration()
    override _.Up(migration: MigrationBuilder) =
        migration.Sql("CREATE TABLE test_upgrade (value TEXT); INSERT INTO test_upgrade VALUES ('upgrade');") |> ignore
    override _.Down(migration: MigrationBuilder) =
        migration.Sql("DROP TABLE test_upgrade;") |> ignore
    override _.BuildTargetModel(model: ModelBuilder) =
        model.HasAnnotation("ProductVersion", "10.0.14") |> ignore

[<DbContext(typeof<UpgradeDb>)>]
type UpgradeSnapshot() =
    inherit ModelSnapshot()
    override _.BuildModel(model: ModelBuilder) =
        model.HasAnnotation("ProductVersion", "10.0.14") |> ignore

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

let private sampleDb directory =
    let options = DbContextOptionsBuilder().UseSqlite(WorkspaceDb.connectionString directory).Options
    new SampleDb(options)

let private createSampleTables directory =
    use connection = openConnection directory
    execute connection """
        CREATE TABLE sample_parent (Id TEXT NOT NULL PRIMARY KEY);
        CREATE TABLE sample_child (
            Id TEXT NOT NULL PRIMARY KEY,
            ParentId TEXT NULL,
            CreatedUtc TEXT NOT NULL,
            ObservedUtc TEXT NULL,
            FOREIGN KEY (ParentId) REFERENCES sample_parent (Id)
        );
    """

let private applied directory =
    use db = WorkspaceDb.create directory
    db.Database.GetAppliedMigrations() |> Seq.toList

let private databaseHandles databasePath =
    Directory.EnumerateFiles("/proc/self/fd")
    |> Seq.choose (fun fd ->
        try
            let target = FileInfo(fd).ResolveLinkTarget(false)
            if isNull target then None else Some target.FullName
        with
        | :? IOException
        | :? UnauthorizedAccessException
        | :? ArgumentException -> None)
    |> Seq.filter (fun target -> target.StartsWith(databasePath, StringComparison.Ordinal))
    |> Seq.toList

[<Fact; Trait("Task", "T010")>]
let ``fresh database applies a migration and records its history`` () =
    use directory = new TemporaryDirectory()
    Assert.False(File.Exists(WorkspaceDb.databasePath directory.Path))
    WorkspaceDb.initialize directory.Path
    Assert.True(File.Exists(WorkspaceDb.databasePath directory.Path))
    Assert.Equal<string list>([ "20261108000000_Initial" ], applied directory.Path)
    do
        use db = WorkspaceDb.create directory.Path
        Assert.Empty(db.Database.GetPendingMigrations())
        Assert.False(db.Database.HasPendingModelChanges())
    use connection = openConnection directory.Path
    Assert.Equal(1L, unbox<int64> (scalar connection "SELECT count(*) FROM __EFMigrationsHistory"))
    Assert.Equal(0L, unbox<int64> (scalar connection "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%'"))

[<Fact; Trait("Task", "T010")>]
let ``reopening and repeated initialization preserve existing data and history`` () =
    use directory = new TemporaryDirectory()
    WorkspaceDb.initialize directory.Path
    Assert.Equal<string list>([ "20261108000000_Initial" ], applied directory.Path)
    do
        use connection = openConnection directory.Path
        execute connection "CREATE TABLE test_marker (value TEXT NOT NULL); INSERT INTO test_marker VALUES ('preserve');"
    WorkspaceDb.initialize directory.Path
    WorkspaceDb.initialize directory.Path
    Assert.Equal<string list>([ "20261108000000_Initial" ], applied directory.Path)
    use reopened = openConnection directory.Path
    Assert.Equal("preserve", unbox<string> (scalar reopened "SELECT value FROM test_marker"))
    Assert.Equal(1L, unbox<int64> (scalar reopened "SELECT count(*) FROM __EFMigrationsHistory"))

[<Fact; Trait("Task", "T010")>]
let ``an existing SQLite file without migration history is preserved`` () =
    use directory = new TemporaryDirectory()
    do
        use connection = openConnection directory.Path
        execute connection "CREATE TABLE test_marker (value TEXT); INSERT INTO test_marker VALUES ('existing');"
    WorkspaceDb.initialize directory.Path
    Assert.Equal<string list>([ "20261108000000_Initial" ], applied directory.Path)
    use connection = openConnection directory.Path
    Assert.Equal("existing", unbox<string> (scalar connection "SELECT value FROM test_marker"))

[<Fact; Trait("Task", "T010")>]
let ``a later test migration upgrades the baseline without losing data`` () =
    use directory = new TemporaryDirectory()
    WorkspaceDb.initialize directory.Path
    do
        use connection = openConnection directory.Path
        execute connection "CREATE TABLE test_marker (value TEXT); INSERT INTO test_marker VALUES ('preserve');"
    let assembly = typeof<UpgradeDb>.Assembly.GetName().Name
    let options =
        DbContextOptionsBuilder()
            .UseSqlite(WorkspaceDb.connectionString directory.Path, fun sqlite -> sqlite.MigrationsAssembly(assembly) |> ignore)
            .Options
    do
        use db = new UpgradeDb(options)
        db.Database.Migrate()
        db.Database.Migrate()
        Assert.Equal<string list>([ "20261108000000_Initial"; "20261109000000_TestUpgrade" ], db.Database.GetAppliedMigrations() |> Seq.toList)
    WorkspaceDb.initialize directory.Path
    use connection = openConnection directory.Path
    Assert.Equal("preserve", unbox<string> (scalar connection "SELECT value FROM test_marker"))
    Assert.Equal("upgrade", unbox<string> (scalar connection "SELECT value FROM test_upgrade"))

[<Fact; Trait("Task", "T010")>]
let ``independent data directories use independent databases`` () =
    use first = new TemporaryDirectory()
    use second = new TemporaryDirectory()
    WorkspaceDb.initialize first.Path
    WorkspaceDb.initialize second.Path
    Assert.Equal<string list>([ "20261108000000_Initial" ], applied first.Path)
    Assert.Equal<string list>([ "20261108000000_Initial" ], applied second.Path)
    do
        use connection = openConnection first.Path
        execute connection "CREATE TABLE test_marker (value TEXT); INSERT INTO test_marker VALUES ('first');"
    use other = openConnection second.Path
    Assert.Equal(0L, unbox<int64> (scalar other "SELECT count(*) FROM sqlite_master WHERE name='test_marker'"))

[<Fact; Trait("Task", "T010")>]
let ``SQLite and EF connections enforce foreign keys on every open`` () =
    use directory = new TemporaryDirectory()
    let settings = SqliteConnectionStringBuilder(WorkspaceDb.connectionString directory.Path)
    Assert.True(settings.ForeignKeys.HasValue && settings.ForeignKeys.Value)
    createSampleTables directory.Path
    use db = sampleDb directory.Path
    for _ in 1..2 do
        db.Database.OpenConnection()
        use command = db.Database.GetDbConnection().CreateCommand()
        command.CommandText <- "PRAGMA foreign_keys"
        Assert.Equal(1L, unbox<int64> (command.ExecuteScalar()))
        db.Database.CloseConnection()
    db.Add(SampleChild(Id = Guid.NewGuid(), ParentId = Nullable(Guid.NewGuid()), CreatedUtc = DateTime.UtcNow)) |> ignore
    let error = Assert.Throws<DbUpdateException>(fun () -> db.SaveChanges() |> ignore)
    let sqlite = Assert.IsType<SqliteException>(error.InnerException)
    Assert.Equal(19, sqlite.SqliteErrorCode)

[<Fact; Trait("Task", "T010")>]
let ``UUIDs and optional references persist as canonical SQLite text`` () =
    use directory = new TemporaryDirectory()
    createSampleTables directory.Path
    let parentId = Guid.Parse("ABCDEF01-2345-6789-ABCD-EF0123456789")
    let childId = Guid.Parse("FEDCBA98-7654-3210-FEDC-BA9876543210")
    let noParentId = Guid.Parse("11111111-2222-3333-4444-555555555555")
    do
        use db = sampleDb directory.Path
        db.Add(SampleParent(Id = parentId)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(SampleChild(Id = childId, ParentId = Nullable(parentId), CreatedUtc = DateTime.UtcNow)) |> ignore
        db.Add(SampleChild(Id = noParentId, CreatedUtc = DateTime.UtcNow)) |> ignore
        db.SaveChanges() |> ignore
    use connection = openConnection directory.Path
    use command = connection.CreateCommand()
    command.CommandText <- "SELECT typeof(Id), Id, typeof(ParentId), ParentId FROM sample_child ORDER BY Id DESC"
    use reader = command.ExecuteReader()
    Assert.True(reader.Read())
    Assert.Equal("text", reader.GetString(0))
    Assert.Equal(childId.ToString("D"), reader.GetString(1))
    Assert.Equal("text", reader.GetString(2))
    Assert.Equal(parentId.ToString("D"), reader.GetString(3))
    Assert.True(reader.Read())
    Assert.Equal(noParentId.ToString("D"), reader.GetString(1))
    Assert.Equal("null", reader.GetString(2))
    Assert.True(reader.IsDBNull(3))
    use db = sampleDb directory.Path
    let linked = db.Set<SampleChild>().Find(childId)
    let unlinked = db.Set<SampleChild>().Find(noParentId)
    Assert.Equal(childId, linked.Id)
    Assert.Equal(parentId, linked.ParentId.Value)
    Assert.False(unlinked.ParentId.HasValue)

[<Fact; Trait("Task", "T010")>]
let ``timestamps use one UTC text format and round trip with UTC kind`` () =
    use directory = new TemporaryDirectory()
    createSampleTables directory.Path
    let id = Guid.NewGuid()
    let timestamp = DateTime(2026, 5, 17, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567L)
    do
        use db = sampleDb directory.Path
        db.Add(SampleChild(Id = id, CreatedUtc = timestamp)) |> ignore
        db.SaveChanges() |> ignore
    use connection = openConnection directory.Path
    Assert.Equal("text", unbox<string> (scalar connection "SELECT typeof(CreatedUtc) FROM sample_child"))
    Assert.Equal("2026-05-17T12:34:56.1234567Z", unbox<string> (scalar connection "SELECT CreatedUtc FROM sample_child"))
    use db = sampleDb directory.Path
    let actual = db.Set<SampleChild>().Find(id).CreatedUtc
    Assert.Equal(timestamp.Ticks, actual.Ticks)
    Assert.Equal(DateTimeKind.Utc, actual.Kind)

[<Fact; Trait("Task", "T010")>]
let ``local timestamps are normalized to UTC`` () =
    use directory = new TemporaryDirectory()
    createSampleTables directory.Path
    let id = Guid.NewGuid()
    let local = DateTime(2026, 5, 17, 12, 34, 56, DateTimeKind.Local).AddTicks(1234567L)
    let expected = local.ToUniversalTime()
    do
        use db = sampleDb directory.Path
        db.Add(SampleChild(Id = id, CreatedUtc = local)) |> ignore
        db.SaveChanges() |> ignore
    use connection = openConnection directory.Path
    Assert.Equal(expected.ToString("O", CultureInfo.InvariantCulture), unbox<string> (scalar connection "SELECT CreatedUtc FROM sample_child"))
    use db = sampleDb directory.Path
    let actual = db.Set<SampleChild>().Find(id).CreatedUtc
    Assert.Equal(expected.Ticks, actual.Ticks)
    Assert.Equal(DateTimeKind.Utc, actual.Kind)

[<Fact; Trait("Task", "T010")>]
let ``offset timestamps normalize to the same UTC text convention`` () =
    use directory = new TemporaryDirectory()
    createSampleTables directory.Path
    let id = Guid.NewGuid()
    let original = DateTimeOffset(2026, 5, 17, 12, 34, 56, TimeSpan.FromMinutes(330.0)).AddTicks(1234567L)
    do
        use db = sampleDb directory.Path
        db.Add(SampleChild(Id = id, CreatedUtc = DateTime.UtcNow, ObservedUtc = Nullable(original))) |> ignore
        db.SaveChanges() |> ignore
    use connection = openConnection directory.Path
    Assert.Equal(original.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), unbox<string> (scalar connection "SELECT ObservedUtc FROM sample_child"))
    use db = sampleDb directory.Path
    let actual = db.Set<SampleChild>().Find(id).ObservedUtc.Value
    Assert.Equal(original.UtcTicks, actual.UtcTicks)
    Assert.Equal(TimeSpan.Zero, actual.Offset)

[<Fact; Trait("Task", "T010")>]
let ``ambiguous unspecified timestamps are not silently persisted`` () =
    use directory = new TemporaryDirectory()
    createSampleTables directory.Path
    use db = sampleDb directory.Path
    db.Add(SampleChild(Id = Guid.NewGuid(), CreatedUtc = DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified))) |> ignore
    let error = Record.Exception(fun () -> db.SaveChanges() |> ignore)
    Assert.NotNull(error)
    use connection = openConnection directory.Path
    Assert.Equal(0L, unbox<int64> (scalar connection "SELECT count(*) FROM sample_child"))

[<Fact; Trait("Task", "T010")>]
let ``initialization failure closes database handles and permits retry`` () =
    use directory = new TemporaryDirectory()
    let path = WorkspaceDb.databasePath directory.Path
    File.WriteAllText(path, "not a SQLite database")
    let error = Record.Exception(fun () -> WorkspaceDb.initialize directory.Path)
    Assert.NotNull(error)
    Assert.IsType<SqliteException>(error) |> ignore
    Assert.Empty(databaseHandles path)
    File.Delete(path)
    WorkspaceDb.initialize directory.Path
    Assert.Equal<string list>([ "20261108000000_Initial" ], applied directory.Path)
    Assert.Empty(databaseHandles path)

[<Fact; Trait("Task", "T010")>]
let ``native SQLite version meets the approved minimum`` () =
    use directory = new TemporaryDirectory()
    use connection = openConnection directory.Path
    let version = Version.Parse(unbox<string> (scalar connection "SELECT sqlite_version()"))
    Assert.True(version >= Version(3, 46, 1), $"SQLite {version} is older than the approved minimum.")
