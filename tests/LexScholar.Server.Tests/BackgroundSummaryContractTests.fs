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
    Assert.Equal(HttpStatusCode.NotImplemented, answered.StatusCode)

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
