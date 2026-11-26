module LexScholar.Infrastructure.Tests.LibraryDeletionFileTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Application.LibraryDeletion
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private seed directory =
    WorkspaceDb.initialize directory
    DocumentStore.ensureDirectories directory
    use c = openConnection directory
    let workspace, project, document, operation = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    addWorkspace c workspace
    addProject c project workspace "ONE"
    let time = "2026-11-23T12:00:00.0000000Z"
    execute c $"INSERT INTO LibraryDocuments VALUES ('{document:D}', 'sha', '{document:N}', 'fixture.pdf', 10, 2, '{time}')"
    execute c $"INSERT INTO ProjectDocuments VALUES ('{project:D}', '{document:D}', 'FocusedSource', '{time}')"
    execute c $"INSERT INTO DocumentSummaries VALUES ('{project:D}', '{document:D}', '{{\"version\":1}}', '{time}')"
    let path = Path.Combine(DocumentStore.originalsDirectory directory, document.ToString("N"))
    File.WriteAllText(path, "original")
    File.SetUnixFileMode(path, UnixFileMode.UserRead)
    let coordination = LibraryCoordination()
    let preview = LibraryDeletion.preview (LibraryDeletionRepository.capture directory) [ document ] |> Result.toOption |> Option.get
    Assert.Equal(Ok operation, LibraryDeletionRepository.admit coordination directory operation [ document ] preview.Revision)
    coordination, operation, document, path

let private item directory operation document =
    use db = WorkspaceDb.create directory
    db.Set<DeletionItemRow>().Find(operation, document)

let private metadataGone directory (document: Guid) =
    use c = openConnection directory
    for table, column in [ "LibraryDocuments", "Id"; "ProjectDocuments", "DocumentId"; "DocumentSummaries", "DocumentId" ] do
        Assert.Equal(0L, scalar c $"SELECT count(*) FROM {table} WHERE {column} = '{document:D}'" :?> int64)

[<Fact; Trait("Task", "T013")>]
let ``completion requires metadata absence and durable unlink and is retryable`` () =
    use dir = new TemporaryDirectory()
    let coordination, operation, document, path = seed dir.Path
    let other = Path.Combine(DocumentStore.originalsDirectory dir.Path, Guid.NewGuid().ToString("N"))
    File.WriteAllText(other, "untouched")
    Assert.Equal(Error MetadataError.StorageError, LibraryDeletionRepository.finishOriginal coordination dir.Path operation document)
    Assert.True(File.Exists path)
    Assert.Equal(Ok (document.ToString("N")), LibraryDeletionRepository.removeMetadata coordination dir.Path operation document)
    let observed = ResizeArray<DocumentStore.RemovalStep>()
    let remove key =
        DocumentStore.removeOriginalWith (fun step ->
            observed.Add step
            Assert.NotEqual<string>("deleted", (item dir.Path operation document).Status)
            if step = DocumentStore.RemovalStep.AfterUnlink then Assert.False(File.Exists path)) dir.Path key
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginalWith ignore remove coordination dir.Path operation document)
    Assert.Equal<DocumentStore.RemovalStep list>(
        [ DocumentStore.RemovalStep.BeforeUnlink; DocumentStore.RemovalStep.AfterUnlink; DocumentStore.RemovalStep.BeforeSync; DocumentStore.RemovalStep.AfterSync ], List.ofSeq observed)
    Assert.False(File.Exists path)
    metadataGone dir.Path document
    Assert.Equal("deleted", (item dir.Path operation document).Status)
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginal coordination dir.Path operation document)
    Assert.Equal("untouched", File.ReadAllText other)

[<Theory; InlineData(0); InlineData(1); InlineData(2); InlineData(3); Trait("Task", "T013")>]
let ``unlink or sync failures retain incomplete intent and retry proven missing`` failAt =
    use dir = new TemporaryDirectory()
    let coordination, operation, document, path = seed dir.Path
    LibraryDeletionRepository.removeMetadata coordination dir.Path operation document |> ignore
    let steps = [| DocumentStore.RemovalStep.BeforeUnlink; DocumentStore.RemovalStep.AfterUnlink; DocumentStore.RemovalStep.BeforeSync; DocumentStore.RemovalStep.AfterSync |]
    let remove key = DocumentStore.removeOriginalWith (fun step -> if step = steps[failAt] then raise (IOException("private/path"))) dir.Path key
    Assert.Equal(Error MetadataError.StorageError, LibraryDeletionRepository.finishOriginalWith ignore remove coordination dir.Path operation document)
    Assert.Equal("incomplete", (item dir.Path operation document).Status)
    Assert.Equal("metadata_removed", (item dir.Path operation document).Phase)
    Assert.Equal("storage_error", (item dir.Path operation document).Reason)
    Assert.Equal((failAt = 0), File.Exists path)
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginal coordination dir.Path operation document)
    Assert.Equal("deleted", (item dir.Path operation document).Status)
    metadataGone dir.Path document

[<Theory; InlineData(false); InlineData(true); Trait("Task", "T013")>]
let ``completion commit failure is reread rather than guessed`` afterCommit =
    use dir = new TemporaryDirectory()
    let coordination, operation, document, _ = seed dir.Path
    LibraryDeletionRepository.removeMetadata coordination dir.Path operation document |> ignore
    let checkpoint step =
        if step = (if afterCommit then LibraryDeletionRepository.CompletionStep.AfterCommit else LibraryDeletionRepository.CompletionStep.BeforeCommit) then
            raise (IOException("lost acknowledgement"))
    let result = LibraryDeletionRepository.finishOriginalWith checkpoint (DocumentStore.removeOriginal dir.Path) coordination dir.Path operation document
    Assert.Equal((if afterCommit then Ok () else Error MetadataError.StorageError), result)
    Assert.Equal((if afterCommit then "deleted" else "incomplete"), (item dir.Path operation document).Status)
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginal coordination dir.Path operation document)

[<Fact; Trait("Task", "T013")>]
let ``invalid keys and nonfiles fail closed while symlink targets stay untouched`` () =
    use dir = new TemporaryDirectory()
    DocumentStore.ensureDirectories dir.Path
    let outside = Path.Combine(dir.Path, "outside")
    File.WriteAllText(outside, "safe")
    for key in [ null; ""; "../outside"; String.replicate 32 "A"; String.replicate 31 "a" ] do
        Assert.ThrowsAny<Exception>(fun () -> DocumentStore.removeOriginal dir.Path key) |> ignore
    let key = Guid.NewGuid().ToString("N")
    let path = Path.Combine(DocumentStore.originalsDirectory dir.Path, key)
    Directory.CreateDirectory path |> ignore
    Assert.ThrowsAny<IOException>(fun () -> DocumentStore.removeOriginal dir.Path key) |> ignore
    Directory.Delete path
    File.CreateSymbolicLink(path, outside) |> ignore
    DocumentStore.removeOriginal dir.Path key
    Assert.Equal("safe", File.ReadAllText outside)
    // Missing originals directory is not proof that a managed entry is absent.
    Directory.Delete(DocumentStore.originalsDirectory dir.Path)
    Assert.ThrowsAny<IOException>(fun () -> DocumentStore.removeOriginal dir.Path key) |> ignore

[<Fact; Trait("Task", "T013")>]
let ``completed deletion retry is terminal without filesystem access or a new commit`` () =
    use dir = new TemporaryDirectory()
    let coordination, operation, document, path = seed dir.Path
    LibraryDeletionRepository.removeMetadata coordination dir.Path operation document |> ignore
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginal coordination dir.Path operation document)
    let mutable fileCalls = 0
    let mutable commitCalls = 0
    let remove _ =
        fileCalls <- fileCalls + 1
        raise (UnauthorizedAccessException("unavailable filesystem"))
    let checkpoint _ =
        commitCalls <- commitCalls + 1
        raise (IOException("retry must not commit"))
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginalWith checkpoint remove coordination dir.Path operation document)
    Assert.Equal(0, fileCalls)
    Assert.Equal(0, commitCalls)
    Assert.Equal("deleted", (item dir.Path operation document).Status)
    Assert.Null((item dir.Path operation document).Reason)
    // A reused name is not authority to unlink again, nor is an unavailable
    // originals directory grounds to regress an already durable outcome.
    File.WriteAllText(path, "later file")
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginal coordination dir.Path operation document)
    Assert.Equal("later file", File.ReadAllText path)
    File.Delete path
    Directory.Delete(DocumentStore.originalsDirectory dir.Path)
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginal coordination dir.Path operation document)
    Assert.Equal("deleted", (item dir.Path operation document).Status)

[<Fact; Trait("Task", "T013")>]
let ``permission failure cannot be interpreted as missing and another item remains untouched`` () =
    use dir = new TemporaryDirectory()
    let coordination, operation, document, path = seed dir.Path
    LibraryDeletionRepository.removeMetadata coordination dir.Path operation document |> ignore
    let remove _ = raise (UnauthorizedAccessException("private/path"))
    Assert.Equal(Error MetadataError.StorageError, LibraryDeletionRepository.finishOriginalWith ignore remove coordination dir.Path operation document)
    Assert.True(File.Exists path)
    Assert.Equal("incomplete", (item dir.Path operation document).Status)
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginal coordination dir.Path operation document)

[<Fact; Trait("Task", "T013")>]
let ``symlinked originals directory is never followed`` () =
    use dir = new TemporaryDirectory()
    let elsewhere = Path.Combine(dir.Path, "elsewhere")
    Directory.CreateDirectory elsewhere |> ignore
    let key = Guid.NewGuid().ToString("N")
    File.WriteAllText(Path.Combine(elsewhere, key), "safe")
    Directory.CreateSymbolicLink(DocumentStore.originalsDirectory dir.Path, elsewhere) |> ignore
    Assert.ThrowsAny<IOException>(fun () -> DocumentStore.removeOriginal dir.Path key) |> ignore
    Assert.Equal("safe", File.ReadAllText(Path.Combine(elsewhere, key)))

[<Fact; Trait("Task", "T005")>]
let ``removal stays anchored when the originals pathname is replaced`` () =
    use dir = new TemporaryDirectory()
    let coordination, operation, document, path = seed dir.Path
    let originals = DocumentStore.originalsDirectory dir.Path
    let moved = Path.Combine(dir.Path, "opened-originals")
    let key = document.ToString("N")
    let other = Guid.NewGuid().ToString("N")
    let outside = Path.Combine(dir.Path, "outside")
    File.WriteAllText(Path.Combine(originals, other), "unrelated")
    File.WriteAllText(outside, "outside")
    Assert.Equal(Ok key, LibraryDeletionRepository.removeMetadata coordination dir.Path operation document)
    let remove storage =
        DocumentStore.removeOriginalWith (fun step ->
            if step = DocumentStore.RemovalStep.BeforeUnlink then
                Directory.Move(originals, moved)
                Directory.CreateDirectory originals |> ignore
                File.WriteAllText(path, "sentinel")
                File.CreateSymbolicLink(Path.Combine(originals, other), outside) |> ignore
            Assert.NotEqual<string>("deleted", (item dir.Path operation document).Status)) dir.Path storage
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginalWith ignore remove coordination dir.Path operation document)
    Assert.False(File.Exists(Path.Combine(moved, key)))
    Assert.Equal("sentinel", File.ReadAllText path)
    Assert.Equal("unrelated", File.ReadAllText(Path.Combine(moved, other)))
    Assert.Equal("outside", File.ReadAllText outside)
    Assert.Equal("deleted", (item dir.Path operation document).Status)

[<Fact; Trait("Task", "T005")>]
let ``sync uses the opened directory when its pathname disappears`` () =
    use dir = new TemporaryDirectory()
    let coordination, operation, document, _ = seed dir.Path
    let originals = DocumentStore.originalsDirectory dir.Path
    let moved = Path.Combine(dir.Path, "opened-originals")
    let key = document.ToString("N")
    Assert.Equal(Ok key, LibraryDeletionRepository.removeMetadata coordination dir.Path operation document)
    let mutable synced = false
    let remove storage =
        DocumentStore.removeOriginalWith (fun step ->
            if step = DocumentStore.RemovalStep.BeforeSync then
                Directory.Move(originals, moved)
                Assert.False(Directory.Exists originals)
            if step = DocumentStore.RemovalStep.AfterSync then synced <- true
            Assert.NotEqual<string>("deleted", (item dir.Path operation document).Status)) dir.Path storage
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginalWith ignore remove coordination dir.Path operation document)
    Assert.True synced
    Assert.False(File.Exists(Path.Combine(moved, key)))
    Assert.Equal("deleted", (item dir.Path operation document).Status)

[<Fact; Trait("Task", "T013")>]
let ``missing intent and corrupted retained key never authorize another file`` () =
    use dir = new TemporaryDirectory()
    let coordination, operation, document, path = seed dir.Path
    let mutable called = false
    let remove _ = called <- true
    Assert.Equal(Error MetadataError.ItemNotFound,
                 LibraryDeletionRepository.finishOriginalWith ignore remove coordination dir.Path (Guid.NewGuid()) document)
    Assert.False called
    LibraryDeletionRepository.removeMetadata coordination dir.Path operation document |> ignore
    let outside = Path.Combine(dir.Path, "outside")
    File.WriteAllText(outside, "safe")
    use c = openConnection dir.Path
    execute c $"UPDATE DeletionItems SET StorageKey = '../outside' WHERE OperationId = '{operation:D}'"
    Assert.Equal(Error MetadataError.StorageError, LibraryDeletionRepository.finishOriginal coordination dir.Path operation document)
    Assert.Equal("incomplete", (item dir.Path operation document).Status)
    Assert.True(File.Exists path)
    Assert.Equal("safe", File.ReadAllText outside)

[<Fact; Trait("Task", "T013")>]
let ``unassociated original completes after metadata removal`` () =
    use dir = new TemporaryDirectory()
    let coordination, operation, document, path = seed dir.Path
    do
        use c = openConnection dir.Path
        execute c $"DELETE FROM ProjectDocuments WHERE DocumentId = '{document:D}'"
    Assert.Equal(Ok (document.ToString("N")), LibraryDeletionRepository.removeMetadata coordination dir.Path operation document)
    Assert.Equal(Ok (), LibraryDeletionRepository.finishOriginal coordination dir.Path operation document)
    Assert.False(File.Exists path)
    metadataGone dir.Path document
    Assert.Equal("deleted", (item dir.Path operation document).Status)

[<Fact; Trait("Task", "T013")>]
let ``file lease waits outside mutation and registry before metadata or unlink`` () = task {
    use dir = new TemporaryDirectory()
    let coordination, operation, document, path = seed dir.Path
    let jobs =
        SummaryJobs(
            { Find = fun _ -> None
              Save = fun _ _ -> ()
              Delete = fun _ -> ()
              AssociationExists = fun _ -> true },
            coordination = coordination)
    use! lease = coordination.FileLease CancellationToken.None
    let queued = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    use _subscription = coordination.FileWait.Subscribe(fun () -> queued.TrySetResult() |> ignore)
    let processing =
        LibraryDeletion.processItem coordination jobs (LibraryDeletionRepository.hasItem dir.Path)
            (LibraryDeletionRepository.removeMetadata coordination dir.Path)
            (LibraryDeletionRepository.finishOriginal coordination dir.Path) operation document CancellationToken.None
    do! queued.Task.WaitAsync(TimeSpan.FromSeconds 10.)
    Assert.False processing.IsCompleted
    Assert.Equal("pending", (item dir.Path operation document).Phase)
    Assert.True(File.Exists path)
    jobs.Locked(fun () -> Assert.True(coordination.IsMutationHeld))
    lease.Dispose()
    let! result = processing.WaitAsync(TimeSpan.FromSeconds 10.)
    Assert.Equal(Ok (), result)
    Assert.False(File.Exists path)
    metadataGone dir.Path document
    Assert.Equal("deleted", (item dir.Path operation document).Status) }
