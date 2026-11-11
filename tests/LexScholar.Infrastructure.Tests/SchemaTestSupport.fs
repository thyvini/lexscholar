/// Raw SQLite helpers shared by the schema tests.
module LexScholar.Infrastructure.Tests.SchemaTestSupport

open System
open System.IO
open Microsoft.Data.Sqlite
open LexScholar.Infrastructure
open Xunit

type TemporaryDirectory() =
    let path = Path.Combine(Path.GetTempPath(), "lexscholar-schema-tests", Guid.NewGuid().ToString("N"))
    do Directory.CreateDirectory(path) |> ignore
    member _.Path = path
    interface IDisposable with member _.Dispose() = Directory.Delete(path, true)

let openConnection directory =
    let connection = new SqliteConnection(WorkspaceDb.connectionString directory)
    connection.Open()
    connection

let execute (connection: SqliteConnection) sql =
    use command = connection.CreateCommand()
    command.CommandText <- sql
    command.ExecuteNonQuery() |> ignore

let scalar (connection: SqliteConnection) sql =
    use command = connection.CreateCommand()
    command.CommandText <- sql
    command.ExecuteScalar()

let tableExists (connection: SqliteConnection) table =
    scalar connection $"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='{table}'" :?> int64 = 1L

let assertConstraint action =
    let error = Assert.Throws<SqliteException>(Action(action))
    Assert.Equal(19, error.SqliteErrorCode)

let addWorkspace (connection: SqliteConnection) (id: Guid) =
    execute connection $"INSERT INTO Workspaces (Id, ActiveProjectId, CreatedUtc) VALUES ('{id:D}', NULL, '2026-05-17T12:34:56.0000000Z')"

let addProject (connection: SqliteConnection) (id: Guid) (workspace: Guid) name =
    execute connection $"INSERT INTO ResearchProjects (Id, WorkspaceId, Name, NormalizedName, CreatedUtc) VALUES ('{id:D}', '{workspace:D}', '{name}', '{name}', '2026-05-17T12:34:56.0000000Z')"
