module LexScholar.Server.Tests.RegenerationReviewTests

open System
open System.Net
open System.Threading.Tasks
open System.Text.Json
open Xunit
open LexScholar.Domain
open LexScholar.Infrastructure
open ChatGptContractTests

let private seedSaved (harness: TestHarness) path =
    let parts = (path: string).Split('/')
    let key = { Project = Guid.Parse parts[3]; Document = Guid.Parse parts[5] }
    let summary : StoredSummary =
        { DocumentId = key.Document; Points = [{ Text = "Original"; Pages = [1] }]
          PagesCovered = [1]; UnreadablePages = []; OmittedPoints = 0
          GeneratedAt = DateTimeOffset.UtcNow; ServiceHost = "old"; Model = "old" }
    SummaryRepository.save harness.Root key summary
    key, SummaryRepository.find harness.Root key

let private wait (work: Task) = work.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()
let private status text =
    use json = JsonDocument.Parse(text: string)
    json.RootElement.GetProperty("status").GetString()

[<Theory; Trait("Task", "T024")>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``legacy GET list and ordinary POST never expose replacement stop or close states`` fail =
    use fake = new FakeChatService(if fail then Status 500 else WaitUntilCancelled)
    use harness = TestHarness.Start(summaryHandler = fake, settings = [
        "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ])
    let path = documentPath harness
    let key, saved = seedSaved harness path
    let started, _ = sendJson harness (path + "/regenerate") "{}"
    Assert.Equal(HttpStatusCode.Accepted, started)
    if fail then wait (SummaryJobsAccess.settled harness.App key.Project key.Document) else wait fake.Waiting
    let get = harness.Client.GetStringAsync(path).GetAwaiter().GetResult()
    Assert.Equal("finished", status get)
    let replacement = harness.Client.GetStringAsync(path + "/regenerate").GetAwaiter().GetResult()
    Assert.Equal((if fail then "failed" else "running"), status replacement)
    // Explicitly name unavailable ChatGPT: a stored summary must bypass destination/auth checks.
    let ordinaryStatus, ordinary = sendJson harness path """{"destination":"chatgpt"}"""
    Assert.Equal(HttpStatusCode.OK, ordinaryStatus)
    Assert.Equal("finished", status ordinary)
    let list = harness.Client.GetStringAsync("/api/summaries").GetAwaiter().GetResult()
    use json = JsonDocument.Parse list
    Assert.Equal("finished", json.RootElement.[0].GetProperty("status").GetString())
    Assert.Equal(saved, SummaryRepository.find harness.Root key)

[<Theory; Trait("Task", "T024")>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``ordinary POST after failed replacement bypasses signed-out or remote-consent checks`` remote =
    let endpoint = if remote then "https://summary.example.test/v1" else "http://127.0.0.1:9/v1"
    use fake = new FakeChatService(Status 500)
    use harness = TestHarness.Start(summaryHandler = fake, settings = [
        "LexScholar:Summary:Endpoint", endpoint; "LexScholar:Summary:Model", "m" ])
    let path = documentPath harness
    let key, saved = seedSaved harness path
    let confirmation =
        if remote then """{"confirmSend":true,"confirmedHost":"summary.example.test","confirmedModel":"m"}"""
        else "{}"
    let started, _ = sendJson harness (path + "/regenerate") confirmation
    Assert.Equal(HttpStatusCode.Accepted, started)
    wait (SummaryJobsAccess.settled harness.App key.Project key.Document)
    let body = if remote then "{}" else """{"destination":"chatgpt"}"""
    let response, content = sendJson harness path body
    Assert.Equal(HttpStatusCode.OK, response)
    Assert.Equal("finished", status content)
    Assert.Contains("Original", content)
    Assert.Equal(1, fake.Requests.Length)
    Assert.Equal(saved, SummaryRepository.find harness.Root key)

[<Fact; Trait("Task", "T024")>]
let ``cancel while extracting invalidates admission even when extraction ignores cancellation`` () =
    let entered = TaskCompletionSource()
    let release = TaskCompletionSource()
    let extraction : LexScholar.Server.SummaryEndpoints.PageExtraction =
        { Extract = fun _ _ -> task {
            entered.SetResult()
            do! release.Task
            return Ok [{ Number = 1; Text = String.replicate 100 "Readable text for summary. " }] } }
    use fake = new FakeChatService(Reply """{"points":[{"text":"Replacement","pages":[1]}]}""")
    use harness = TestHarness.Start(summaryHandler = fake, summaryExtraction = extraction, settings = [
        "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ])
    let path = documentPath harness
    let key, saved = seedSaved harness path
    let pending = Task.Run(fun () -> sendJson harness (path + "/regenerate") "{}")
    wait entered.Task
    let cancelled, _ = sendJson harness (path + "/cancel") "{}"
    Assert.Equal(HttpStatusCode.NoContent, cancelled)
    release.SetResult()
    let response, _ = pending.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.Conflict, response)
    wait (SummaryJobsAccess.settled harness.App key.Project key.Document)
    Assert.Empty(fake.Requests)
    Assert.Equal(saved, SummaryRepository.find harness.Root key)

[<Fact; Trait("Task", "T024")>]
let ``cancel during held plan refresh invalidates pending replacement before it registers`` () =
    use fake = new FakeOpenAi(ResponsesScript.Completed """{"points":[{"text":"Replacement","pages":[1]}]}""")
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    let key, saved = seedSaved harness path
    signInExpiring harness fake
    let release = fake.HoldTokens()
    let pending = Task.Run(fun () -> sendJson harness (path + "/regenerate") """{"destination":"chatgpt","confirmSend":true}""")
    wait fake.TokenArrived
    let cancelled, _ = sendJson harness (path + "/cancel") "{}"
    Assert.Equal(HttpStatusCode.NoContent, cancelled)
    release()
    let response, _ = pending.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.Conflict, response)
    wait (SummaryJobsAccess.settled harness.App key.Project key.Document)
    Assert.Empty(openAiRequests fake)
    Assert.Equal(saved, SummaryRepository.find harness.Root key)
