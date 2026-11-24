module LexScholar.Server.Tests.LibraryCoordinationContractTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open LexScholar.Application
open LexScholar.Infrastructure
open LexScholar.Domain
open LexScholar.Server
open Xunit

[<Fact; Trait("Task", "T003")>]
let ``host import shares the file lifetime boundary`` () = task {
    use host = TestHarness.Start()
    let coordination = host.App.Services.GetRequiredService<LibraryCoordination>()
    use! lease = coordination.FileLease(CancellationToken.None)
    let waiting = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    use subscription = coordination.FileWait.Subscribe(fun () -> waiting.TrySetResult() |> ignore)
    use tokenResponse = host.Client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    use tokenJson = JsonDocument.Parse(tokenResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    let token = tokenJson.RootElement.GetProperty("requestToken").GetString()
    use form = new MultipartFormDataContent()
    let bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
    form.Add(new ByteArrayContent(bytes), "file", "valid.pdf")
    use request = new HttpRequestMessage(HttpMethod.Post, "/api/documents")
    request.Headers.TryAddWithoutValidation("Origin", host.Client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token) |> ignore
    request.Content <- form
    let upload = host.Client.SendAsync(request)
    let! first = Task.WhenAny(waiting.Task, upload :> Task).WaitAsync(TimeSpan.FromSeconds 30.)
    Assert.Same(waiting.Task, first)
    do! waiting.Task.WaitAsync(TimeSpan.FromSeconds 30.)
    Assert.Empty(LibraryRepository.list host.Root)
    Assert.False(upload.IsCompleted)
    lease.Dispose()
    use! response = upload
    Assert.Equal(HttpStatusCode.Created, response.StatusCode)
    Assert.Single(LibraryRepository.list host.Root) |> ignore
}

[<Fact; Trait("Task", "T003")>]
let ``registry database completion proceeds while simulated deletion waits for files`` () = task {
    use host = TestHarness.Start()
    let coordination = host.App.Services.GetRequiredService<LibraryCoordination>()
    let workspace, project, document = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    let now = DateTimeOffset.UtcNow
    do
        use db = WorkspaceDb.create host.Root
        db.Add(WorkspaceRow(Id = workspace, CreatedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = "One", NormalizedName = "ONE", CreatedUtc = now)) |> ignore
        db.Add(LibraryDocumentRow(Id = document, Sha256 = "hash", StorageKey = "key", OriginalFileName = "one.pdf", ByteLength = 1L, PageCount = 1, ImportedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(ProjectDocumentRow(ProjectId = project, DocumentId = document, Role = "FocusedSource", AddedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
    let key = { Project = project; Document = document }
    let handle = TaskCompletionSource<JobHandle>(TaskCreationOptions.RunContinuationsAsynchronously)
    let done' = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let jobs =
        (host.App :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
    use connection = new SqliteConnection($"Data Source={host.DatabasePath}")
    connection.Open()
    use command = connection.CreateCommand()
    command.CommandText <- "PRAGMA journal_mode"
    Assert.Equal("wal", string (command.ExecuteScalar()))
    jobs.Locked(fun () ->
        Assert.True(coordination.IsMutationHeld)
        command.CommandText <- "SELECT COUNT(*) FROM ProjectDocuments"
        Assert.Equal(1L, unbox<int64> (command.ExecuteScalar())))
    let run given = task { handle.SetResult(given); do! done'.Task }
    use! exportLease = coordination.FileLease(CancellationToken.None)
    let deleting = coordination.FileLease(CancellationToken.None)
    Assert.False(deleting.IsCompleted)
    let! admission = Task.Run(fun () -> jobs.Start(key, CancellationToken.None, run)).WaitAsync(TimeSpan.FromSeconds 30.)
    Assert.Equal(Started, admission)
    let! job = handle.Task.WaitAsync(TimeSpan.FromSeconds 30.)
    let summary =
        { DocumentId = document; Points = []; PagesCovered = []; UnreadablePages = []
          OmittedPoints = 0; GeneratedAt = now; ServiceHost = "local"; Model = "m" }
    let! completed = Task.Run(fun () -> jobs.Complete(key, job.Id, summary)).WaitAsync(TimeSpan.FromSeconds 30.)
    Assert.True(completed)
    Assert.True((SummaryRepository.find host.Root key).IsSome)
    Assert.False(deleting.IsCompleted)
    exportLease.Dispose()
    use! deletionLease = deleting
    let result =
        coordination.Mutation(fun () ->
            jobs.RemoveAssociation(key, fun () -> LibraryRepository.removeAssociation host.Root project document))
    Assert.True(Result.isOk result)
    Assert.True((SummaryRepository.find host.Root key).IsNone)
    Assert.Single(LibraryRepository.list host.Root) |> ignore
    done'.SetResult()
}

[<Fact; Trait("Task", "T003")>]
let ``host registry progresses while a real PDF is open for extraction`` () = task {
    let entered = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let release = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable root = ""
    let extraction : SummaryEndpoints.PageExtraction =
        { Extract = fun document cancellation ->
            PdfText.extractPagesWith (fun stream -> task {
                Assert.Equal(int '%', stream.ReadByte())
                stream.Position <- 0L
                entered.TrySetResult() |> ignore
                do! release.Task }) root document cancellation }
    use fake = new FakeChatService(Reply """{"points":[{"text":"Main idea","pages":[1]}]}""")
    use host = TestHarness.Start(summaryHandler = fake, summaryExtraction = extraction, settings = [
        "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"
        "LexScholar:Summary:Model", "m" ])
    root <- host.Root
    let workspace, project, other = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    let now = DateTimeOffset.UtcNow
    use tokenResponse = host.Client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    use tokenJson = JsonDocument.Parse(tokenResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    let token = tokenJson.RootElement.GetProperty("requestToken").GetString()
    let request (method': HttpMethod) (path: string) =
        let message = new HttpRequestMessage(method', path)
        message.Headers.TryAddWithoutValidation("Origin", host.Client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
        message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token) |> ignore
        message
    use form = new MultipartFormDataContent()
    form.Add(new ByteArrayContent(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", "article-en.pdf"))), "file", "article-en.pdf")
    use uploadRequest = request HttpMethod.Post "/api/documents"
    uploadRequest.Content <- form
    use! upload = host.Client.SendAsync(uploadRequest)
    Assert.Equal(HttpStatusCode.Created, upload.StatusCode)
    use! body = upload.Content.ReadAsStreamAsync()
    use json = JsonDocument.Parse(body)
    let document = json.RootElement.GetProperty("id").GetGuid()
    do
        use db = WorkspaceDb.create host.Root
        db.Add(WorkspaceRow(Id = workspace, CreatedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = "One", NormalizedName = "ONE", CreatedUtc = now)) |> ignore
        db.Add(ResearchProjectRow(Id = other, WorkspaceId = workspace, Name = "Two", NormalizedName = "TWO", CreatedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(ProjectDocumentRow(ProjectId = project, DocumentId = document, Role = "FocusedSource", AddedUtc = now)) |> ignore
        db.Add(ProjectDocumentRow(ProjectId = other, DocumentId = document, Role = "FocusedSource", AddedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
    let coordination = host.App.Services.GetRequiredService<LibraryCoordination>()
    let jobs = (host.App :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
    use! exportLease = coordination.FileLease(CancellationToken.None)
    let deleting = coordination.FileLease(CancellationToken.None)
    use summaryRequest = request HttpMethod.Post $"/api/projects/{project}/documents/{document}/summary"
    summaryRequest.Content <- new StringContent("""{"destination":"configured"}""", Encoding.UTF8, "application/json")
    let pending = host.Client.SendAsync(summaryRequest)
    try
        let! first = Task.WhenAny(entered.Task, pending :> Task).WaitAsync(TimeSpan.FromSeconds 30.)
        Assert.Same(entered.Task, first)
        Assert.False(deleting.IsCompleted)
        let key = { Project = other; Document = document }
        let handle = TaskCompletionSource<JobHandle>(TaskCreationOptions.RunContinuationsAsynchronously)
        let finish = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        let run given = task { handle.SetResult(given); do! finish.Task }
        let! admitted = Task.Run(fun () -> jobs.Start(key, CancellationToken.None, run)).WaitAsync(TimeSpan.FromSeconds 30.)
        Assert.Equal(Started, admitted)
        let! job = handle.Task.WaitAsync(TimeSpan.FromSeconds 30.)
        let summary =
            { DocumentId = document; Points = []; PagesCovered = []; UnreadablePages = []
              OmittedPoints = 0; GeneratedAt = now; ServiceHost = "local"; Model = "m" }
        let! completed = Task.Run(fun () -> jobs.Complete(key, job.Id, summary)).WaitAsync(TimeSpan.FromSeconds 30.)
        Assert.True(completed)
        finish.SetResult()
        Assert.True((SummaryRepository.find host.Root key).IsSome)
        exportLease.Dispose()
        use! deletionLease = deleting.WaitAsync(TimeSpan.FromSeconds 30.)
        let! removed =
            Task.Run(fun () ->
                coordination.Mutation(fun () ->
                    jobs.RemoveAssociation(key, fun () -> LibraryRepository.removeAssociation host.Root other document)))
                .WaitAsync(TimeSpan.FromSeconds 30.)
        Assert.True(Result.isOk removed)
    finally
        release.TrySetResult() |> ignore
    use! response = pending.WaitAsync(TimeSpan.FromSeconds 30.)
    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode)
    do! SummaryJobsAccess.settled host.App project document |> fun work -> work.WaitAsync(TimeSpan.FromSeconds 30.)
    Assert.True((SummaryRepository.find host.Root { Project = project; Document = document }).IsSome)
}
