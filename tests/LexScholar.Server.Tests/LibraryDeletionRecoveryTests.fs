module LexScholar.Server.Tests.LibraryDeletionRecoveryTests

open System
open System.IO
open System.Diagnostics
open System.Net
open System.Net.Http
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open LexScholar.Server
open LexScholar.Application
open LexScholar.Application.LibraryDeletion
open LexScholar.Infrastructure
open Xunit

let private seed root admitted =
    WorkspaceDb.initialize root
    DocumentStore.ensureDirectories root
    let document, operation = Guid.NewGuid(), Guid.NewGuid()
    let key = document.ToString("N")
    use db = WorkspaceDb.create root
    db.Add(LibraryDocumentRow(Id = document, StorageKey = key, OriginalFileName = "fixture.pdf",
                             Sha256 = key, ByteLength = 7L, PageCount = 1, ImportedUtc = DateTimeOffset.UtcNow)) |> ignore
    let workspace = Guid.NewGuid()
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = DateTimeOffset.UtcNow)) |> ignore
    for name, role in ["First", "FocusedSource"; "Second", "ReferenceSource"] do
        let project = Guid.NewGuid()
        db.Add(ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = name, NormalizedName = name.ToUpperInvariant(), CreatedUtc = DateTimeOffset.UtcNow)) |> ignore
        db.Add(ProjectDocumentRow(ProjectId = project, DocumentId = document, Role = role, AddedUtc = DateTimeOffset.UtcNow)) |> ignore
        db.Add(DocumentSummaryRow(ProjectId = project, DocumentId = document, Content = """{"version":1}""", GeneratedAt = DateTimeOffset.UtcNow)) |> ignore
    db.SaveChanges() |> ignore
    File.WriteAllText(Path.Combine(DocumentStore.originalsDirectory root, key), "fixture")
    let coordination = LibraryCoordination()
    if admitted then
        let preview = LibraryDeletion.preview (LibraryDeletionRepository.capture root) [document] |> Result.toOption |> Option.get
        Assert.Equal(Ok operation, LibraryDeletionRepository.admit coordination root operation [document] preview.Revision)
    coordination, operation, document, key

let private item root operation document =
    use db = WorkspaceDb.create root
    db.Set<DeletionItemRow>().Find(operation, document)

[<Fact; Trait("Task", "T014")>]
let ``production restart does not recover intents or quarantine retained deletion originals`` () =
    use workspace = TemporaryWorkspace.Create()
    let coordination, operation, document, key = seed workspace.Root true
    Assert.Equal(Ok key, LibraryDeletionRepository.removeMetadata coordination workspace.Root operation document)
    let unrelated = Guid.NewGuid().ToString("N")
    File.WriteAllText(Path.Combine(DocumentStore.originalsDirectory workspace.Root, unrelated), "orphan")
    use host = TestHarness.Start(workspace = workspace)
    Assert.True(File.Exists(Path.Combine(DocumentStore.originalsDirectory workspace.Root, key)), "Unfinished deletion must remain in originals, not quarantine.")
    Assert.False(File.Exists(Path.Combine(DocumentStore.quarantineDirectory workspace.Root, key)))
    Assert.Equal("metadata_removed", (item workspace.Root operation document).Status)
    Assert.True(File.Exists(Path.Combine(DocumentStore.quarantineDirectory workspace.Root, unrelated)))

let private recovery () =
    LibraryDeletionRecovery.StartupForTests(fun root ->
        LibraryDeletionRecovery.runForTests (LibraryCoordination()) root)

[<Fact; Trait("Task", "T014")>]
let ``test startup resumes committed intent before serving and repeated restart is terminal`` () =
    use workspace = TemporaryWorkspace.Create()
    let _, operation, document, key = seed workspace.Root true
    do
        use host = TestHarness.Start(workspace = workspace, deletionRecovery = recovery())
        Assert.Equal("deleted", (item workspace.Root operation document).Status)
        Assert.False(File.Exists(Path.Combine(DocumentStore.originalsDirectory workspace.Root, key)))
        use db = WorkspaceDb.create workspace.Root
        Assert.Empty(db.Set<LibraryDocumentRow>())
    // Completed items carry no authority to remove a reused name.
    File.WriteAllText(Path.Combine(DocumentStore.originalsDirectory workspace.Root, key), "unrelated")
    use host = TestHarness.Start(workspace = workspace, deletionRecovery = recovery())
    Assert.Equal("unrelated", File.ReadAllText(Path.Combine(DocumentStore.quarantineDirectory workspace.Root, key)))
    Assert.Equal("deleted", (item workspace.Root operation document).Status)

/// Test-executable-only mode. Exit bypasses disposal/catch/finally at the exact
/// durable boundary, unlike an exception that the repository can reconcile.
let runCrashChild (args: string array) =
    let root, operation, document, stage = args[0], Guid.Parse args[1], Guid.Parse args[2], args[3]
    use lease = DataDirectoryLock.acquire root |> Result.toOption |> Option.get
    let checkpoint name =
        if name = stage then Environment.Exit(73)
    let coordination = LibraryCoordination()
    checkpoint "intent"
    LibraryDeletionRepository.removeMetadataWith
        (fun step -> checkpoint $"metadata-{step}") coordination root operation document |> ignore
    let remove key =
        DocumentStore.removeOriginalWith (fun step -> checkpoint $"file-{step}") root key
    LibraryDeletionRepository.finishOriginalWith
        (fun step -> checkpoint $"completion-{step}") remove coordination root operation document |> ignore
    74 // A missed checkpoint must fail the parent assertion.

[<Theory;
  InlineData("intent");
  InlineData("metadata-AssociationsRemoved"); InlineData("metadata-BeforeCommit"); InlineData("metadata-AfterCommit");
  InlineData("file-BeforeUnlink"); InlineData("file-AfterUnlink"); InlineData("file-BeforeSync"); InlineData("file-AfterSync");
  InlineData("completion-BeforeCommit"); InlineData("completion-AfterCommit"); Trait("Task", "T014")>]
let ``abrupt process exit at each durable boundary recovers on same workspace`` stage = task {
    use workspace = TemporaryWorkspace.Create()
    let _, operation, document, key = seed workspace.Root true
    let info = ProcessStartInfo("dotnet", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true)
    for arg in [typeof<TemporaryWorkspace>.Assembly.Location; "--deletion-crash"; workspace.Root; string operation; string document; stage] do
        info.ArgumentList.Add arg
    use child = new Process(StartInfo = info)
    Assert.True(child.Start())
    let output = child.StandardOutput.ReadToEndAsync()
    let errors = child.StandardError.ReadToEndAsync()
    try
        do! child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 30.)
    finally
        if not child.HasExited then
            child.Kill(true)
            child.WaitForExit()
    let! _ = output
    let! error = errors
    Assert.True(child.ExitCode = 73, $"Checkpoint not reached: exit {child.ExitCode}; {error}")
    // Inspect durable state before any recovery: rollback before commit; committed
    // intent after commit even though no caller received an acknowledgement.
    let before = item workspace.Root operation document
    let metadataCommitted = stage <> "intent" && not (stage.StartsWith("metadata-") && stage <> "metadata-AfterCommit")
    Assert.Equal((if metadataCommitted then "metadata_removed" else "pending"), before.Phase)
    Assert.Equal((if stage = "completion-AfterCommit" then "deleted" elif metadataCommitted then "metadata_removed" else "pending"), before.Status)
    do
        use host = TestHarness.Start(workspace = workspace, deletionRecovery = recovery())
        Assert.Equal("deleted", (item workspace.Root operation document).Status)
        Assert.False(File.Exists(Path.Combine(DocumentStore.originalsDirectory workspace.Root, key)))
        Assert.False(File.Exists(Path.Combine(DocumentStore.quarantineDirectory workspace.Root, key)))
        use db = WorkspaceDb.create workspace.Root
        Assert.Empty(db.Set<LibraryDocumentRow>())
        Assert.Empty(db.Set<ProjectDocumentRow>())
        Assert.Empty(db.Set<DocumentSummaryRow>())
    use restarted = TestHarness.Start(workspace = workspace, deletionRecovery = recovery())
    Assert.Equal("deleted", (item workspace.Root operation document).Status)
}

[<Fact; Trait("Task", "T014")>]
let ``restart before intent never authorizes deletion`` () =
    use workspace = TemporaryWorkspace.Create()
    let _, _, document, key = seed workspace.Root false
    use host = TestHarness.Start(workspace = workspace, deletionRecovery = recovery())
    Assert.True(File.Exists(Path.Combine(DocumentStore.originalsDirectory workspace.Root, key)))
    use db = WorkspaceDb.create workspace.Root
    Assert.NotNull(db.Set<LibraryDocumentRow>().Find(document))
    Assert.Empty(db.Set<DeletionItemRow>())

[<Fact; Trait("Task", "T014")>]
let ``recovery failure stays incomplete protected from cleanup while other items progress`` () =
    use workspace = TemporaryWorkspace.Create()
    let coordination, operation, document, key = seed workspace.Root true
    let _, otherOperation, otherDocument, otherKey = seed workspace.Root true
    let _, _, untouched, untouchedKey = seed workspace.Root false
    let seam =
        LibraryDeletionRecovery.StartupForTests(fun root ->
            let remove id =
                if id = key then raise (UnauthorizedAccessException("private/path"))
                DocumentStore.removeOriginal root id
            LibraryDeletionRecovery.runWith
                (LibraryDeletionRepository.removeMetadata coordination root)
                (LibraryDeletionRepository.finishOriginalWith ignore remove coordination root) root)
    do
        use host = TestHarness.Start(workspace = workspace, deletionRecovery = seam)
        Assert.Equal("incomplete", (item workspace.Root operation document).Status)
        Assert.Equal("storage_error", (item workspace.Root operation document).Reason)
        Assert.True(File.Exists(Path.Combine(DocumentStore.originalsDirectory workspace.Root, key)))
        Assert.False(File.Exists(Path.Combine(DocumentStore.quarantineDirectory workspace.Root, key)))
        Assert.Equal("deleted", (item workspace.Root otherOperation otherDocument).Status)
        Assert.False(File.Exists(Path.Combine(DocumentStore.originalsDirectory workspace.Root, otherKey)))
        use db = WorkspaceDb.create workspace.Root
        Assert.NotNull(db.Set<LibraryDocumentRow>().Find(untouched))
        Assert.Equal(2, db.Set<ProjectDocumentRow>() |> Seq.filter (fun row -> row.DocumentId = untouched) |> Seq.length)
        Assert.Equal(2, db.Set<DocumentSummaryRow>() |> Seq.filter (fun row -> row.DocumentId = untouched) |> Seq.length)
        Assert.Equal("fixture", File.ReadAllText(Path.Combine(DocumentStore.originalsDirectory workspace.Root, untouchedKey)))
    use host = TestHarness.Start(workspace = workspace, deletionRecovery = recovery())
    Assert.Equal("deleted", (item workspace.Root operation document).Status)

[<Theory; InlineData(false, false); InlineData(false, true); InlineData(true, false); InlineData(true, true); Trait("Task", "T014")>]
let ``startup rereads uncertain commits and later restart retries failed transactions`` completion afterCommit =
    use workspace = TemporaryWorkspace.Create()
    let coordination, operation, document, key = seed workspace.Root true
    let seam =
        LibraryDeletionRecovery.StartupForTests(fun root ->
            let metadataCheckpoint step =
                if not completion && step = (if afterCommit then LibraryDeletionRepository.MetadataStep.AfterCommit else LibraryDeletionRepository.MetadataStep.BeforeCommit) then
                    raise (IOException("lost acknowledgement"))
            let completionCheckpoint step =
                if completion && step = (if afterCommit then LibraryDeletionRepository.CompletionStep.AfterCommit else LibraryDeletionRepository.CompletionStep.BeforeCommit) then
                    raise (IOException("lost acknowledgement"))
            LibraryDeletionRecovery.runWith
                (LibraryDeletionRepository.removeMetadataWith metadataCheckpoint coordination root)
                (LibraryDeletionRepository.finishOriginalWith completionCheckpoint (DocumentStore.removeOriginal root) coordination root) root)
    do
        use host = TestHarness.Start(workspace = workspace, deletionRecovery = seam)
        Assert.Equal((if afterCommit then "deleted" else "incomplete"), (item workspace.Root operation document).Status)
        Assert.Equal((not completion && not afterCommit), File.Exists(Path.Combine(DocumentStore.originalsDirectory workspace.Root, key)))
        Assert.False(File.Exists(Path.Combine(DocumentStore.quarantineDirectory workspace.Root, key)))
    use host = TestHarness.Start(workspace = workspace, deletionRecovery = recovery())
    Assert.Equal("deleted", (item workspace.Root operation document).Status)

[<Fact; Trait("Task", "T014")>]
let ``production environment ignores even explicitly injected recovery`` () =
    use workspace = TemporaryWorkspace.Create()
    let _, operation, document, key = seed workspace.Root true
    let mutable called = false
    let seam = LibraryDeletionRecovery.StartupForTests(fun _ -> called <- true)
    use host = TestHarness.Start(workspace = workspace, environment = "Production", deletionRecovery = seam)
    Assert.False called
    Assert.Equal("pending", (item workspace.Root operation document).Status)
    Assert.True(File.Exists(Path.Combine(DocumentStore.originalsDirectory workspace.Root, key)))

let private renameJournal root name =
    use connection = new SqliteConnection(WorkspaceDb.connectionString root)
    connection.Open()
    use command = connection.CreateCommand()
    command.CommandText <- name
    command.ExecuteNonQuery() |> ignore

let private hideJournal root =
    // Migrations remain applied; only the cleanup journal read fails. Rename
    // rather than delete so retained intents can be restored for the next run.
    renameJournal root "ALTER TABLE DeletionItems RENAME TO RetainedDeletionItems"

let private restoreJournal root =
    renameJournal root "ALTER TABLE RetainedDeletionItems RENAME TO DeletionItems"

type private StartupLogs() =
    let entries = System.Collections.Concurrent.ConcurrentQueue<LogLevel * string * exn>()
    member _.Entries = entries |> Seq.toList
    interface ILoggerProvider with
        member _.CreateLogger(_category) =
            { new ILogger with
                member _.BeginScope(_state) = null
                member _.IsEnabled(_level) = true
                member _.Log(level, _eventId, state, error, formatter) =
                    entries.Enqueue(level, formatter.Invoke(state, error), error) }
        member _.Dispose() = ()

[<Fact; Trait("Task", "T014")>]
let ``unreadable journal skips all orphan cleanup without losing originals or intent`` () =
    use workspace = TemporaryWorkspace.Create()
    let coordination, operation, document, key = seed workspace.Root true
    Assert.Equal(Ok key, LibraryDeletionRepository.removeMetadata coordination workspace.Root operation document)
    let orphan = Guid.NewGuid().ToString("N")
    let original name = Path.Combine(DocumentStore.originalsDirectory workspace.Root, name)
    File.WriteAllText(original orphan, "unrelated orphan")
    hideJournal workspace.Root
    let mutable failed = false
    let error = Record.Exception(fun () ->
        let report = StorageCleanup.run workspace.Root
        failed <- report.DeletionJournalReadFailed
        Assert.Empty report.Quarantined
        Assert.Empty report.Conflicting)
    Assert.Null error
    Assert.True failed
    for name in [key; orphan] do
        Assert.True(File.Exists(original name))
        Assert.False(File.Exists(Path.Combine(DocumentStore.quarantineDirectory workspace.Root, name)))
    restoreJournal workspace.Root
    Assert.Equal("metadata_removed", (item workspace.Root operation document).Status)
    let report = StorageCleanup.run workspace.Root
    Assert.False report.DeletionJournalReadFailed
    Assert.Equal<string list>([orphan], report.Quarantined)
    Assert.True(File.Exists(original key))

[<Fact; Trait("Task", "T014")>]
let ``production serves workspace with sanitized warning when deletion journal cannot be read`` () = task {
    use workspace = TemporaryWorkspace.Create()
    let _, operation, document, key = seed workspace.Root true
    hideJournal workspace.Root
    let logs = new StartupLogs()
    let builder = HostSetup.builder workspace.Root []
    builder.Environment.EnvironmentName <- "Production"
    builder.Logging.AddProvider(logs) |> ignore
    let mutable host = None
    let error = Record.Exception(fun () ->
        match Program.create builder false with
        | Ok app -> host <- Some app
        | Error rejected -> failwithf "Unexpected rejection: %A" rejected)
    Assert.Null error
    use app = host |> Option.get
    do! app.StartAsync()
    try
        let server = app.Services.GetRequiredService<IServer>()
        let address = server.Features.Get<IServerAddressesFeature>().Addresses |> Seq.exactlyOne
        use client = new HttpClient(BaseAddress = Uri(address))
        use! response = client.GetAsync("/api/projects")
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let warning =
            logs.Entries |> List.filter (fun (level, text, _) ->
                level = LogLevel.Warning && text.Contains("deletion journal"))
            |> Assert.Single
        let _, text, exception' = warning
        Assert.Equal("Could not read the deletion journal; original-file cleanup was skipped and will be retried at next startup.", text)
        Assert.Null exception'
        Assert.DoesNotContain(workspace.Root, text)
        Assert.DoesNotContain(key, text)
        Assert.DoesNotContain("Sqlite", text)
        Assert.True(File.Exists(Path.Combine(DocumentStore.originalsDirectory workspace.Root, key)))
    finally
        app.StopAsync().GetAwaiter().GetResult()
    restoreJournal workspace.Root
    Assert.Equal("pending", (item workspace.Root operation document).Status)
}
