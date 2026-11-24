module LexScholar.Server.Tests.LibraryExportRaceTests

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open LexScholar.Application
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Server
open Xunit

let private signal () = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
let private timeout = TimeSpan.FromSeconds 30.
let private fixture name = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))

let private output path : Stream =
    new FileStream(path, FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite,
        Share = FileShare.None, Options = FileOptions.DeleteOnClose,
        UnixCreateMode = Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite)))

let private send (host: TestHarness) (path: string) (content: HttpContent option) = task {
    use! csrf = host.Client.GetAsync("/api/antiforgery")
    let! text = csrf.Content.ReadAsStringAsync()
    use json = JsonDocument.Parse(text)
    use request = new HttpRequestMessage(HttpMethod.Post, path)
    request.Headers.Add("Origin", host.Client.BaseAddress.GetLeftPart(UriPartial.Authority))
    request.Headers.Add("X-CSRF-TOKEN", json.RootElement.GetProperty("requestToken").GetString())
    content |> Option.iter (fun value -> request.Content <- value)
    return! host.Client.SendAsync(request)
}

let private seed root =
    let document, workspace, project, other = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    let now = DateTimeOffset.UtcNow
    let bytes = fixture "valid.pdf"
    let key = document.ToString("N")
    let path = Path.Combine(DocumentStore.originalsDirectory root, key)
    File.WriteAllBytes(path, bytes)
    use db = WorkspaceDb.create root
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = now)) |> ignore
    db.SaveChanges() |> ignore
    for id, name in [project, "One"; other, "Two"] do
        db.Add(ResearchProjectRow(Id = id, WorkspaceId = workspace, Name = name, NormalizedName = name.ToUpperInvariant(), CreatedUtc = now)) |> ignore
    db.Add(LibraryDocumentRow(Id = document, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        StorageKey = key, OriginalFileName = "source.pdf", ByteLength = int64 bytes.Length, PageCount = 1, ImportedUtc = now)) |> ignore
    db.SaveChanges() |> ignore
    for id in [project; other] do
        db.Add(ProjectDocumentRow(ProjectId = id, DocumentId = document, Role = "FocusedSource", AddedUtc = now)) |> ignore
    db.SaveChanges() |> ignore
    document, project, other, path, bytes

let private summary document text =
    { DocumentId = document; Points = [{ Text = text; Pages = [1] }]; PagesCovered = [1]
      UnreadablePages = []; OmittedPoints = 0; GeneratedAt = DateTimeOffset.UtcNow
      ServiceHost = "local"; Model = "fixture" }

let private registry (host: TestHarness) =
    (host.App :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs

let private manifest (archive: ZipArchive) =
    use stream = archive.GetEntry("manifest.json").Open()
    JsonDocument.Parse(stream)

[<Theory; InlineData(false); InlineData(true); Trait("Task", "T007")>]
let ``export protects originals while import unlink and metadata mutations race`` reuse = task {
    let copying, release = signal (), new ManualResetEventSlim(false)
    use release = release
    let mutable root = ""
    let files: LibraryExport.ExportFiles =
        { CreateArchive = output
          OpenOriginal = fun key ->
              let stream = DocumentStore.openOriginal root key CancellationToken.None |> fun work -> work.GetAwaiter().GetResult()
              copying.TrySetResult() |> ignore
              if not (release.Wait(timeout)) then
                  stream.Dispose()
                  failwith "Export copy barrier timed out."
              stream }
    use host = TestHarness.Start(configure = fun app ->
        let coordination = app.Services.GetRequiredService<LibraryCoordination>()
        LibraryControlEndpoints.mapWithBuildForTests app coordination
            (LibraryExport.buildWith files app.Configuration["LexScholar:DataDirectory"]))
    root <- host.Root
    let document, project, other, path, bytes = seed root
    let jobs = registry host
    let coordination = host.App.Services.GetRequiredService<LibraryCoordination>()
    let key = { Project = project; Document = document }
    let otherKey = { Project = other; Document = document }
    let saved = summary document "Old saved version"
    SummaryRepository.save root key saved
    let entered = TaskCompletionSource<JobHandle>(TaskCreationOptions.RunContinuationsAsynchronously)
    let finish = signal ()
    let run handle = task { entered.SetResult(handle); do! finish.Task }
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, run, regenerate = true))
    let! handle = entered.Task.WaitAsync(timeout)
    let continuing = TaskCompletionSource<JobHandle>(TaskCreationOptions.RunContinuationsAsynchronously)
    let continueRun handle = task { continuing.SetResult(handle); do! finish.Task }
    let incoming = if reuse then bytes else fixture (Path.Combine("summary", "article-en.pdf"))
    let queued = signal ()
    use subscription = coordination.FileWait.Subscribe(fun () -> queued.TrySetResult() |> ignore)
    let exporting = send host "/api/library/export" None
    let mutable upload: Task<HttpResponseMessage> option = None
    let mutable deleting: Task<IDisposable> option = None
    try
        let! first = Task.WhenAny(copying.Task, exporting :> Task).WaitAsync(timeout)
        Assert.Same(copying.Task, first)
        // This is the real host import (validation/staging may proceed; dedup,
        // promotion and insertion must wait for the archive's file lease).
        let form = new MultipartFormDataContent()
        form.Add(new ByteArrayContent(incoming), "file", "incoming.pdf")
        let pending = send host "/api/documents" (Some form)
        upload <- Some pending
        let! first = Task.WhenAny(queued.Task, pending :> Task).WaitAsync(timeout)
        Assert.Same(queued.Task, first)
        Assert.False(pending.IsCompleted)
        Assert.Single(LibraryRepository.list root) |> ignore
        let waitingDeletion = coordination.FileLease(CancellationToken.None)
        deleting <- Some waitingDeletion
        Assert.False(waitingDeletion.IsCompleted)
        // A bounded progress handshake proves no registry/mutation lock is
        // held across the blocked PDF I/O or asynchronous file-lease waits.
        let changing = Task.Run(fun () ->
            jobs.Locked(fun () ->
                Assert.True(coordination.IsMutationHeld)
                Assert.True(jobs.Complete(key, handle.Id, summary document "Replacement"))
                Assert.True(LibraryRepository.changeRole root project document ReferenceSource |> Result.isOk)
                jobs.RemoveAssociation(otherKey, fun () -> LibraryRepository.removeAssociation root other document)))
        let! changed = changing.WaitAsync(timeout)
        Assert.True(Result.isOk changed)
        Assert.Equal(Started, jobs.Start(key, CancellationToken.None, continueRun, regenerate = true))
        let! stillRunning = continuing.Task.WaitAsync(timeout)
        Assert.False(stillRunning.Token.IsCancellationRequested)
        Assert.False(handle.Token.IsCancellationRequested)
        Assert.Equal("Replacement", (SummaryRepository.find root key).Value.Points.Head.Text)
        use db = WorkspaceDb.create root
        let liveAssociation = db.Set<ProjectDocumentRow>() |> Seq.exactlyOne
        Assert.Equal("ReferenceSource", liveAssociation.Role)
        Assert.False(waitingDeletion.IsCompleted)
        Assert.True(File.Exists(path))
    finally
        release.Set()
        finish.TrySetResult() |> ignore
    use! response = exporting.WaitAsync(timeout)
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let! stillRunning = continuing.Task.WaitAsync(timeout)
    Assert.False(stillRunning.Token.IsCancellationRequested)
    // Simulate the future destructive file phase through the actual shared
    // boundary; production deletion remains disabled. Unlink after build,
    // before inspecting the downloaded ZIP: delivery no longer needs originals.
    use! deletionLease = deleting.Value.WaitAsync(timeout)
    coordination.Mutation(fun () ->
        Assert.True(jobs.RemoveAssociation(key, fun () -> LibraryRepository.removeAssociation root project document) |> Result.isOk)
        use db = WorkspaceDb.create root
        db.Remove(db.Set<LibraryDocumentRow>().Find(document)) |> ignore
        db.SaveChanges() |> ignore
        File.Delete(path))
    deletionLease.Dispose()
    use! imported = upload.Value.WaitAsync(timeout)
    Assert.Equal((if reuse then HttpStatusCode.OK else HttpStatusCode.Created), imported.StatusCode)
    let! archiveBytes = response.Content.ReadAsByteArrayAsync()
    use archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read)
    use json = manifest archive
    Assert.Single(json.RootElement.GetProperty("documents").EnumerateArray()) |> ignore
    let associations = json.RootElement.GetProperty("associations").EnumerateArray() |> Seq.toList
    Assert.Equal(2, associations.Length)
    Assert.All(associations, fun item -> Assert.Equal("FocusedSource", item.GetProperty("role").GetString()))
    let old = associations |> List.find (fun item -> item.GetProperty("projectId").GetGuid() = project)
    Assert.Equal("Old saved version", (old.GetProperty("summary").GetProperty("points").[0]).GetProperty("text").GetString())
    use text = new StreamReader(archive.GetEntry($"summaries/{project:D}/{document:D}.txt").Open())
    let rendered = text.ReadToEnd()
    Assert.Contains("Old saved version", rendered)
    Assert.DoesNotContain("Replacement", rendered)
    use original = archive.GetEntry($"pdf/{document:D}.pdf").Open()
    use copied = new MemoryStream()
    original.CopyTo(copied)
    Assert.Equal<byte>(bytes, copied.ToArray())
    Assert.False(File.Exists(path))
    Assert.Empty(Directory.GetFiles(DocumentStore.exportDirectory root))
}

[<Fact; Trait("Task", "T007")>]
let ``export waits through promotion and insertion without blocking registry progress`` () = task {
    use host = TestHarness.Start(configure = fun app ->
        LibraryControlEndpoints.mapForTests app app.Configuration["LexScholar:DataDirectory"]
            (app.Services.GetRequiredService<LibraryCoordination>()))
    let document, project, _, _, _ = seed host.Root
    let coordination = host.App.Services.GetRequiredService<LibraryCoordination>()
    let promoted, release, queued = signal (), signal (), signal ()
    use subscription = coordination.FileWait.Subscribe(fun () -> queued.TrySetResult() |> ignore)
    let mutable promotedPath = ""
    // Real storage/validation/database adapters; only the post-promotion
    // handshake is injected into the application workflow.
    let import = ImportDocument.createCoordinated coordination
                    { Limits = { MaxBytes = 10_000_000L; MaxPages = 100 }
                      Validate = PdfValidator.validate
                      OpenStagedWrite = DocumentStore.openStagedWrite host.Root
                      OpenStagedRead = DocumentStore.openStagedRead host.Root
                      Promote = fun key token -> task {
                          let! storageKey = DocumentStore.promote host.Root key token
                          promotedPath <- Path.Combine(DocumentStore.originalsDirectory host.Root, storageKey)
                          promoted.SetResult()
                          do! release.Task
                          return storageKey }
                      Discard = DocumentStore.discard host.Root
                      OpenOriginal = DocumentStore.openOriginal host.Root
                      FindBySha256 = LibraryRepository.findBySha256 host.Root
                      Insert = fun row token ->
                          coordination.Mutation(fun () -> LibraryRepository.insert host.Root row token |> fun work -> work.GetAwaiter().GetResult())
                          Task.FromResult(())
                      Now = fun () -> DateTimeOffset.UtcNow }
    let bytes = fixture (Path.Combine("summary", "article-en.pdf"))
    use source = new MemoryStream(bytes)
    let importing = import source "promoted.pdf" CancellationToken.None
    do! promoted.Task.WaitAsync(timeout)
    let exporting = send host "/api/library/export" None
    try
        let! first = Task.WhenAny(queued.Task, exporting :> Task).WaitAsync(timeout)
        Assert.Same(queued.Task, first)
        Assert.True(File.Exists(promotedPath))
        Assert.Single(LibraryRepository.list host.Root) |> ignore
        Assert.False(exporting.IsCompleted)
        let jobs = registry host
        do! Task.Run(fun () ->
            jobs.Locked(fun () ->
                Assert.True(coordination.IsMutationHeld)
                Assert.True(jobs.Capabilities.AssociationExists { Project = project; Document = document }))).WaitAsync(timeout)
    finally
        release.TrySetResult() |> ignore
    let! outcome = importing.WaitAsync(timeout)
    let imported = match outcome with Ok item -> item.Document | Error failure -> failwithf "%A" failure
    use! response = exporting.WaitAsync(timeout)
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let! bytesOut = response.Content.ReadAsByteArrayAsync()
    use archive = new ZipArchive(new MemoryStream(bytesOut), ZipArchiveMode.Read)
    use json = manifest archive
    Assert.Equal(2, json.RootElement.GetProperty("documents").GetArrayLength())
    use original = archive.GetEntry($"pdf/{imported.Id:D}.pdf").Open()
    use copied = new MemoryStream()
    original.CopyTo(copied)
    Assert.Equal<byte>(bytes, copied.ToArray())
    Assert.True(File.Exists(promotedPath))
}
