module LexScholar.Server.Tests.LibraryDeletionSummaryRaceTests

open System
open System.Net
open System.Text.Json
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Server
open Microsoft.AspNetCore.Builder
open Microsoft.EntityFrameworkCore
open Xunit
open ChatGptContractTests

let private wait (work: Task) = work.WaitAsync(TimeSpan.FromSeconds 20.).GetAwaiter().GetResult()
let private result (work: Task<'a>) = work.WaitAsync(TimeSpan.FromSeconds 20.).GetAwaiter().GetResult()
let private signal () = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
let private configure (app: WebApplication) =
    LibraryDeletionEndpoints.mapForTests app app.Configuration["LexScholar:DataDirectory"]
let private settings =
    [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m"
      "LexScholar:Summary:MaxChunkCharacters", "4000" ]
let private keyOf (path: string) : SummaryKey =
    let parts = path.Split('/')
    { Project = Guid.Parse parts[3]; Document = Guid.Parse parts[5] }
let private saved (h: TestHarness) key =
    let summary : StoredSummary =
        { DocumentId = key.Document; Points = [{ Text = "Old version"; Pages = [1] }]
          PagesCovered = [1]; UnreadablePages = []; OmittedPoints = 0
          GeneratedAt = DateTimeOffset.UtcNow; ServiceHost = "old"; Model = "old" }
    SummaryRepository.save h.Root key summary
    Some summary
let private registry (h: TestHarness) =
    (h.App :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
let private preview (h: TestHarness) document =
    let status, body = sendJson h "/api/library/deletions/preview" (JsonSerializer.Serialize {| documentIds = [document] |})
    Assert.Equal(HttpStatusCode.OK, status)
    use json = JsonDocument.Parse body
    json.RootElement.GetProperty("revision").GetString()
let private confirm h document revision =
    sendJson h "/api/library/deletions"
        (JsonSerializer.Serialize {| operationId = Guid.NewGuid(); documentIds = [document]; revision = revision |})
let private admit h document =
    let status, _ = confirm h document (preview h document)
    Assert.Equal(HttpStatusCode.Accepted, status)

[<Theory; Trait("Task", "T011")>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``intent fences extraction preparations even when extractor ignores cancellation`` replacement =
    let entered, release = signal (), signal ()
    let mutable extractionToken = Threading.CancellationToken.None
    let extraction : SummaryEndpoints.PageExtraction =
        { Extract = fun _ token -> task {
            extractionToken <- token
            entered.TrySetResult() |> ignore
            do! release.Task.WaitAsync(TimeSpan.FromSeconds 20.)
            return Ok [{ Number = 1; Text = String.replicate 100 "source text " }] } }
    use fake = new FakeChatService(Reply """{"points":[{"text":"New","pages":[1]}]}""")
    use h = TestHarness.Start(settings = settings, configure = configure, summaryHandler = fake, summaryExtraction = extraction)
    let path = documentPath h
    let key = keyOf path
    let before = if replacement then saved h key else None
    let pending = Task.Run(fun () -> sendJson h (path + if replacement then "/regenerate" else "") """{"confirmSend":true}""")
    try
        wait entered.Task
        admit h key.Document
        Assert.True(extractionToken.IsCancellationRequested)
    finally release.TrySetResult() |> ignore
    let status, _ = result pending
    Assert.False(status = HttpStatusCode.Accepted || status = HttpStatusCode.OK)
    Assert.Empty fake.Requests
    Assert.Equal(before, SummaryRepository.find h.Root key)
    Assert.Equal(before |> Option.map JobState.Finished, (registry h).State key)
    // New preparations after intent are fenced too.
    let status, _ = sendJson h (path + "/regenerate") """{"confirmSend":true}"""
    Assert.NotEqual(HttpStatusCode.Accepted, status)
    Assert.Empty fake.Requests

[<Theory; Trait("Task", "T011")>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``intent cancels running jobs prevents later chunks and leaves unrelated jobs alone`` replacement =
    use fake = new FakeChatService(Hold """{"points":[{"text":"New","pages":[1]}]}""")
    use h = TestHarness.Start(settings = settings, configure = configure, summaryHandler = fake)
    let path = documentPathFor h "long-40.pdf"
    let key = keyOf path
    let before = if replacement then saved h key else None
    let otherPath = documentPathFor h "article-pt.pdf"
    let other = keyOf otherPath
    let status, _ = sendJson h (path + if replacement then "/regenerate" else "") """{"confirmSend":true}"""
    Assert.Equal(HttpStatusCode.Accepted, status)
    wait fake.Waiting
    let settled = (registry h).Settled key
    let unrelatedEntered, unrelatedRelease = signal (), signal ()
    Assert.Equal(Started, (registry h).Start(other, Threading.CancellationToken.None, fun job -> task {
        unrelatedEntered.SetResult()
        do! unrelatedRelease.Task.WaitAsync(TimeSpan.FromSeconds 20.)
        job.Token.ThrowIfCancellationRequested() }))
    let unrelatedSettled = (registry h).Settled other
    try
        wait unrelatedEntered.Task
        admit h key.Document
        Assert.False(unrelatedSettled.IsCompleted)
        // Assertion before waiting makes a missing cancellation a meaningful RED, not a timeout.
        Assert.Equal(before |> Option.map JobState.Finished, (registry h).State key)
        wait fake.Cancelled
        wait settled
        Assert.Single fake.Requests |> ignore
        Assert.Equal(before, SummaryRepository.find h.Root key)
    finally
        unrelatedRelease.TrySetResult() |> ignore
        fake.Release()
    wait unrelatedSettled

[<Fact; Trait("Task", "T011")>]
let ``completion winning before intent refreshes impact and refused confirmation does not cancel`` () =
    use fake = new FakeChatService(Hold """{"points":[{"text":"New","pages":[1]}]}""")
    use h = TestHarness.Start(settings = settings, configure = configure, summaryHandler = fake)
    let path = documentPath h
    let key = keyOf path
    let before = preview h key.Document
    let status, _ = sendJson h path "{}"
    Assert.Equal(HttpStatusCode.Accepted, status)
    wait fake.Waiting
    let settled = (registry h).Settled key
    fake.Release()
    wait settled
    Assert.True((SummaryRepository.find h.Root key).IsSome)
    let status, body = confirm h key.Document before
    Assert.Equal(HttpStatusCode.Conflict, status)
    Assert.Equal("impact_changed", problemCode body)
    let completed = SummaryRepository.find h.Root key
    admit h key.Document
    Assert.Equal(completed, SummaryRepository.find h.Root key)

[<Theory; Trait("Task", "T011")>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``intent during credential refresh prevents model and summary sends`` replacement =
    use fake = new FakeOpenAi(ResponsesScript.Completed """{"points":[{"text":"New","pages":[1]}]}""")
    use h = TestHarness.Start(configure = configure, summaryHandler = fake)
    let path = documentPath h
    let key = keyOf path
    let before = if replacement then saved h key else None
    fake.ExpiresIn <- 1
    signInThrough h fake "" |> ignore
    let release = fake.HoldTokens()
    let pending = Task.Run(fun () -> sendJson h (path + if replacement then "/regenerate" else "") """{"destination":"chatgpt","confirmSend":true}""")
    try
        wait fake.TokenArrived
        admit h key.Document
    finally release ()
    let status, _ = result pending
    Assert.False(status = HttpStatusCode.Accepted || status = HttpStatusCode.OK)
    Assert.Equal(before, SummaryRepository.find h.Root key)
    Assert.DoesNotContain(fake.Requests, fun request -> request.Host = "api.openai.com")

[<Theory; Trait("Task", "T011")>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``intent cancels a running plan job without saving or sending again`` replacement =
    use fake = new FakeOpenAi(ResponsesScript.WaitUntilCancelled)
    use h = TestHarness.Start(configure = configure, summaryHandler = fake)
    let path = documentPathFor h "long-40.pdf"
    let key = keyOf path
    let before = if replacement then saved h key else None
    signInThrough h fake "" |> ignore
    let status, _ =
        sendJson h (path + if replacement then "/regenerate" else "")
            """{"destination":"chatgpt","confirmSend":true}"""
    Assert.Equal(HttpStatusCode.Accepted, status)
    wait fake.Waiting
    let settled = (registry h).Settled key
    admit h key.Document
    Assert.Equal(before |> Option.map JobState.Finished, (registry h).State key)
    wait fake.Cancelled
    wait settled
    Assert.Equal(before, SummaryRepository.find h.Root key)
    Assert.Single(fake.Requests |> List.filter (fun request -> request.Path = "/v1/responses")) |> ignore

[<Fact; Trait("Task", "T011")>]
let ``durable intent fences every project and late callbacks cannot change state or save`` () =
    use h = TestHarness.Start(configure = configure)
    let first = documentPath h |> keyOf
    let second = documentPath h |> keyOf
    Assert.Equal(first.Document, second.Document)
    Assert.NotEqual(first.Project, second.Project)
    let jobs = registry h
    let handles =
        [first; second] |> List.map (fun key ->
            let handle = TaskCompletionSource<JobHandle>(TaskCreationOptions.RunContinuationsAsynchronously)
            let release = signal ()
            Assert.Equal(Started, jobs.Start(key, Threading.CancellationToken.None, fun job -> task {
                handle.SetResult job
                do! release.Task.WaitAsync(TimeSpan.FromSeconds 20.) }))
            key, result handle.Task, release, jobs.Settled key)
    try
        admit h first.Document
        for key, handle, _, _ in handles do
            Assert.True(handle.Token.IsCancellationRequested)
            let summary : StoredSummary =
                { DocumentId = key.Document; Points = [{ Text = "Late"; Pages = [1] }]
                  PagesCovered = [1]; UnreadablePages = []; OmittedPoints = 0
                  GeneratedAt = DateTimeOffset.UtcNow; ServiceHost = "late"; Model = "late" }
            // Simulate a provider/worker ignoring cancellation and completing anyway.
            Assert.False(jobs.Complete(key, handle.Id, summary))
            Assert.False(jobs.Fail(key, handle.Id, "service_failed", "late", None))
            Assert.False(jobs.Progress(key, handle.Id, Progress.Combining))
            Assert.Equal(None, jobs.State key)
            Assert.Equal(None, SummaryRepository.find h.Root key)
            let token, lease = jobs.Prepare key
            use _ = lease
            Assert.True(token.IsCancellationRequested)
    finally
        for _, _, release, _ in handles do release.TrySetResult() |> ignore
    for _, _, _, settled in handles do wait settled

[<Theory; Trait("Task", "T011")>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``refused or rolled back intent does not cancel running jobs`` persistenceFailure =
    use fake = new FakeChatService(Hold """{"points":[{"text":"New","pages":[1]}]}""")
    use h = TestHarness.Start(settings = settings, configure = configure, summaryHandler = fake)
    let path = documentPath h
    let key = keyOf path
    let revision = preview h key.Document
    let status, _ = sendJson h path "{}"
    Assert.Equal(HttpStatusCode.Accepted, status)
    wait fake.Waiting
    let settled = (registry h).Settled key
    try
        if persistenceFailure then
            use db = WorkspaceDb.create h.Root
            db.Database.ExecuteSqlRaw("""
                CREATE TRIGGER FailDeletionIntent BEFORE INSERT ON DeletionItems
                BEGIN SELECT RAISE(ABORT, 'injected failure'); END;
                """) |> ignore
        let status, _ = confirm h key.Document (if persistenceFailure then revision else "stale")
        Assert.Equal((if persistenceFailure then HttpStatusCode.InternalServerError else HttpStatusCode.Conflict), status)
        Assert.False(fake.Cancelled.IsCompleted)
        Assert.True(match (registry h).State key with Some (JobState.Running _) -> true | _ -> false)
    finally fake.Release()
    wait settled
    Assert.True((SummaryRepository.find h.Root key).IsSome)

[<Fact; Trait("Task", "T011")>]
let ``preparation fencing preserves ordinary missing document and association responses`` () =
    use h = TestHarness.Start(settings = settings, configure = configure)
    let path = documentPath h
    let key = keyOf path
    let missingDocument, body =
        sendJson h $"/api/projects/{key.Project}/documents/{Guid.NewGuid()}/summary" "{}"
    Assert.Equal(HttpStatusCode.NotFound, missingDocument)
    Assert.Equal("document_not_found", problemCode body)
    let missingAssociation, body =
        sendJson h $"/api/projects/{Guid.NewGuid()}/documents/{key.Document}/summary" "{}"
    Assert.Equal(HttpStatusCode.NotFound, missingAssociation)
    Assert.Equal("not_found", problemCode body)
