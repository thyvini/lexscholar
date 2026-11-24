module LexScholar.Server.Tests.LibraryExportFailureTests

open System
open System.IO
open System.Net
open System.Net.Http
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

let private start () =
    TestHarness.Start(configure = fun app ->
        LibraryControlEndpoints.mapForTests app app.Configuration["LexScholar:DataDirectory"] (app.Services.GetRequiredService<LibraryCoordination>()))

let private seed root =
    let id = Guid.NewGuid()
    let bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
    let key = id.ToString("N")
    DocumentStore.ensureDirectories root
    let path = Path.Combine(DocumentStore.originalsDirectory root, key)
    File.WriteAllBytes(path, bytes)
    use db = WorkspaceDb.create root
    db.Add(LibraryDocumentRow(Id = id, Sha256 = "fixture", StorageKey = key,
        OriginalFileName = "source.pdf", ByteLength = int64 bytes.Length, PageCount = 1,
        ImportedUtc = DateTimeOffset.UtcNow)) |> ignore
    db.SaveChanges() |> ignore
    id, path

let private export (host: TestHarness) = task {
    use! response = host.Client.GetAsync("/api/antiforgery")
    use! body = response.Content.ReadAsStreamAsync()
    use json = JsonDocument.Parse(body)
    use request = new HttpRequestMessage(HttpMethod.Post, "/api/library/export")
    request.Headers.Add("Origin", host.Client.BaseAddress.GetLeftPart(UriPartial.Authority))
    request.Headers.Add("X-CSRF-TOKEN", json.RootElement.GetProperty("requestToken").GetString())
    return! host.Client.SendAsync(request)
}

[<Theory; InlineData(false); InlineData(true); Trait("Task", "T006")>]
let ``missing and wrong length originals identify the document without paths or successful ZIP`` truncate = task {
    use host = start ()
    let id, path = seed host.Root
    if truncate then File.WriteAllBytes(path, [| 1uy |]) else File.Delete(path)
    use! response = export host
    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)
    let! text = response.Content.ReadAsStringAsync()
    use json = JsonDocument.Parse(text)
    Assert.Equal("document_unavailable", json.RootElement.GetProperty("code").GetString())
    Assert.True(json.RootElement.TryGetProperty("documentId") |> fst, "Failure must identify its document.")
    Assert.Equal(id, json.RootElement.GetProperty("documentId").GetGuid())
    Assert.Equal("source.pdf", json.RootElement.GetProperty("name").GetString())
    Assert.DoesNotContain(host.Root, text)
    Assert.NotEqual<string>("application/zip", response.Content.Headers.ContentType.MediaType)
    Assert.Empty(Directory.GetFiles(Path.Combine(host.Root, "exports")))
    Assert.Single(LibraryRepository.list host.Root) |> ignore
}

[<Fact; Trait("Task", "T006")>]
let ``restart removes abandoned private exports without touching originals or library`` () =
    use workspace = TemporaryWorkspace.Create()
    let id, path =
        use host = TestHarness.Start(workspace = workspace)
        seed host.Root
    let staging = Path.Combine(workspace.Root, "exports")
    Directory.CreateDirectory(staging) |> ignore
    File.WriteAllText(Path.Combine(staging, "abandoned.zip"), "interrupted archive")
    use restarted = TestHarness.Start(workspace = workspace)
    Assert.Empty(Directory.GetFileSystemEntries(staging))
    Assert.True(File.Exists(path))
    Assert.Equal(id, (LibraryRepository.list restarted.Root |> List.exactlyOne).Id)

[<Theory; InlineData(false); InlineData(true); Trait("Task", "T006")>]
let ``inaccessible abandoned exports do not block restart or relax permissions and cleanup retries later`` enumeration =
    use workspace = TemporaryWorkspace.Create()
    let id, original =
        use host = TestHarness.Start(workspace = workspace)
        seed host.Root
    let staging = DocumentStore.exportDirectory workspace.Root
    Directory.CreateDirectory(staging) |> ignore
    let abandoned = Path.Combine(staging, "abandoned.zip")
    File.WriteAllText(abandoned, "private interrupted archive")
    File.SetUnixFileMode(abandoned, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
    let restricted =
        if enumeration then UnixFileMode.UserExecute
        else UnixFileMode.UserRead ||| UnixFileMode.UserExecute
    File.SetUnixFileMode(staging, restricted)
    try
        // Prove this environment actually exercises the failing filesystem
        // operation (rather than passing under a privileged test account).
        Assert.ThrowsAny<UnauthorizedAccessException>(fun () ->
            if enumeration then Directory.GetFileSystemEntries(staging) |> ignore
            else File.Delete(abandoned)) |> ignore
        Assert.True((StorageCleanup.run workspace.Root).ExportCleanupIncomplete)
        for _ in 1..2 do
            let error = Record.Exception(fun () ->
                use restarted = TestHarness.Start(workspace = workspace)
                Assert.Equal(id, (LibraryRepository.list restarted.Root |> List.exactlyOne).Id)
                Assert.True(File.Exists(original)))
            Assert.Null(error)
            Assert.True(File.Exists(abandoned))
            Assert.Equal(restricted, File.GetUnixFileMode(staging))
            Assert.Equal(UnixFileMode.UserRead ||| UnixFileMode.UserWrite, File.GetUnixFileMode(abandoned))
    finally
        File.SetUnixFileMode(staging, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
    use retried = TestHarness.Start(workspace = workspace)
    Assert.Empty(Directory.GetFileSystemEntries(staging))
    Assert.True(File.Exists(original))
    Assert.Equal(id, (LibraryRepository.list retried.Root |> List.exactlyOne).Id)
    Assert.False((StorageCleanup.run workspace.Root).ExportCleanupIncomplete)

[<Fact; Trait("Task", "T006")>]
let ``one undeletable export entry does not prevent other abandoned export cleanup`` () =
    use workspace = TemporaryWorkspace.Create()
    do
        use host = TestHarness.Start(workspace = workspace)
        seed host.Root |> ignore
    let staging = DocumentStore.exportDirectory workspace.Root
    let blocked = Path.Combine(staging, "blocked")
    Directory.CreateDirectory(blocked) |> ignore
    let retained = Path.Combine(blocked, "private.zip")
    let removable = Path.Combine(staging, "removable.zip")
    File.WriteAllText(retained, "keep private until cleanup is possible")
    File.WriteAllText(removable, "can be cleaned now")
    File.SetUnixFileMode(blocked, UnixFileMode.UserRead ||| UnixFileMode.UserExecute)
    try
        Assert.ThrowsAny<UnauthorizedAccessException>(fun () -> File.Delete(retained)) |> ignore
        let report = StorageCleanup.run workspace.Root
        Assert.True(report.ExportCleanupIncomplete)
        Assert.True(File.Exists(retained))
        Assert.False(File.Exists(removable))
        Assert.Equal(UnixFileMode.UserRead ||| UnixFileMode.UserExecute, File.GetUnixFileMode(blocked))
    finally
        File.SetUnixFileMode(blocked, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

type private ReadStream(length: int64, read: byte[] -> int -> int -> int) =
    inherit Stream()
    override _.CanRead = true
    override _.CanWrite = false
    override _.CanSeek = false
    override _.Length = length
    override _.Position with get () = 0L and set _ = raise (NotSupportedException())
    override _.Read(buffer, offset, count) = read buffer offset count
    override _.Flush() = ()
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength(_) = raise (NotSupportedException())
    override _.Write(_, _, _) = raise (NotSupportedException())

let private output path : Stream =
    let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite,
                                   Share = FileShare.None, Options = FileOptions.DeleteOnClose,
                                   UnixCreateMode = Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite))
    new FileStream(path, options)

let private startWith files =
    TestHarness.Start(configure = fun app ->
        LibraryControlEndpoints.mapWithBuildForTests app
            (app.Services.GetRequiredService<LibraryCoordination>())
            (LibraryExport.buildWith files app.Configuration["LexScholar:DataDirectory"]))

[<Theory; InlineData("unreadable"); InlineData("read"); InlineData("short"); InlineData("long"); Trait("Task", "T006")>]
let ``source faults never yield an incomplete successful archive`` fault = task {
    let files: LibraryExport.ExportFiles =
        { CreateArchive = output
          OpenOriginal = fun _ ->
            if fault = "unreadable" then raise (UnauthorizedAccessException("private path"))
            let bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
            let mutable position = 0
            new ReadStream(int64 bytes.Length, fun buffer offset count ->
                if fault = "read" then raise (IOException("private path"))
                let length = if fault = "short" then bytes.Length - 1 else bytes.Length + 1
                let copied = min count (length - position)
                Array.Fill(buffer, 0uy, offset, copied)
                position <- position + copied
                copied) }
    use host = startWith files
    let id, _ = seed host.Root
    use! response = export host
    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)
    let! text = response.Content.ReadAsStringAsync()
    use json = JsonDocument.Parse(text)
    Assert.Equal(id, json.RootElement.GetProperty("documentId").GetGuid())
    Assert.DoesNotContain("private path", text)
    Assert.Empty(Directory.GetFiles(DocumentStore.exportDirectory host.Root))
    Assert.Single(LibraryRepository.list host.Root) |> ignore
}

type private FullDiskStream(inner: Stream, full: unit -> bool) =
    inherit Stream()
    override _.CanRead = inner.CanRead
    override _.CanWrite = true
    override _.CanSeek = inner.CanSeek
    override _.Length = inner.Length
    override _.Position with get () = inner.Position and set value = inner.Position <- value
    override _.Read(b, o, c) = inner.Read(b, o, c)
    override _.Flush() = inner.Flush()
    override _.Seek(o, origin) = inner.Seek(o, origin)
    override _.SetLength(value) = inner.SetLength(value)
    override _.Write(b, o, c) =
        if full () then raise (IOException("No space left: private path"))
        inner.Write(b, o, c)
    override _.Dispose(disposing) =
        if disposing then inner.Dispose()
        base.Dispose(disposing)

[<Theory; InlineData(false); InlineData(true); Trait("Task", "T006")>]
let ``archive creation and disk full faults return sanitized export failure and remove staging`` diskFull = task {
    let mutable copyingPdf = false
    let files: LibraryExport.ExportFiles =
        { OpenOriginal = fun _ ->
              copyingPdf <- true
              new MemoryStream(Array.zeroCreate 1024) :> Stream
          CreateArchive = fun path ->
            if diskFull then new FullDiskStream(output path, fun () -> copyingPdf) :> Stream
            else raise (IOException("private path")) }
    use host = startWith files
    let _, _ = seed host.Root
    // Make the fake input length agree so any failure is archive-side.
    do
        use db = WorkspaceDb.create host.Root
        let row = db.Set<LibraryDocumentRow>() |> Seq.exactlyOne
        row.ByteLength <- 1024L
        db.SaveChanges() |> ignore
    use! response = export host
    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode)
    let! text = response.Content.ReadAsStringAsync()
    use json = JsonDocument.Parse(text)
    Assert.Equal("export_failed", json.RootElement.GetProperty("code").GetString())
    Assert.False(json.RootElement.TryGetProperty("documentId") |> fst)
    Assert.DoesNotContain("private path", text)
    Assert.Empty(Directory.GetFiles(DocumentStore.exportDirectory host.Root))
}

[<Fact; Trait("Task", "T006")>]
let ``PDF copying is bounded and cancellation disposes private archive and source`` () =
    use host = TestHarness.Start()
    let _, _ = seed host.Root
    use cancellation = new CancellationTokenSource()
    let mutable maximum = 0
    let mutable reads = 0
    let mutable sourceDisposed = false
    let files: LibraryExport.ExportFiles =
        { CreateArchive = output
          OpenOriginal = fun _ ->
            { new ReadStream(1024L * 1024L * 1024L, fun buffer offset count ->
                maximum <- max maximum count
                reads <- reads + 1
                Array.Fill(buffer, 0uy, offset, count)
                if reads = 3 then cancellation.Cancel()
                count) with
                override _.Dispose(disposing) =
                    if disposing then sourceDisposed <- true } }
    do
        use db = WorkspaceDb.create host.Root
        let row = db.Set<LibraryDocumentRow>() |> Seq.exactlyOne
        row.ByteLength <- 1024L * 1024L * 1024L
        db.SaveChanges() |> ignore
    Assert.ThrowsAny<OperationCanceledException>(fun () ->
        LibraryExport.buildWith files host.Root cancellation.Token |> ignore) |> ignore
    Assert.Equal(3, reads)
    Assert.InRange(maximum, 1, 81_920)
    Assert.True(sourceDisposed)
    Assert.Empty(Directory.GetFiles(DocumentStore.exportDirectory host.Root))

[<Fact; Trait("Task", "T006")>]
let ``complete private archive is file backed private and deleted on disposal`` () =
    use host = TestHarness.Start()
    let _, _ = seed host.Root
    match LibraryExport.build host.Root CancellationToken.None with
    | Error failure -> failwithf "%A" failure
    | Ok archive ->
        let path = Directory.GetFiles(DocumentStore.exportDirectory host.Root) |> Array.exactlyOne
        Assert.IsType<FileStream>(archive) |> ignore
        Assert.Equal(UnixFileMode.UserRead ||| UnixFileMode.UserWrite, File.GetUnixFileMode(path))
        Assert.Equal(UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute,
                     File.GetUnixFileMode(DocumentStore.exportDirectory host.Root))
        Assert.Equal(0L, archive.Position)
        archive.Dispose()
        Assert.False(File.Exists(path))

type private HeldTransfer(inner: Stream, waiting: TaskCompletionSource, disposed: TaskCompletionSource, ?fail: bool) =
    inherit Stream()
    let mutable reads = 0
    let read (buffer: Memory<byte>) (cancellation: CancellationToken) = task {
        reads <- reads + 1
        if reads = 1 then
            return! inner.ReadAsync(buffer.Slice(0, min buffer.Length 1024), cancellation).AsTask()
        else
            if defaultArg fail false then raise (IOException("Injected transfer read failure"))
            let cancelled = TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously)
            use registration = cancellation.Register(fun () -> cancelled.TrySetCanceled(cancellation) |> ignore)
            waiting.TrySetResult() |> ignore
            return! cancelled.Task
    }
    override _.CanRead = true
    override _.CanSeek = false
    override _.CanWrite = false
    override _.Length = inner.Length
    override _.Position with get () = inner.Position and set _ = raise (NotSupportedException())
    override _.Read(b, o, c) = inner.Read(b, o, c)
    override _.ReadAsync(buffer: Memory<byte>, cancellation) = ValueTask<int>(read buffer cancellation)
    override _.ReadAsync(buffer: byte[], offset, count, cancellation) = read (Memory(buffer, offset, count)) cancellation
    override _.Flush() = ()
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength(_) = raise (NotSupportedException())
    override _.Write(_, _, _) = raise (NotSupportedException())
    override _.Dispose(disposing) =
        if disposing then
            inner.Dispose()
            disposed.TrySetResult() |> ignore
        base.Dispose(disposing)

[<Fact; Trait("Task", "T006")>]
let ``disconnect during a real HTTP transfer disposes the complete archive without changing library`` () = task {
    let waiting = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let disposed = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    use host = TestHarness.Start(configure = fun app ->
        let build token =
            LibraryExport.build app.Configuration["LexScholar:DataDirectory"] token
            |> Result.map (fun archive -> new HeldTransfer(archive, waiting, disposed) :> Stream)
        LibraryControlEndpoints.mapWithBuildForTests app
            (app.Services.GetRequiredService<LibraryCoordination>()) build)
    let id, path = seed host.Root
    // No drain: abandoning this response must disconnect the socket, rather
    // than silently finishing the download in HttpClient's connection pool.
    use handler = new SocketsHttpHandler(MaxResponseDrainSize = 0)
    use client = new HttpClient(handler, BaseAddress = host.Client.BaseAddress)
    use! csrf = client.GetAsync("/api/antiforgery")
    let! csrfText = csrf.Content.ReadAsStringAsync()
    use json = JsonDocument.Parse(csrfText)
    use request = new HttpRequestMessage(HttpMethod.Post, "/api/library/export")
    request.Headers.Add("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority))
    request.Headers.Add("X-CSRF-TOKEN", json.RootElement.GetProperty("requestToken").GetString())
    use! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    do! waiting.Task.WaitAsync(TimeSpan.FromSeconds(10.))
    Assert.Single(Directory.GetFiles(DocumentStore.exportDirectory host.Root)) |> ignore
    response.Dispose()
    do! disposed.Task.WaitAsync(TimeSpan.FromSeconds(10.))
    Assert.Empty(Directory.GetFiles(DocumentStore.exportDirectory host.Root))
    Assert.True(File.Exists(path))
    Assert.Equal(id, (LibraryRepository.list host.Root |> List.exactlyOne).Id)
}

[<Fact; Trait("Task", "T006")>]
let ``transfer read failure is not a successful download and removes the complete staging file`` () = task {
    let waiting = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let disposed = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    use host = TestHarness.Start(configure = fun app ->
        let build token =
            LibraryExport.build app.Configuration["LexScholar:DataDirectory"] token
            |> Result.map (fun archive -> new HeldTransfer(archive, waiting, disposed, fail = true) :> Stream)
        LibraryControlEndpoints.mapWithBuildForTests app
            (app.Services.GetRequiredService<LibraryCoordination>()) build)
    seed host.Root |> ignore
    let! _ = Assert.ThrowsAnyAsync<HttpRequestException>(fun () -> task {
        use! response = export host
        return ()
    })
    do! disposed.Task.WaitAsync(TimeSpan.FromSeconds(10.))
    Assert.Empty(Directory.GetFiles(DocumentStore.exportDirectory host.Root))
    Assert.Single(LibraryRepository.list host.Root) |> ignore
}

[<Fact; Trait("Task", "T006")>]
let ``failed export leaves durable summary association and running regeneration untouched`` () = task {
    use host = start ()
    let document, path = seed host.Root
    let workspace, project = Guid.NewGuid(), Guid.NewGuid()
    let now = DateTimeOffset.UtcNow
    do
        use db = WorkspaceDb.create host.Root
        db.Add(WorkspaceRow(Id = workspace, CreatedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = "One", NormalizedName = "ONE", CreatedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(ProjectDocumentRow(ProjectId = project, DocumentId = document, Role = "FocusedSource", AddedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
    let key = { Project = project; Document = document }
    let saved =
        { DocumentId = document; Points = [{ Text = "Stored"; Pages = [1] }]; PagesCovered = [1]
          UnreadablePages = []; OmittedPoints = 0; GeneratedAt = now; ServiceHost = "local"; Model = "fixture" }
    SummaryRepository.save host.Root key saved
    let jobs = (host.App :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
    let entered = TaskCompletionSource<JobHandle>(TaskCreationOptions.RunContinuationsAsynchronously)
    let finish = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let run handle = task { entered.SetResult(handle); do! finish.Task }
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, run, regenerate = true))
    let! handle = entered.Task.WaitAsync(TimeSpan.FromSeconds(10.))
    try
        File.Delete(path)
        use! response = export host
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)
        Assert.False(handle.Token.IsCancellationRequested)
        Assert.Equal(Some saved, SummaryRepository.find host.Root key)
        Assert.Single(LibraryRepository.list host.Root) |> ignore
        use db = WorkspaceDb.create host.Root
        let association = db.Set<ProjectDocumentRow>() |> Seq.exactlyOne
        Assert.Equal("FocusedSource", association.Role)
    finally
        finish.TrySetResult() |> ignore
    do! SummaryJobsAccess.settled host.App project document |> fun work -> work.WaitAsync(TimeSpan.FromSeconds(10.))
}
