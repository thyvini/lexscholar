module LexScholar.Infrastructure.Tests.LibraryDeletionTransactionTests

open System
open System.IO
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open LexScholar.Application
open LexScholar.Application.LibraryDeletion
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private time = "2026-11-23T12:00:00.0000000Z"

let private seed directory =
    WorkspaceDb.initialize directory
    use c = openConnection directory
    let workspace, p1, p2 = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    let shared, alone, untouched, operation = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    addWorkspace c workspace
    addProject c p1 workspace "ONE"
    addProject c p2 workspace "TWO"
    for document in [ shared; alone; untouched ] do
        execute c $"INSERT INTO LibraryDocuments VALUES ('{document:D}', 'sha', '{document:N}', 'fixture.pdf', 10, 2, '{time}')"
        File.WriteAllText(Path.Combine(directory, document.ToString("N")), "original")
    for document in [ shared; untouched ] do
        for project in [ p1; p2 ] do
            execute c $"INSERT INTO ProjectDocuments VALUES ('{project:D}', '{document:D}', 'FocusedSource', '{time}')"
            execute c $"INSERT INTO DocumentSummaries VALUES ('{project:D}', '{document:D}', '{{\"version\":1}}', '{time}')"
    let coordination = LibraryCoordination()
    let preview = LibraryDeletion.preview (LibraryDeletionRepository.capture directory) [ shared; alone ] |> Result.toOption |> Option.get
    Assert.Equal(Ok operation, LibraryDeletionRepository.admit coordination directory operation [ shared; alone ] preview.Revision)
    coordination, operation, shared, alone, untouched

let private assertMetadata directory document expected =
    use c = openConnection directory
    for table, column, count in [ "LibraryDocuments", "Id", 1L; "ProjectDocuments", "DocumentId", 2L; "DocumentSummaries", "DocumentId", 2L ] do
        Assert.Equal((if expected then count else 0L), scalar c $"SELECT count(*) FROM {table} WHERE {column} = '{document:D}'" :?> int64)

let private assertPhase directory (operation: Guid) (document: Guid) status phase =
    use db = WorkspaceDb.create directory
    let item = db.Set<DeletionItemRow>().Find(operation, document)
    Assert.Equal(status, item.Status)
    Assert.Equal(phase, item.Phase)
    Assert.Equal(document.ToString("N"), item.StorageKey)
    if status = "incomplete" then Assert.Equal("storage_error", item.Reason)
    Assert.True(File.Exists(Path.Combine(directory, item.StorageKey)), "Metadata removal must not remove the original")

[<Fact; Trait("Task", "T012")>]
let ``shared and unassociated metadata removal is durable and idempotent without false deleted`` () =
    use dir = new TemporaryDirectory()
    let coordination, operation, shared, alone, untouched = seed dir.Path
    for document in [ shared; alone ] do
        Assert.Equal(Ok (document.ToString("N")), LibraryDeletionRepository.removeMetadata coordination dir.Path operation document)
        Assert.Equal(Ok (document.ToString("N")), LibraryDeletionRepository.removeMetadata coordination dir.Path operation document)
        assertMetadata dir.Path document false
        assertPhase dir.Path operation document "metadata_removed" "metadata_removed"
    WorkspaceDb.initialize dir.Path
    assertMetadata dir.Path untouched true
    use c = openConnection dir.Path
    Assert.Equal(2L, scalar c "SELECT count(*) FROM ResearchProjects" :?> int64)

[<Theory; InlineData(false); InlineData(true); Trait("Task", "T012")>]
let ``rollback preserves metadata and another item can progress`` failBeforeCommit =
    use dir = new TemporaryDirectory()
    let coordination, operation, shared, alone, untouched = seed dir.Path
    let checkpoint step =
        let target = if failBeforeCommit then LibraryDeletionRepository.MetadataStep.BeforeCommit else LibraryDeletionRepository.MetadataStep.AssociationsRemoved
        if step = target then raise (IOException("secret/path/research"))
    Assert.Equal(Error MetadataError.StorageError, LibraryDeletionRepository.removeMetadataWith checkpoint coordination dir.Path operation shared)
    assertMetadata dir.Path shared true
    assertMetadata dir.Path untouched true
    assertPhase dir.Path operation shared "incomplete" "pending"
    Assert.Equal(Ok (alone.ToString("N")), LibraryDeletionRepository.removeMetadata coordination dir.Path operation alone)
    Assert.Equal(Ok (shared.ToString("N")), LibraryDeletionRepository.removeMetadata coordination dir.Path operation shared)
    assertMetadata dir.Path shared false

[<Fact; Trait("Task", "T012")>]
let ``uncertain committed transaction is resolved from a fresh durable read`` () =
    use dir = new TemporaryDirectory()
    let coordination, operation, shared, _, untouched = seed dir.Path
    let checkpoint step =
        if step = LibraryDeletionRepository.MetadataStep.AfterCommit then raise (IOException("lost commit acknowledgement"))
    Assert.Equal(Ok (shared.ToString("N")), LibraryDeletionRepository.removeMetadataWith checkpoint coordination dir.Path operation shared)
    assertMetadata dir.Path shared false
    assertMetadata dir.Path untouched true
    assertPhase dir.Path operation shared "metadata_removed" "metadata_removed"

[<Fact; Trait("Task", "T012")>]
let ``missing intent never authorizes metadata removal`` () =
    use dir = new TemporaryDirectory()
    let coordination, _, shared, _, _ = seed dir.Path
    Assert.Equal(Error MetadataError.ItemNotFound, LibraryDeletionRepository.removeMetadata coordination dir.Path (Guid.NewGuid()) shared)
    assertMetadata dir.Path shared true

[<Fact; Trait("Task", "T012")>]
let ``processing fences preparations under the shared boundary and commits all metadata together`` () =
    use dir = new TemporaryDirectory()
    let coordination, operation, shared, _, untouched = seed dir.Path
    let jobs =
        SummaryJobs(
            { Find = fun _ -> None
              Save = fun _ _ -> ()
              Delete = fun _ -> ()
              AssociationExists = fun _ -> true },
            coordination = coordination)
    let selected : SummaryKey = { Project = Guid.NewGuid(); Document = shared }
    let other : SummaryKey = { Project = Guid.NewGuid(); Document = untouched }
    let selectedToken, selectedLease = jobs.Prepare selected
    let otherToken, otherLease = jobs.Prepare other
    use _selected = selectedLease
    use _other = otherLease
    let checkpoint step =
        Assert.True(coordination.IsMutationHeld)
        Assert.True(selectedToken.IsCancellationRequested)
        Assert.False(otherToken.IsCancellationRequested)
        if step = LibraryDeletionRepository.MetadataStep.BeforeCommit then
            // WAL readers still see the whole pre-change state, including the
            // pending journal, not association deletion without a phase marker.
            assertMetadata dir.Path shared true
            assertPhase dir.Path operation shared "pending" "pending"
    let remove = LibraryDeletionRepository.removeMetadataWith checkpoint coordination dir.Path
    let hasIntent = LibraryDeletionRepository.hasItem dir.Path
    Assert.Equal(Error MetadataError.ItemNotFound, LibraryDeletion.removeMetadata jobs hasIntent remove (Guid.NewGuid()) shared)
    Assert.False(selectedToken.IsCancellationRequested)
    Assert.Equal(Ok (shared.ToString("N")), LibraryDeletion.removeMetadata jobs hasIntent remove operation shared)
    assertMetadata dir.Path shared false
    assertPhase dir.Path operation shared "metadata_removed" "metadata_removed"

[<Theory; InlineData(false); InlineData(true); Trait("Task", "T012")>]
let ``intent lookup storage failure is sanitized and does not interrupt other items`` accessFailure =
    use dir = new TemporaryDirectory()
    let coordination, operation, shared, alone, untouched = seed dir.Path
    let jobs =
        SummaryJobs(
            { Find = fun _ -> None
              Save = fun _ _ -> ()
              Delete = fun _ -> ()
              AssociationExists = fun _ -> true },
            coordination = coordination)
    let key : SummaryKey = { Project = Guid.NewGuid(); Document = shared }
    let token, lease = jobs.Prepare key
    use _lease = lease
    let hasIntent op document =
        Assert.True(coordination.IsMutationHeld)
        if document = shared then
            if accessFailure then raise (UnauthorizedAccessException("private/path/research"))
            else raise (SqliteException("private/path/research", 10))
        LibraryDeletionRepository.hasItem dir.Path op document
    let mutable removed = []
    let remove op document =
        removed <- document :: removed
        LibraryDeletionRepository.removeMetadata coordination dir.Path op document
    Assert.Equal(Error MetadataError.StorageError, LibraryDeletion.removeMetadata jobs hasIntent remove operation shared)
    Assert.Empty(removed)
    Assert.False(token.IsCancellationRequested)
    assertMetadata dir.Path shared true
    assertPhase dir.Path operation shared "pending" "pending"
    Assert.Equal(Ok (alone.ToString("N")), LibraryDeletion.removeMetadata jobs hasIntent remove operation alone)
    assertPhase dir.Path operation alone "metadata_removed" "metadata_removed"
    assertMetadata dir.Path untouched true
    use db = WorkspaceDb.create dir.Path
    Assert.NotNull(db.Set<DeletionOperationRow>().Find(operation))
    Assert.Equal(2, db.Set<DeletionItemRow>() |> Seq.length)
