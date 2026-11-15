/// HTTP contract tests for background summaries (F004), against contracts/summary-api.yaml.
module LexScholar.Server.Tests.BackgroundSummaryContractTests

open System
open System.Net
open System.Net.Http
open System.IO
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open LexScholar.Infrastructure
open Xunit

let private token (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    json.RootElement.GetProperty("requestToken").GetString()

let private send (client: HttpClient) (method: HttpMethod) (path: string) (csrf: string option) =
    use message = new HttpRequestMessage(method, path)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    csrf |> Option.iter (fun value -> message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", value) |> ignore)
    client.Send(message)

let private summaryPath () = $"/api/projects/{Guid.NewGuid():D}/documents/{Guid.NewGuid():D}/summary"

[<Fact; Trait("Task", "T001")>]
let ``the summary state and status routes exist`` () =
    use harness = TestHarness.Start()
    // The state route is live since T002: nothing is there for an unknown pair.
    use state = send harness.Client HttpMethod.Get (summaryPath ()) None
    Assert.Equal(HttpStatusCode.NotFound, state.StatusCode)
    use statuses = send harness.Client HttpMethod.Get "/api/summaries" None
    Assert.Equal(HttpStatusCode.NotImplemented, statuses.StatusCode)

[<Fact; Trait("Task", "T001")>]
let ``deleting a summary exists and needs the antiforgery token`` () =
    use harness = TestHarness.Start()
    let path = summaryPath ()
    use refused = send harness.Client HttpMethod.Delete path None
    Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode)
    use answered = send harness.Client HttpMethod.Delete path (Some (token harness.Client))
    // Live since T006: dismissing where nothing is kept is idempotent.
    Assert.Equal(HttpStatusCode.NoContent, answered.StatusCode)

let private settings = [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ]

let private seedProject directory =
    use db = WorkspaceDb.create directory
    let time = DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero)
    let workspace = Guid.NewGuid()
    let project = Guid.NewGuid()
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    db.Add(ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = "P", NormalizedName = "P", CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    project

let private sendWith (client: HttpClient) (method: HttpMethod) (path: string) (content: HttpContent option) =
    use message = new HttpRequestMessage(method, path)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token client) |> ignore
    content |> Option.iter (fun body -> message.Content <- body)
    let response = client.Send(message)
    response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

/// Imports a summary fixture and associates it with the project; returns its document id.
let private addDocument (client: HttpClient) (project: Guid) (name: string) =
    let part = new ByteArrayContent(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", name)))
    part.Headers.ContentType <- MediaTypeHeaderValue("application/pdf")
    let form = new MultipartFormDataContent()
    form.Add(part, "file", name)
    let status, text = sendWith client HttpMethod.Post "/api/documents" (Some (form :> HttpContent))
    Assert.True(status = HttpStatusCode.Created || status = HttpStatusCode.OK, text)
    let document = (use json = JsonDocument.Parse text in json.RootElement.GetProperty("id").GetGuid())
    let body = JsonSerializer.Serialize({| documentId = string document; role = "FocusedSource" |})
    let status, _ = sendWith client HttpMethod.Post $"/api/projects/{project}/documents" (Some (new StringContent(body, Encoding.UTF8, "application/json")))
    Assert.Equal(HttpStatusCode.Created, status)
    document

let private path (project: Guid) (document: Guid) = $"/api/projects/{project}/documents/{document}/summary"

let private ask (client: HttpClient) project document =
    sendWith client HttpMethod.Post (path project document) (Some (new StringContent("""{"confirmSend":false}""", Encoding.UTF8, "application/json")))

let private answer = """{"points":[{"text":"Main idea","pages":[1,2]}]}"""

[<Fact; Trait("Task", "T002")>]
let ``a configured-service summary answers 202 while the service is still working`` () =
    let fake = new FakeChatService(Hold answer)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    let status, text = ask harness.Client project document
    Assert.Equal(HttpStatusCode.Accepted, status)
    use json = JsonDocument.Parse text
    Assert.Equal("running", json.RootElement.GetProperty("status").GetString())
    fake.Waiting.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()
    let status, state = sendWith harness.Client HttpMethod.Get (path project document) None
    Assert.Equal(HttpStatusCode.OK, status)
    Assert.Contains("\"running\"", state)
    fake.Release()
    SummaryJobsAccess.settled harness.App project document |> fun task -> task.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()

[<Fact; Trait("Task", "T002")>]
let ``closing the POST's connection does not stop the job and the finished summary is found afterwards`` () =
    let fake = new FakeChatService(Hold answer)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    // The request is abandoned as soon as the headers arrive, as when the researcher leaves the document.
    use abandon = new Threading.CancellationTokenSource()
    use message = new HttpRequestMessage(HttpMethod.Post, path project document)
    message.Headers.TryAddWithoutValidation("Origin", harness.Client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token harness.Client) |> ignore
    message.Content <- new StringContent("""{"confirmSend":false}""", Encoding.UTF8, "application/json")
    let response = harness.Client.Send(message, HttpCompletionOption.ResponseHeadersRead, abandon.Token)
    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode)
    abandon.Cancel()
    response.Dispose()
    fake.Waiting.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()
    Assert.False(fake.Cancelled.IsCompleted, "the job's service call is not tied to the request")
    fake.Release()
    SummaryJobsAccess.settled harness.App project document |> fun task -> task.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()
    Assert.False(fake.Cancelled.IsCompleted)
    let status, text = sendWith harness.Client HttpMethod.Get (path project document) None
    Assert.Equal(HttpStatusCode.OK, status)
    use json = JsonDocument.Parse text
    Assert.Equal("finished", json.RootElement.GetProperty("status").GetString())
    let summary = json.RootElement.GetProperty("summary")
    Assert.Equal(document, summary.GetProperty("documentId").GetGuid())
    let point = summary.GetProperty("points").[0]
    Assert.Equal("Main idea", point.GetProperty("text").GetString())
    Assert.Equal<int list>([ 1; 2 ], [ for p in point.GetProperty("pages").EnumerateArray() -> p.GetInt32() ])
    Assert.Equal(1, fake.Requests.Length)

[<Fact; Trait("Task", "T002")>]
let ``a pair with no summary has no state`` () =
    let fake = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    let status, text = sendWith harness.Client HttpMethod.Get (path project document) None
    Assert.Equal(HttpStatusCode.NotFound, status)
    use json = JsonDocument.Parse text
    Assert.Equal("no_summary", json.RootElement.GetProperty("code").GetString())

/// The first call answers, the rest are held: the job has reported one finished chunk and is still running.
let private heldAfterFirst n = if n = 1 then Reply """{"points":[{"text":"Main idea","pages":[1]}]}""" else Hold """{"points":[{"text":"Main idea","pages":[1]}]}"""

[<Fact; Trait("Task", "T004")>]
let ``the state of a running multi-chunk summary reports the pages processed, then the summary`` () =
    let fake = new FakeChatService(heldAfterFirst)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "long-40.pdf"
    let status, _ = ask harness.Client project document
    Assert.Equal(HttpStatusCode.Accepted, status)
    fake.Waiting.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()
    let status, text = sendWith harness.Client HttpMethod.Get (path project document) None
    Assert.Equal(HttpStatusCode.OK, status)
    (
        use json = JsonDocument.Parse text
        let root = json.RootElement
        Assert.Equal("running", root.GetProperty("status").GetString())
        let processed = root.GetProperty("pagesProcessed").GetInt32()
        Assert.InRange(processed, 1, 39)
        Assert.Equal(40, root.GetProperty("totalPages").GetInt32()))
    // A repeat ask while it runs shows the same progress and starts nothing.
    let status, again = ask harness.Client project document
    Assert.Equal(HttpStatusCode.OK, status)
    Assert.Contains("pagesProcessed", again)
    fake.Release()
    SummaryJobsAccess.settled harness.App project document |> fun task -> task.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()
    let status, text = sendWith harness.Client HttpMethod.Get (path project document) None
    Assert.Equal(HttpStatusCode.OK, status)
    use json = JsonDocument.Parse text
    Assert.Equal("finished", json.RootElement.GetProperty("status").GetString())

// F004 T005: a failure is the job's state, readable on return, with what F003 sent.

let private settleJob (harness: TestHarness) project document =
    SummaryJobsAccess.settled harness.App project document |> fun task -> task.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()

let private getJson (harness: TestHarness) project document =
    let status, text = sendWith harness.Client HttpMethod.Get (path project document) None
    Assert.Equal(HttpStatusCode.OK, status)
    (use json = JsonDocument.Parse text in json.RootElement.Clone())

[<Fact; Trait("Task", "T005")>]
let ``a service error is kept as a failed state with F003's code and detail`` () =
    let fake = new FakeChatService(Status 500)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    let status, _ = ask harness.Client project document
    Assert.Equal(HttpStatusCode.Accepted, status)
    settleJob harness project document
    let state = getJson harness project document
    Assert.Equal("failed", state.GetProperty("status").GetString())
    Assert.Equal("service_failed", state.GetProperty("code").GetString())
    let detail = state.GetProperty("detail").GetString()
    Assert.False(String.IsNullOrWhiteSpace detail)
    // The service's own body never reaches the researcher.
    Assert.DoesNotContain("service detail", detail)
    // It is still there when asked again later.
    Assert.Equal("failed", (getJson harness project document).GetProperty("status").GetString())

[<Fact; Trait("Task", "T005")>]
let ``a reply without a valid citation is kept as not_enough_text with the omitted count`` () =
    let fake = new FakeChatService(Reply """{"points":[{"text":"Nowhere","pages":[999]}]}""")
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    ask harness.Client project document |> ignore
    settleJob harness project document
    let state = getJson harness project document
    Assert.Equal("failed", state.GetProperty("status").GetString())
    Assert.Equal("not_enough_text", state.GetProperty("code").GetString())
    Assert.Equal(1, state.GetProperty("omittedPoints").GetInt32())

[<Fact; Trait("Task", "T005")>]
let ``asking again after a failure starts a new job that replaces it`` () =
    let fake = new FakeChatService(fun n -> if n = 1 then Status 500 else Reply answer)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    ask harness.Client project document |> ignore
    settleJob harness project document
    Assert.Equal("failed", (getJson harness project document).GetProperty("status").GetString())
    let status, _ = ask harness.Client project document
    Assert.Equal(HttpStatusCode.Accepted, status)
    settleJob harness project document
    Assert.Equal("finished", (getJson harness project document).GetProperty("status").GetString())
    Assert.Equal(2, fake.Requests.Length)


// F004 T006: a finished summary is stored, found after a restart, and dismissing deletes it.

let private delete (harness: TestHarness) project document = sendWith harness.Client HttpMethod.Delete (path project document) None

let private storedRows (harness: TestHarness) =
    use connection = new Microsoft.Data.Sqlite.SqliteConnection(WorkspaceDb.connectionString harness.Root)
    connection.Open()
    use command = connection.CreateCommand()
    command.CommandText <- "SELECT count(*) FROM DocumentSummaries"
    command.ExecuteScalar() :?> int64

/// Asks on a fresh host over `workspace`, waits for the job and shuts the host down; returns the ids.
let private finishThenStop (workspace: TemporaryWorkspace) (script: ChatScript) =
    let fake = new FakeChatService(script)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake, workspace = workspace)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    ask harness.Client project document |> ignore
    settleJob harness project document
    project, document

[<Fact; Trait("Task", "T006")>]
let ``a finished summary is found after the host restarts, with its time, host and model`` () =
    use workspace = TemporaryWorkspace.Create()
    let project, document = finishThenStop workspace (Reply answer)
    let fake = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake, workspace = workspace)
    let state = getJson harness project document
    Assert.Equal("finished", state.GetProperty("status").GetString())
    let summary = state.GetProperty("summary")
    Assert.Equal(document, summary.GetProperty("documentId").GetGuid())
    Assert.Equal("Main idea", summary.GetProperty("points").[0].GetProperty("text").GetString())
    Assert.Equal("127.0.0.1:9", summary.GetProperty("serviceHost").GetString())
    Assert.Equal("m", summary.GetProperty("model").GetString())
    Assert.True(summary.GetProperty("generatedAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes -10.)
    Assert.Empty(fake.Requests)
    // Asking again does not start a second job: the stored summary is returned.
    let status, _ = ask harness.Client project document
    Assert.Equal(HttpStatusCode.OK, status)
    Assert.Empty(fake.Requests)

[<Fact; Trait("Task", "T006")>]
let ``dismissing a finished summary deletes it for good and she can ask again`` () =
    use workspace = TemporaryWorkspace.Create()
    let project, document =
        let fake = new FakeChatService(Reply answer)
        use harness = TestHarness.Start(settings = settings, summaryHandler = fake, workspace = workspace)
        let project = seedProject harness.Root
        let document = addDocument harness.Client project "article-en.pdf"
        ask harness.Client project document |> ignore
        settleJob harness project document
        Assert.Equal(1L, storedRows harness)
        let status, _ = delete harness project document
        Assert.Equal(HttpStatusCode.NoContent, status)
        Assert.Equal(0L, storedRows harness)
        let status, _ = sendWith harness.Client HttpMethod.Get (path project document) None
        Assert.Equal(HttpStatusCode.NotFound, status)
        // Idempotent.
        Assert.Equal(HttpStatusCode.NoContent, fst (delete harness project document))
        project, document
    let fake = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake, workspace = workspace)
    let status, _ = sendWith harness.Client HttpMethod.Get (path project document) None
    Assert.Equal(HttpStatusCode.NotFound, status)
    let status, _ = ask harness.Client project document
    Assert.Equal(HttpStatusCode.Accepted, status)
    settleJob harness project document
    Assert.Equal(1, fake.Requests.Length)

[<Fact; Trait("Task", "T006")>]
let ``dismissing a failed summary clears it and nothing was stored`` () =
    let fake = new FakeChatService(Status 500)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    ask harness.Client project document |> ignore
    settleJob harness project document
    Assert.Equal(0L, storedRows harness)
    Assert.Equal(HttpStatusCode.NoContent, fst (delete harness project document))
    let status, _ = sendWith harness.Client HttpMethod.Get (path project document) None
    Assert.Equal(HttpStatusCode.NotFound, status)

[<Fact; Trait("Task", "T006")>]
let ``a summary still running when the host shuts down stores nothing`` () =
    use workspace = TemporaryWorkspace.Create()
    let fake = new FakeChatService(Hold answer)
    let project, document =
        use harness = TestHarness.Start(settings = settings, summaryHandler = fake, workspace = workspace)
        let project = seedProject harness.Root
        let document = addDocument harness.Client project "article-en.pdf"
        ask harness.Client project document |> ignore
        fake.Waiting.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()
        project, document
    let restarted = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = settings, summaryHandler = restarted, workspace = workspace)
    Assert.Equal(0L, storedRows harness)
    let status, _ = sendWith harness.Client HttpMethod.Get (path project document) None
    Assert.Equal(HttpStatusCode.NotFound, status)

// F004 T007: one summary per document in a project; several at once.

let private waitFor (task: Threading.Tasks.Task) = task.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()

let private statusOf (text: string) = (use json = JsonDocument.Parse text in json.RootElement.GetProperty("status").GetString())

[<Fact; Trait("Task", "T007")>]
let ``a repeat POST while running answers 200 running, sends nothing new and the first job continues`` () =
    let fake = new FakeChatService(Hold answer)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    Assert.Equal(HttpStatusCode.Accepted, fst (ask harness.Client project document))
    waitFor fake.Waiting
    let status, text = ask harness.Client project document
    Assert.Equal(HttpStatusCode.OK, status)
    Assert.Equal("running", statusOf text)
    Assert.Equal(1, fake.Requests.Length)
    fake.Release()
    settleJob harness project document
    Assert.Equal("finished", (getJson harness project document).GetProperty("status").GetString())
    Assert.Equal(1, fake.Requests.Length)

[<Fact; Trait("Task", "T007")>]
let ``a running summary answers a repeat POST with 200 without the confirmation a service elsewhere needs`` () =
    let fake = new FakeChatService(Hold answer)
    let elsewhere = [ "LexScholar:Summary:Endpoint", "https://summary.example.test/v1"; "LexScholar:Summary:Model", "m" ]
    use harness = TestHarness.Start(settings = elsewhere, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    let confirmed = """{"confirmSend":true,"confirmedHost":"summary.example.test","confirmedModel":"m"}"""
    let post body = sendWith harness.Client HttpMethod.Post (path project document) (Some (new StringContent(body, Encoding.UTF8, "application/json")))
    Assert.Equal(HttpStatusCode.Accepted, fst (post confirmed))
    waitFor fake.Waiting
    // No confirmation, and even a destination that would be refused: the pair already has its summary.
    for body in [ """{"confirmSend":false}"""; """{"destination":"nowhere"}""" ] do
        let status, text = post body
        Assert.Equal(HttpStatusCode.OK, status)
        Assert.Equal("running", statusOf text)
    Assert.Equal(1, fake.Requests.Length)
    fake.Release()
    settleJob harness project document

[<Fact; Trait("Task", "T007")>]
let ``a repeat POST after finishing answers 200 with the summary and calls the service no more`` () =
    let fake = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    ask harness.Client project document |> ignore
    settleJob harness project document
    let status, text = ask harness.Client project document
    Assert.Equal(HttpStatusCode.OK, status)
    Assert.Equal("finished", statusOf text)
    Assert.Equal(1, fake.Requests.Length)

[<Fact; Trait("Task", "T007")>]
let ``two POSTs racing for one document start one job`` () =
    let fake = new FakeChatService(Hold answer)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project "article-en.pdf"
    let results =
        [ for _ in 1 .. 2 -> Threading.Tasks.Task.Run(fun () -> fst (ask harness.Client project document)) ]
        |> List.map (fun task -> task.GetAwaiter().GetResult())
        |> List.sort
    Assert.Equal<HttpStatusCode list>([ HttpStatusCode.OK; HttpStatusCode.Accepted ], results)
    waitFor fake.Waiting
    fake.Release()
    settleJob harness project document
    Assert.Equal(1, fake.Requests.Length)

/// Completes `Arrived` when `expected` requests have reached the inner handler (a handshake: all of them are in flight together).
type private ArrivalCounter(inner: Net.Http.HttpMessageHandler, expected: int) =
    inherit DelegatingHandler(inner)
    let mutable count = 0
    let arrived = Threading.Tasks.TaskCompletionSource()
    member _.Arrived : Threading.Tasks.Task = arrived.Task
    override _.SendAsync(request, cancellation) =
        if Threading.Interlocked.Increment(&count) = expected then arrived.TrySetResult() |> ignore
        base.SendAsync(request, cancellation)

[<Fact; Trait("Task", "T007")>]
let ``three documents summarize at once and all finish`` () =
    let fake = new FakeChatService(Hold """{"points":[{"text":"Main idea","pages":[1]}]}""")
    let counter = new ArrivalCounter(fake, 3)
    use harness = TestHarness.Start(settings = settings, summaryHandler = counter)
    let project = seedProject harness.Root
    let documents = [ "article-en.pdf"; "article-pt.pdf"; "partial.pdf" ] |> List.map (addDocument harness.Client project)
    for document in documents do
        Assert.Equal(HttpStatusCode.Accepted, fst (ask harness.Client project document))
    // All three requests are held in flight before any is released: the jobs ran side by side.
    waitFor counter.Arrived
    Assert.Equal(3, fake.Requests.Length)
    fake.Release()
    for document in documents do
        settleJob harness project document
        Assert.Equal("finished", (getJson harness project document).GetProperty("status").GetString())

[<Fact; Trait("Task", "T007")>]
let ``the same document in two projects has two independent summaries`` () =
    let fake = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let first = seedProject harness.Root
    let second = seedProject harness.Root
    let document = addDocument harness.Client first "article-en.pdf"
    let body = JsonSerializer.Serialize({| documentId = string document; role = "FocusedSource" |})
    Assert.Equal(HttpStatusCode.Created, fst (sendWith harness.Client HttpMethod.Post $"/api/projects/{second}/documents" (Some (new StringContent(body, Encoding.UTF8, "application/json")))))
    Assert.Equal(HttpStatusCode.Accepted, fst (ask harness.Client first document))
    settleJob harness first document
    // The other project has nothing yet, and asking there starts its own job (202, not 200).
    Assert.Equal(HttpStatusCode.NotFound, fst (sendWith harness.Client HttpMethod.Get (path second document) None))
    Assert.Equal(HttpStatusCode.Accepted, fst (ask harness.Client second document))
    settleJob harness second document
    Assert.Equal(2, fake.Requests.Length)
    // Dismissing one leaves the other.
    Assert.Equal(HttpStatusCode.NoContent, fst (delete harness first document))
    Assert.Equal(HttpStatusCode.NotFound, fst (sendWith harness.Client HttpMethod.Get (path first document) None))
    Assert.Equal("finished", (getJson harness second document).GetProperty("status").GetString())
