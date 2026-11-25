module LexScholar.Server.Tests.RegenerationReviewTests

open System
open System.Net
open System.Threading.Tasks
open System.Text.Json
open Xunit
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Application
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
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
let ``every configured replacement requires a fresh boolean confirmation but ordinary local sends do not`` remote =
    let endpoint = if remote then "https://summary.example.test/v1" else "http://127.0.0.1:9/v1"
    let host = if remote then "summary.example.test" else "127.0.0.1:9"
    use fake = new FakeChatService(WaitUntilCancelled)
    use harness = TestHarness.Start(summaryHandler = fake, settings = [
        "LexScholar:Summary:Endpoint", endpoint; "LexScholar:Summary:Model", "m" ])
    let path = documentPath harness
    let key, saved = seedSaved harness path
    for flag in [ ""; ",\"confirmSend\":false"; ",\"confirmSend\":\"true\""; ",\"confirmSend\":1" ] do
        let response, content = sendJson harness (path + "/regenerate") ("{\"destination\":\"configured\"" + flag + "}")
        Assert.Equal(HttpStatusCode.Conflict, response)
        Assert.Contains("confirmation_required", content)
        Assert.Contains(host, content)
        Assert.Empty(fake.Requests)
        Assert.Equal(saved, SummaryRepository.find harness.Root key)
    if remote then
        let response, _ = sendJson harness (path + "/regenerate") """{"confirmSend":true,"confirmedHost":"wrong","confirmedModel":"m"}"""
        Assert.Equal(HttpStatusCode.Conflict, response)
        Assert.Empty(fake.Requests)
    let confirmed =
        if remote then """{"confirmSend":true,"confirmedHost":"summary.example.test","confirmedModel":"m"}"""
        else """{"confirmSend":true}"""
    let started, _ = sendJson harness (path + "/regenerate") confirmed
    Assert.Equal(HttpStatusCode.Accepted, started)
    wait fake.Waiting
    let repeated, content = sendJson harness (path + "/regenerate") "{}"
    Assert.Equal(HttpStatusCode.OK, repeated)
    Assert.Equal("running", status content)
    Assert.Single(fake.Requests) |> ignore
    Assert.Equal(saved, SummaryRepository.find harness.Root key)
    sendJson harness (path + "/cancel") "{}" |> ignore
    if not remote then
        // A separate unsaved pair still follows the ordinary, no-consent local path.
        SummaryRepository.delete harness.Root key
        let ordinary, _ = sendJson harness path "{}"
        Assert.Equal(HttpStatusCode.Accepted, ordinary)

[<Fact; Trait("Task", "T024")>]
let ``an already confirmed plan still requires fresh consent for each replacement`` () =
    use fake = new FakeOpenAi(ResponsesScript.Completed """{"points":[{"text":"Generated","pages":[1]}]}""")
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInThrough harness fake "" |> ignore
    let parts = path.Split('/')
    let project, document = Guid.Parse parts[3], Guid.Parse parts[5]
    let initial, _ = sendJson harness path """{"destination":"chatgpt","confirmSend":true}"""
    Assert.Equal(HttpStatusCode.Accepted, initial)
    wait (SummaryJobsAccess.settled harness.App project document)
    let key, saved = seedSaved harness path
    let before = fake.Requests.Length
    for flag in [ ""; ",\"confirmSend\":false"; ",\"confirmSend\":\"true\""; ",\"confirmSend\":1" ] do
        let response, content = sendJson harness (path + "/regenerate") ("{\"destination\":\"chatgpt\"" + flag + "}")
        Assert.Equal(HttpStatusCode.Conflict, response)
        Assert.Contains("confirmation_required", content)
        Assert.Contains("\"account\"", content)
        Assert.Equal(before, fake.Requests.Length)
        Assert.Equal(saved, SummaryRepository.find harness.Root key)
    let accepted, _ = sendJson harness (path + "/regenerate") """{"destination":"chatgpt","confirmSend":true}"""
    Assert.Equal(HttpStatusCode.Accepted, accepted)
    wait (SummaryJobsAccess.settled harness.App project document)
    Assert.Equal("Generated", (SummaryRepository.find harness.Root key).Value.Points.Head.Text)
    // Completion does not carry consent forward to a later replacement.
    let next, _ = sendJson harness (path + "/regenerate") """{"destination":"chatgpt"}"""
    Assert.Equal(HttpStatusCode.Conflict, next)

[<Theory; Trait("Task", "T024")>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``ordinary Started response cannot expose a replacement registered before response construction`` fail =
    let key : SummaryKey = { Project = Guid.NewGuid(); Document = Guid.NewGuid() }
    let mutable saved = None
    let jobs = SummaryJobs {
        Find = fun _ -> saved
        Save = fun _ summary -> saved <- Some summary
        Delete = fun _ -> saved <- None
        AssociationExists = fun _ -> true }
    let summary : StoredSummary =
        { DocumentId = key.Document; Points = [{ Text = "Original"; Pages = [1] }]
          PagesCovered = [1]; UnreadablePages = []; OmittedPoints = 0
          GeneratedAt = DateTimeOffset.UtcNow; ServiceHost = "old"; Model = "old" }
    let handle = TaskCompletionSource<JobHandle>()
    let finish = TaskCompletionSource()
    let run given = task { handle.SetResult given; do! finish.Task }
    // Suspend the caller after registration by retaining its outcome, before invoking the real response helper.
    let ordinaryOutcome = jobs.Start(key, Threading.CancellationToken.None, run)
    Assert.Equal(Started, ordinaryOutcome)
    let ordinary = handle.Task.GetAwaiter().GetResult()
    Assert.True(jobs.Complete(key, ordinary.Id, summary))
    finish.SetResult()
    let replacementHandle = TaskCompletionSource<JobHandle>()
    let replacementFinish = TaskCompletionSource()
    let replacementRun given = task { replacementHandle.SetResult given; do! replacementFinish.Task }
    Assert.Equal(Started, jobs.Start(key, Threading.CancellationToken.None, replacementRun, regenerate = true))
    let replacement = replacementHandle.Task.GetAwaiter().GetResult()
    if fail then Assert.True(jobs.Fail(key, replacement.Id, "service_failed", "failed replacement", None))
    try
        // The same production helper must keep the two response audiences separate in either interleaving.
        let response = LexScholar.Server.SummaryEndpoints.startAnswer false jobs key ordinaryOutcome
        let statusResult = response :?> Microsoft.AspNetCore.Http.IStatusCodeHttpResult
        Assert.Equal(Nullable 202, statusResult.StatusCode)
        let json = JsonSerializer.Serialize((response :?> Microsoft.AspNetCore.Http.IValueHttpResult).Value)
        Assert.Equal("finished", status json)
        Assert.Contains("Original", json)
        let awareResponse = LexScholar.Server.SummaryEndpoints.startAnswer true jobs key Started
        let awareJson = JsonSerializer.Serialize((awareResponse :?> Microsoft.AspNetCore.Http.IValueHttpResult).Value)
        Assert.Equal((if fail then "failed" else "running"), status awareJson)
        Assert.Equal(Some summary, saved)
    finally
        jobs.Cancel key
        replacementFinish.SetResult()

[<Theory; Trait("Task", "T024")>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``legacy GET list and ordinary POST never expose replacement stop or close states`` fail =
    use fake = new FakeChatService(if fail then Status 500 else WaitUntilCancelled)
    use harness = TestHarness.Start(summaryHandler = fake, settings = [
        "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ])
    let path = documentPath harness
    let key, saved = seedSaved harness path
    let started, _ = sendJson harness (path + "/regenerate") """{"confirmSend":true}"""
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
        else """{"confirmSend":true}"""
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
    let pending = Task.Run(fun () -> sendJson harness (path + "/regenerate") """{"confirmSend":true}""")
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

[<Theory; Trait("Task", "T024")>]
[<InlineData("configured", false)>]
[<InlineData("configured", true)>]
[<InlineData("chatgpt", false)>]
[<InlineData("chatgpt", true)>]
let ``replacement forwards the instruction without retaining its raw input or service error echoes`` destination fail =
    let secret = "PRIVATE-REPLACEMENT-INSTRUCTION-T024"
    let reply = """{"points":[{"text":"Replacement","pages":[1]}]}"""
    use configured = new FakeChatService(if fail then StatusWithBody(400, JsonSerializer.Serialize({| error = {| message = secret |} |})) else Reply reply)
    use plan = new FakeOpenAi(if fail then ResponsesScript.Status(400, "unknown_error") else ResponsesScript.Completed reply)
    plan.SummaryErrorMessage <- secret
    let handler : System.Net.Http.HttpMessageHandler = if destination = "configured" then configured else plan
    let lines = new CapturingProvider()
    use harness = TestHarness.Start(
        summaryHandler = handler,
        settings = [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ],
        configure = fun host -> host.Services.GetRequiredService<ILoggerFactory>().AddProvider lines)
    let path = documentPath harness
    let key, original = seedSaved harness path
    if destination = "chatgpt" then signInThrough harness plan "" |> ignore
    let started, response = sendJson harness (path + "/regenerate") (JsonSerializer.Serialize({| destination = destination; confirmSend = true; instruction = secret |}))
    Assert.Equal(HttpStatusCode.Accepted, started)
    Assert.DoesNotContain(secret, response)
    wait (SummaryJobsAccess.settled harness.App key.Project key.Document)
    let outgoing =
        if destination = "configured" then Assert.Single(configured.Requests).Body
        else Assert.Single(plan.RequestsTo("api.openai.com", "/v1/responses")).Body
    Assert.Contains(secret, outgoing)
    let replacement = harness.Client.GetStringAsync(path + "/regenerate").GetAwaiter().GetResult()
    Assert.Equal((if fail then "failed" else "finished"), status replacement)
    Assert.DoesNotContain(secret, replacement)
    let ordinary = harness.Client.GetStringAsync(path).GetAwaiter().GetResult()
    Assert.Equal("finished", status ordinary)
    Assert.DoesNotContain(secret, ordinary)
    if fail then Assert.Equal(original, SummaryRepository.find harness.Root key)
    else Assert.Equal("Replacement", (SummaryRepository.find harness.Root key).Value.Points.Head.Text)
    Assert.DoesNotContain(secret, String.Join('\n', lines.Lines))
    for file in IO.Directory.EnumerateFiles(harness.Root, "*", IO.SearchOption.AllDirectories) do
        if IO.Path.GetFileName(file) <> ".lexscholar.lock" then
            use stream = new IO.FileStream(file, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite ||| IO.FileShare.Delete)
            use memory = new IO.MemoryStream()
            stream.CopyTo memory
            Assert.DoesNotContain(secret, Text.Encoding.Latin1.GetString(memory.ToArray()))

[<Fact; Trait("Task", "T024")>]
let ``invalid replacement instruction is rejected without changing the saved version`` () =
    use fake = new FakeChatService(Status 500)
    use harness = TestHarness.Start(summaryHandler = fake, settings = [
        "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ])
    let path = documentPath harness
    let key, original = seedSaved harness path
    let response, content = sendJson harness (path + "/regenerate") (JsonSerializer.Serialize({| instruction = String('x', 2001) |}))
    Assert.Equal(HttpStatusCode.BadRequest, response)
    Assert.Contains("invalid_instruction", content)
    Assert.Empty(fake.Requests)
    Assert.Equal(original, SummaryRepository.find harness.Root key)
