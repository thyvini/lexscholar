module LexScholar.Server.Tests.WorkspaceStartupTests

open System
open System.IO
open System.Net
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open LexScholar.Infrastructure
open LexScholar.Server
open LexScholar.Server.Tests.ProcessHarness
open Xunit

let private migrations directory =
    use db = WorkspaceDb.create directory
    db.Database.GetAppliedMigrations() |> Seq.toList

let private currentMigrations = [ "20261108000000_Initial"; "20261110000000_Projects" ]

[<Fact; Trait("Task", "T010")>]
let ``host construction applies migrations before it starts listening`` () =
    use workspace = TemporaryWorkspace.Create()
    let builder = HostSetup.builder workspace.Root []
    match Program.create builder false with
    | Error error -> Assert.Fail($"Host creation unexpectedly rejected: {error}")
    | Ok app ->
        use app = app
        Assert.False(app.Lifetime.ApplicationStarted.IsCancellationRequested)
        Assert.True(File.Exists(WorkspaceDb.databasePath workspace.Root))
        Assert.Equal<string list>(currentMigrations, migrations workspace.Root)
        use contender = HostProcess.Start(workspace.Root, [])
        Assert.Equal("rejected", contender.Startup.Kind)
        Assert.Equal("DirectoryAlreadyInUse", contender.Startup.ErrorType)

[<Fact; Trait("Task", "T010")>]
let ``startup checks the lock before SQLite and releases it on initialization failure`` () =
    use workspace = TemporaryWorkspace.Create()
    let databasePath = WorkspaceDb.databasePath workspace.Root
    Directory.CreateDirectory(databasePath) |> ignore
    let held =
        match DataDirectoryLock.acquire workspace.Root with
        | Ok handle -> handle
        | Error error -> failwithf "Could not acquire test lock: %A" error
    use held = held
    match Program.create (HostSetup.builder workspace.Root []) false with
    | Error (Program.LockFailure (DirectoryAlreadyInUse _)) -> ()
    | Error error -> Assert.Fail($"Expected lock contention before database access, got {error}.")
    | Ok app ->
        app.DisposeAsync().AsTask().GetAwaiter().GetResult()
        Assert.Fail("Contended data directory unexpectedly started.")
    held.Dispose()
    let error = Record.Exception(fun () ->
        match Program.create (HostSetup.builder workspace.Root []) false with
        | Ok app -> app.DisposeAsync().AsTask().GetAwaiter().GetResult()
        | Error rejected -> failwithf "Unexpected typed startup rejection: %A" rejected)
    Assert.NotNull(error)
    Assert.IsType<SqliteException>(error) |> ignore
    match DataDirectoryLock.acquire workspace.Root with
    | Error lockError -> Assert.Fail($"Initialization failure leaked the directory lock: {lockError}")
    | Ok handle -> handle.Dispose()
    Directory.Delete(databasePath)
    match Program.create (HostSetup.builder workspace.Root []) false with
    | Error rejected -> Assert.Fail($"A repaired workspace could not start: {rejected}")
    | Ok app ->
        use app = app
        Assert.Equal<string list>(currentMigrations, migrations workspace.Root)

[<Fact; Trait("Task", "T010")>]
let ``real host process restarts with its migrated database intact`` () =
    use workspace = TemporaryWorkspace.Create()
    use first = HostProcess.Start(workspace.Root, [])
    Assert.Equal("ready", first.Startup.Kind)
    Assert.True(File.Exists(WorkspaceDb.databasePath workspace.Root))
    Assert.Equal<string list>(currentMigrations, migrations workspace.Root)
    do
        use connection = new SqliteConnection(WorkspaceDb.connectionString workspace.Root)
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "CREATE TABLE test_marker (value TEXT); INSERT INTO test_marker VALUES ('preserve');"
        command.ExecuteNonQuery() |> ignore
    use client = client first
    use response = client.GetAsync("/api/projects").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    first.Stop()
    use second = HostProcess.Start(workspace.Root, [])
    Assert.Equal("ready", second.Startup.Kind)
    Assert.Equal<string list>(currentMigrations, migrations workspace.Root)
    use connection = new SqliteConnection(WorkspaceDb.connectionString workspace.Root)
    connection.Open()
    use command = connection.CreateCommand()
    command.CommandText <- "SELECT value FROM test_marker"
    Assert.Equal("preserve", unbox<string> (command.ExecuteScalar()))
