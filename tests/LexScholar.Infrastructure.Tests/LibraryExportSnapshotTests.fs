module LexScholar.Infrastructure.Tests.LibraryExportSnapshotTests

open System
open System.Diagnostics
open System.IO
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private time = "2026-11-12T12:00:00.0000000Z"

[<Fact; Trait("Task", "T002")>]
let ``export reads a consistent state while another connection commits`` () =
    use dir = new TemporaryDirectory()
    WorkspaceDb.initialize dir.Path
    let workspace, project, document, unassociated, added = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    use writer = openConnection dir.Path
    Assert.Equal("wal", (scalar writer "PRAGMA journal_mode" :?> string).ToLowerInvariant())
    addWorkspace writer workspace
    addProject writer project workspace "PROJECT"
    execute writer $"INSERT INTO LibraryDocuments VALUES ('{document:D}', 'sha', 'key', 'one.pdf', 10, 2, '{time}')"
    execute writer $"INSERT INTO LibraryDocuments VALUES ('{unassociated:D}', 'sha2', 'key2', 'two.pdf', 11, 2, '{time}')"
    execute writer $"INSERT INTO ProjectDocuments VALUES ('{project:D}', '{document:D}', 'FocusedSource', '{time}')"
    let key = { Project = project; Document = document }
    let summary =
        { DocumentId = document; Points = [ { Text = "Before"; Pages = [ 1 ] } ]
          PagesCovered = [ 1 ]; UnreadablePages = []; OmittedPoints = 0
          GeneratedAt = DateTimeOffset.Parse(time); ServiceHost = "local"; Model = "one" }
    SummaryRepository.save dir.Path key summary
    let mutable commitElapsed = TimeSpan.Zero
    let snapshot =
        LibraryExportSnapshot.withSnapshot dir.Path (fun db ->
            // Commit all changes after the read transaction has been established.
            let clock = Stopwatch.StartNew()
            execute writer "BEGIN IMMEDIATE"
            execute writer $"INSERT INTO LibraryDocuments VALUES ('{added:D}', 'sha3', 'key3', 'three.pdf', 12, 2, '{time}')"
            execute writer $"UPDATE ProjectDocuments SET Role = 'ReferenceSource' WHERE DocumentId = '{document:D}'"
            execute writer $"INSERT INTO ProjectDocuments VALUES ('{project:D}', '{unassociated:D}', 'FocusedSource', '{time}')"
            execute writer $"UPDATE DocumentSummaries SET Content = '{{\"version\":1,\"points\":[{{\"text\":\"After\",\"pages\":[2]}}],\"pagesCovered\":[2],\"unreadablePages\":[],\"omittedPoints\":0,\"serviceHost\":\"local\",\"model\":\"two\"}}' WHERE DocumentId = '{document:D}'"
            execute writer "COMMIT"
            clock.Stop()
            commitElapsed <- clock.Elapsed
            LibraryExportSnapshot.read db)
    printfn "WAL writer committed while the snapshot was open in %O" commitElapsed
    Assert.Equal(2, snapshot.Documents.Length)
    Assert.Contains(snapshot.Documents, fun row -> row.Id = unassociated)
    Assert.Single(snapshot.Associations) |> ignore
    Assert.Equal("FocusedSource", snapshot.Associations.Head.Role)
    Assert.Equal("Before", (snapshot.Summaries.Head |> snd).Points.Head.Text)
    let later = LibraryExportSnapshot.capture dir.Path
    Assert.Equal(3, later.Documents.Length)
    Assert.Equal(2, later.Associations.Length)
    Assert.Equal("After", (later.Summaries.Head |> snd).Points.Head.Text)

[<Fact; Trait("Task", "T002")>]
let ``snapshot rejects an unknown stored summary version`` () =
    use dir = new TemporaryDirectory()
    WorkspaceDb.initialize dir.Path
    use c = openConnection dir.Path
    let workspace, project, document = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    addWorkspace c workspace
    addProject c project workspace "PROJECT"
    execute c $"INSERT INTO LibraryDocuments VALUES ('{document:D}', 'sha', 'key', 'one.pdf', 10, 2, '{time}')"
    execute c $"INSERT INTO ProjectDocuments VALUES ('{project:D}', '{document:D}', 'FocusedSource', '{time}')"
    execute c $"INSERT INTO DocumentSummaries VALUES ('{project:D}', '{document:D}', '{{\"version\":2}}', '{time}')"
    Assert.Throws<InvalidDataException>(fun () -> LibraryExportSnapshot.capture dir.Path |> ignore) |> ignore
