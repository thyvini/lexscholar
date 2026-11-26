module LexScholar.Server.Tests.LibraryDeletionImportRaceTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open LexScholar.Application
open LexScholar.Domain
open LexScholar.Infrastructure
open Xunit

let private timeout = TimeSpan.FromSeconds 30.
let private signal () = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
let private bytes () = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
let private jobs (host: TestHarness) =
    (host.App :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
let private coordination (host: TestHarness) = host.App.Services.GetRequiredService<LibraryCoordination>()

let private dependencies (host: TestHarness) : ImportDocument.ImportDependencies =
    { Limits = { MaxBytes = 10_000_000L; MaxPages = 100 }
      Validate = PdfValidator.validate
      OpenStagedWrite = DocumentStore.openStagedWrite host.Root
      OpenStagedRead = DocumentStore.openStagedRead host.Root
      Promote = DocumentStore.promote host.Root
      Discard = DocumentStore.discard host.Root
      OpenOriginal = DocumentStore.openOriginal host.Root
      FindBySha256 = LibraryRepository.findBySha256 host.Root
      Insert = fun row token ->
          coordination host |> fun c ->
              c.Mutation(fun () -> LibraryRepository.insert host.Root row token |> fun t -> t.GetAwaiter().GetResult())
          Task.FromResult(())
      Now = fun () -> DateTimeOffset.UtcNow }

let private import host deps = task {
    use source = new MemoryStream(bytes ())
    let! result = ImportDocument.createCoordinated (coordination host) deps source "fixture.pdf" CancellationToken.None
    return result |> Result.toOption |> Option.get
}

let private admit (host: TestHarness) ids =
    let preview = LibraryDeletion.preview (LibraryDeletionRepository.capture host.Root) ids |> Result.toOption |> Option.get
    let operation = Guid.NewGuid()
    let result = LibraryDeletion.admitFenced (jobs host)
                     (LibraryDeletionRepository.admit (coordination host) host.Root) operation ids preview.Revision
    operation, result

let private processDocument host operation document finish =
    LibraryDeletion.processItem (coordination host) (jobs host)
        (LibraryDeletionRepository.hasItem host.Root)
        (LibraryDeletionRepository.removeMetadata (coordination host) host.Root)
        finish operation document CancellationToken.None

let private finish host = LibraryDeletionRepository.finishOriginal (coordination host) host.Root

let private assertNew (host: TestHarness) (old: LibraryDocument) (imported: ImportDocument.ImportOutcome) =
    Assert.False(imported.Reused)
    Assert.NotEqual(old.Id, imported.Document.Id)
    Assert.NotEqual<string>(old.StorageKey, imported.Document.StorageKey)
    Assert.Equal<byte>(bytes (), File.ReadAllBytes(Path.Combine(DocumentStore.originalsDirectory host.Root, imported.Document.StorageKey)))

[<Theory; InlineData(false); InlineData(true); Trait("Task", "T015")>]
let ``intent before lookup or during byte comparison cannot reuse selected identity`` duringComparison = task {
    use host = TestHarness.Start()
    let deps = dependencies host
    let! original = import host deps
    let entered, release = signal (), signal ()
    let held =
        { deps with
            OpenOriginal = fun key token -> task {
              let! stream = deps.OpenOriginal key token
              entered.TrySetResult() |> ignore
              do! release.Task
              return stream } }
    let mutable operation = Guid.Empty
    if not duringComparison then
        let id, result = admit host [original.Document.Id]
        operation <- id
        Assert.Equal(Ok id, result)
    let importing = import host (if duringComparison then held else deps)
    try
        if duringComparison then
            do! entered.Task.WaitAsync(timeout)
            let id, result = admit host [original.Document.Id]
            operation <- id
            Assert.Equal(Ok id, result)
    finally
        release.TrySetResult() |> ignore
    let! imported = importing.WaitAsync(timeout)
    assertNew host original.Document imported
    Assert.True(File.Exists(Path.Combine(DocumentStore.originalsDirectory host.Root, original.Document.StorageKey)))
    let! removed = processDocument host operation original.Document.Id (finish host)
    Assert.Equal(Ok (), removed)
    Assert.False(File.Exists(Path.Combine(DocumentStore.originalsDirectory host.Root, original.Document.StorageKey)))
    Assert.True(File.Exists(Path.Combine(DocumentStore.originalsDirectory host.Root, imported.Document.StorageKey)))
    Assert.Equal<byte>(bytes (), File.ReadAllBytes(Path.Combine(DocumentStore.originalsDirectory host.Root, imported.Document.StorageKey)))
}

[<Fact; Trait("Task", "T015")>]
let ``promotion excludes processing while intent and unrelated registry work progress`` () = task {
    use host = TestHarness.Start()
    let deps = dependencies host
    let! original = import host deps
    let operation, result = admit host [original.Document.Id]
    Assert.Equal(Ok operation, result)
    let promoted, release, waiting = signal (), signal (), signal ()
    let held =
        { deps with
            Promote = fun key token -> task {
              let! stored = deps.Promote key token
              promoted.TrySetResult() |> ignore
              do! release.Task
              return stored } }
    let importing = import host held
    let mutable deleting = None
    try
        let! first = Task.WhenAny(promoted.Task, importing :> Task).WaitAsync(timeout)
        Assert.Same(promoted.Task, first)
        use subscription = (coordination host).FileWait.Subscribe(fun () -> waiting.TrySetResult() |> ignore)
        let work = processDocument host operation original.Document.Id (finish host)
        deleting <- Some work
        let! first = Task.WhenAny(waiting.Task, work :> Task).WaitAsync(timeout)
        Assert.Same(waiting.Task, first)
        Assert.False(work.IsCompleted)
        do! Task.Run(fun () -> (jobs host).Locked(fun () -> Assert.True((coordination host).IsMutationHeld))).WaitAsync(timeout)
    finally
        release.TrySetResult() |> ignore
    let! imported = importing.WaitAsync(timeout)
    assertNew host original.Document imported
    let! removed = deleting.Value.WaitAsync(timeout)
    Assert.Equal(Ok (), removed)
    Assert.Single(LibraryRepository.list host.Root) |> ignore
}

[<Fact; Trait("Task", "T015")>]
let ``unlink excludes reimport then new association works and old admission stays fenced`` () = task {
    use host = TestHarness.Start()
    let deps = dependencies host
    let! original = import host deps
    let workspace, project = Guid.NewGuid(), Guid.NewGuid()
    do
        use db = WorkspaceDb.create host.Root
        db.Add(WorkspaceRow(Id = workspace, CreatedUtc = DateTimeOffset.UtcNow)) |> ignore
        db.Add(ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = "Fixture", NormalizedName = "FIXTURE", CreatedUtc = DateTimeOffset.UtcNow)) |> ignore
        db.SaveChanges() |> ignore
    let associate document =
        (coordination host).Mutation(fun () ->
            LibraryRepository.associate host.Root { ProjectId = project; DocumentId = document; Role = FocusedSource; AddedUtc = DateTimeOffset.UtcNow })
    Assert.True(associate original.Document.Id |> Result.isOk)
    let operation, result = admit host [original.Document.Id]
    Assert.Equal(Ok operation, result)
    Assert.True(associate original.Document.Id |> Result.isError)
    let key = { Project = project; Document = original.Document.Id }
    Assert.NotEqual(Started, (jobs host).Start(key, CancellationToken.None, fun _ -> Task.FromResult(())))
    use release = new ManualResetEventSlim(false)
    let unlinking, waiting = signal (), signal ()
    let remove storage =
        unlinking.TrySetResult() |> ignore
        if not (release.Wait(timeout)) then failwith "Unlink barrier timed out"
        DocumentStore.removeOriginal host.Root storage
    let complete = LibraryDeletionRepository.finishOriginalWith ignore remove (coordination host) host.Root
    let deleting = Task.Run<Result<unit, LibraryDeletion.MetadataError>>(fun () -> processDocument host operation original.Document.Id complete)
    let mutable importing = None
    try
        do! unlinking.Task.WaitAsync(timeout)
        use subscription = (coordination host).FileWait.Subscribe(fun () -> waiting.TrySetResult() |> ignore)
        let work = import host deps
        importing <- Some work
        let! first = Task.WhenAny(waiting.Task, work :> Task).WaitAsync(timeout)
        Assert.Same(waiting.Task, first)
        Assert.False(work.IsCompleted)
    finally
        release.Set()
    let! removed = deleting.WaitAsync(timeout)
    Assert.Equal(Ok (), removed)
    let! imported = importing.Value.WaitAsync(timeout)
    assertNew host original.Document imported
    Assert.True(associate imported.Document.Id |> Result.isOk)
    // F001 removal remains association-only.
    Assert.Equal(Ok (), (jobs host).RemoveAssociation({ key with Document = imported.Document.Id },
        fun () -> LibraryRepository.removeAssociation host.Root project imported.Document.Id))
    Assert.True(File.Exists(Path.Combine(DocumentStore.originalsDirectory host.Root, imported.Document.StorageKey)))
}

[<Fact; Trait("Task", "T017")>]
let ``production confirmation deletes an imported original through the complete processor`` () = task {
    use host = TestHarness.Start()
    let! imported = import host (dependencies host)
    let ids = [imported.Document.Id]
    let preview = LibraryDeletion.preview (LibraryDeletionRepository.capture host.Root) ids |> Result.toOption |> Option.get
    use! csrfResponse = host.Client.GetAsync("/api/antiforgery")
    Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode)
    let! csrfText = csrfResponse.Content.ReadAsStringAsync()
    use csrf = JsonDocument.Parse(csrfText)
    use request = new HttpRequestMessage(HttpMethod.Post, "/api/library/deletions")
    request.Headers.Add("Origin", host.Client.BaseAddress.GetLeftPart(UriPartial.Authority))
    request.Headers.Add("X-CSRF-TOKEN", csrf.RootElement.GetProperty("requestToken").GetString())
    let operation = Guid.NewGuid()
    request.Content <- new StringContent(
        JsonSerializer.Serialize({| operationId = operation; documentIds = ids; revision = preview.Revision |}),
        Encoding.UTF8, "application/json")
    use! response = host.Client.SendAsync(request)
    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode)
    let operations = (host.App :> Microsoft.AspNetCore.Builder.IApplicationBuilder).Properties["LexScholar.LibraryDeletions"] :?> LibraryDeletion.Operations
    do! (operations.Settled operation).WaitAsync(TimeSpan.FromSeconds 10.0)
    use db = WorkspaceDb.create host.Root
    Assert.Single(db.Set<DeletionOperationRow>()) |> ignore
    Assert.All(db.Set<DeletionItemRow>(), fun item -> Assert.Equal("deleted", item.Status))
    Assert.Empty(LibraryRepository.list host.Root)
    Assert.False(File.Exists(Path.Combine(DocumentStore.originalsDirectory host.Root, imported.Document.StorageKey)))
}

[<Fact; Trait("Task", "T015")>]
let ``overlap never partially admits and unrelated imports survive selected processing`` () = task {
    use host = TestHarness.Start()
    let deps = dependencies host
    let! selected = import host deps
    let! duplicate = import host deps
    Assert.True(duplicate.Reused)
    let operation, result = admit host [selected.Document.Id]
    Assert.Equal(Ok operation, result)
    // A different valid fixture can be imported while another document is pending.
    use source = new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", "article-en.pdf")))
    let! outcome = ImportDocument.createCoordinated (coordination host) deps source "other.pdf" CancellationToken.None
    let other = (outcome |> Result.toOption |> Option.get).Document
    let _, refused = admit host [selected.Document.Id; other.Id]
    Assert.Equal(Error LibraryDeletion.AdmissionError.DeletionInProgress, refused)
    do
        use db = WorkspaceDb.create host.Root
        Assert.Single(db.Set<DeletionOperationRow>()) |> ignore
        Assert.Single(db.Set<DeletionItemRow>()) |> ignore
    let! removed = processDocument host operation selected.Document.Id (finish host)
    Assert.Equal(Ok (), removed)
    Assert.Equal(other.Id, (LibraryRepository.list host.Root |> List.exactlyOne).Id)
    Assert.True(File.Exists(Path.Combine(DocumentStore.originalsDirectory host.Root, other.StorageKey)))
}
