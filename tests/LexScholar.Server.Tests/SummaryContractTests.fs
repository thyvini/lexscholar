/// HTTP contract of the summary endpoint (F003, contracts/summary-api.yaml).
module LexScholar.Server.Tests.SummaryContractTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Net.Http.Headers
open System.IO
open System.Text.Json
open LexScholar.Infrastructure
open Xunit

let private token (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    json.RootElement.GetProperty("requestToken").GetString()

let private requestSummary (client: HttpClient) (body: string) =
    let csrf = token client
    use message = new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/summary")
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf) |> ignore
    message.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    let response = client.Send(message)
    let text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    response.StatusCode, text

[<Fact; Trait("Task", "T001")>]
let ``without a configured service the endpoint answers 503 summary_service_not_configured`` () =
    use harness = TestHarness.Start()
    let status, text = requestSummary harness.Client """{"confirmSend":false}"""
    Assert.Equal(HttpStatusCode.ServiceUnavailable, status)
    use json = JsonDocument.Parse(text)
    let problem = json.RootElement
    Assert.Equal("summary_service_not_configured", problem.GetProperty("code").GetString())
    Assert.Contains("LexScholar:Summary:Endpoint", problem.GetProperty("detail").GetString())

let private settings = [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ]

let private fixture name = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", name))

let private send (client: HttpClient) (method: HttpMethod) (path: string) (content: HttpContent option) =
    use message = new HttpRequestMessage(method, path)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token client) |> ignore
    content |> Option.iter (fun body -> message.Content <- body)
    let response = client.Send(message)
    response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

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

/// Imports a summary fixture through HTTP; returns its document id.
let private import (client: HttpClient) (name: string) =
    let part = new ByteArrayContent(fixture name)
    part.Headers.ContentType <- MediaTypeHeaderValue("application/pdf")
    let form = new MultipartFormDataContent()
    form.Add(part, "file", name)
    let status, text = send client HttpMethod.Post "/api/documents" (Some (form :> HttpContent))
    Assert.True(status = HttpStatusCode.Created || status = HttpStatusCode.OK, text)
    use json = JsonDocument.Parse(text)
    json.RootElement.GetProperty("id").GetGuid()

let private associate (client: HttpClient) (project: Guid) (document: Guid) =
    let body = JsonSerializer.Serialize({| documentId = string document; role = "FocusedSource" |})
    let status, text = send client HttpMethod.Post $"/api/projects/{project}/documents" (Some (new StringContent(body, Encoding.UTF8, "application/json")))
    Assert.Equal(HttpStatusCode.Created, status)

let private summarize (client: HttpClient) (project: Guid) (document: Guid) =
    send client HttpMethod.Post $"/api/projects/{project}/documents/{document}/summary"
        (Some (new StringContent("""{"confirmSend":false}""", Encoding.UTF8, "application/json")))

let private expectProblem (fake: FakeChatService) (root: string) status code (actual: HttpStatusCode, text: string) =
    Assert.Equal(status, actual)
    use json = JsonDocument.Parse(text)
    Assert.Equal(code, json.RootElement.GetProperty("code").GetString())
    Assert.DoesNotContain(root, text)
    Assert.Empty(fake.Requests)

/// Runs `action` with a started host, an associated fixture document and the fake service.
let private withDocument fixtureName (action: TestHarness -> FakeChatService -> Guid -> Guid -> unit) =
    let fake = new FakeChatService(Status 500)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = import harness.Client fixtureName
    associate harness.Client project document
    action harness fake project document

[<Fact; Trait("Task", "T002")>]
let ``a document that is not associated with the project is 404 not_found`` () =
    let fake = new FakeChatService(Status 500)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = import harness.Client "article-en.pdf"
    expectProblem fake harness.Root HttpStatusCode.NotFound "not_found" (summarize harness.Client project document)
    expectProblem fake harness.Root HttpStatusCode.NotFound "not_found" (summarize harness.Client (Guid.NewGuid()) document)

[<Fact; Trait("Task", "T002")>]
let ``an unknown document is 404 document_not_found`` () =
    let fake = new FakeChatService(Status 500)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    expectProblem fake harness.Root HttpStatusCode.NotFound "document_not_found" (summarize harness.Client project (Guid.NewGuid()))

[<Fact; Trait("Task", "T002")>]
let ``a missing original is 503 document_unavailable`` () =
    withDocument "article-en.pdf" (fun harness fake project document ->
        for file in Directory.GetFiles(DocumentStore.originalsDirectory harness.Root) do
            File.SetUnixFileMode(file, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            File.Delete(file)
        expectProblem fake harness.Root HttpStatusCode.ServiceUnavailable "document_unavailable" (summarize harness.Client project document))

[<Theory; Trait("Task", "T002")>]
[<InlineData("too-long.pdf", "too_many_pages")>]
[<InlineData("scanned.pdf", "no_text")>]
[<InlineData("little-text.pdf", "not_enough_text")>]
let ``documents that cannot be summarized are 422 and nothing is sent`` (fixtureName: string, code: string) =
    withDocument fixtureName (fun harness fake project document ->
        expectProblem fake harness.Root (enum<HttpStatusCode> 422) code (summarize harness.Client project document))

/// Runs the endpoint against the fake with the given settings; returns status, parsed NDJSON lines and the fake.
let private summarizeWith fixtureName (script: ChatScript) extra =
    let fake = new FakeChatService(script)
    use harness = TestHarness.Start(settings = settings @ extra, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = import harness.Client fixtureName
    associate harness.Client project document
    let status, text = summarize harness.Client project document
    let events = text.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun line -> JsonDocument.Parse(line).RootElement.Clone())
    status, events, fake, document

[<Fact; Trait("Task", "T003")>]
let ``a summary event has the contract shape and the default request body has only model and messages`` () =
    let answer = """{"points":[{"text":"Main idea","pages":[1,2]}]}"""
    let status, events, fake, document = summarizeWith "article-en.pdf" (Reply answer) []
    Assert.Equal(HttpStatusCode.OK, status)
    let event = Assert.Single(events)
    Assert.Equal("summary", event.GetProperty("type").GetString())
    Assert.Equal(document, event.GetProperty("documentId").GetGuid())
    let point = event.GetProperty("points").[0]
    Assert.Equal("Main idea", point.GetProperty("text").GetString())
    Assert.Equal<int list>([ 1; 2 ], [ for p in point.GetProperty("pages").EnumerateArray() -> p.GetInt32() ])
    Assert.Equal(0, event.GetProperty("omittedPoints").GetInt32())
    Assert.Equal(12, event.GetProperty("pagesCovered").GetArrayLength())
    Assert.Equal(0, event.GetProperty("unreadablePages").GetArrayLength())
    Assert.Equal("m", event.GetProperty("model").GetString())
    Assert.Equal("127.0.0.1:9", event.GetProperty("serviceHost").GetString())
    Assert.True(event.GetProperty("generatedAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5))
    let request = Assert.Single(fake.Requests)
    Assert.Equal(Uri("http://127.0.0.1:9/v1/chat/completions"), request.Uri)
    Assert.Equal(None, request.Authorization)
    use body = JsonDocument.Parse(request.Body)
    Assert.Equal<string list>([ "messages"; "model" ], [ for p in body.RootElement.EnumerateObject() -> p.Name ] |> List.sort)
    Assert.Equal("m", body.RootElement.GetProperty("model").GetString())

[<Fact; Trait("Task", "T003")>]
let ``optional settings are sent only when set, and the key is a Bearer token`` () =
    let _, _, fake, _ =
        summarizeWith "article-en.pdf" (Reply """{"points":[]}""")
            [ "LexScholar:Summary:Temperature", "0.2"; "LexScholar:Summary:MaxOutputTokens", "500"; "LexScholar:Summary:ApiKey", "secret-key" ]
    let request = Assert.Single(fake.Requests)
    Assert.Equal(Some "Bearer secret-key", request.Authorization)
    use body = JsonDocument.Parse(request.Body)
    Assert.Equal(0.2, body.RootElement.GetProperty("temperature").GetDouble())
    Assert.Equal(500, body.RootElement.GetProperty("max_tokens").GetInt32())

[<Fact; Trait("Task", "T003")>]
let ``citations to unreadable or missing pages are omitted and counted`` () =
    let answer = """{"points":[{"text":"ok","pages":[1,3]},{"text":"gone","pages":[999]},{"text":"blank","pages":[2]},{"text":"mixed","pages":[1,5]}]}"""
    let _, events, _, _ = summarizeWith "partial.pdf" (Reply answer) []
    let event = Assert.Single(events)
    Assert.Equal("summary", event.GetProperty("type").GetString())
    Assert.Equal(1, event.GetProperty("points").GetArrayLength())
    Assert.Equal(3, event.GetProperty("omittedPoints").GetInt32())
    let unreadable = [ for p in event.GetProperty("unreadablePages").EnumerateArray() -> p.GetInt32() ]
    Assert.Equal<int list>([ 2; 5 ], unreadable)
    let covered = [ for p in event.GetProperty("pagesCovered").EnumerateArray() -> p.GetInt32() ]
    Assert.DoesNotContain(2, covered)
    Assert.DoesNotContain(5, covered)

[<Fact; Trait("Task", "T003")>]
let ``no valid point ends with a not_enough_text failed event`` () =
    let _, events, _, _ = summarizeWith "article-en.pdf" (Reply """{"points":[{"text":"x","pages":[999]}]}""") []
    let event = Assert.Single(events)
    Assert.Equal("failed", event.GetProperty("type").GetString())
    Assert.Equal("not_enough_text", event.GetProperty("code").GetString())
    Assert.Equal(1, event.GetProperty("omittedPoints").GetInt32())

[<Theory; Trait("Task", "T003")>]
[<InlineData(500)>]
[<InlineData(400)>]
[<InlineData(302)>]
let ``an error status is a service_failed event that never echoes the service body`` (code: int) =
    let _, events, _, _ = summarizeWith "article-en.pdf" (Status code) []
    let event = Assert.Single(events)
    Assert.Equal("failed", event.GetProperty("type").GetString())
    Assert.Equal("service_failed", event.GetProperty("code").GetString())
    Assert.DoesNotContain("service detail", event.GetRawText())

[<Fact; Trait("Task", "T003")>]
let ``unparseable output and a response over 1 MB are service_failed`` () =
    for answer in [ "no json at all"; String('x', 1_100_000) ] do
        let _, events, _, _ = summarizeWith "article-en.pdf" (Reply answer) []
        let event = Assert.Single(events)
        Assert.Equal("service_failed", event.GetProperty("code").GetString())

type private ThrowingStream() =
    inherit MemoryStream()
    override _.ReadAsync(_: byte array, _: int, _: int, _: Threading.CancellationToken) : Threading.Tasks.Task<int> =
        Threading.Tasks.Task.FromException<int>(IOException("connection closed"))
    override _.ReadAsync(_: Memory<byte>, _: Threading.CancellationToken) : Threading.Tasks.ValueTask<int> =
        Threading.Tasks.ValueTask<int>(Threading.Tasks.Task.FromException<int>(IOException("connection closed")))

/// Sends the headers, then breaks the connection while the body is read.
type private TruncatedBody() =
    inherit HttpContent()
    override _.SerializeToStreamAsync(_, _) = Threading.Tasks.Task.FromException(IOException("connection closed"))
    override _.TryComputeLength(length) = length <- 10L; false
    override _.CreateContentReadStreamAsync() = Threading.Tasks.Task.FromResult<Stream>(new ThrowingStream())

type private BrokenBodyHandler() =
    inherit HttpMessageHandler()
    override _.SendAsync(_, _) = Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK, Content = new TruncatedBody()))

[<Fact; Trait("Task", "T003")>]
let ``a response that breaks while its body is read is a service_failed event`` () =
    use harness = TestHarness.Start(settings = settings, summaryHandler = new BrokenBodyHandler())
    let project = seedProject harness.Root
    let document = import harness.Client "article-en.pdf"
    associate harness.Client project document
    let status, text = summarize harness.Client project document
    Assert.Equal(HttpStatusCode.OK, status)
    use json = JsonDocument.Parse(text.Trim())
    Assert.Equal("service_failed", json.RootElement.GetProperty("code").GetString())

[<Fact; Trait("Task", "T006")>]
let ``a 40-page document streams progress to 40 of 40, then combining, then one summary`` () =
    // Later map calls cite page 1, which is not in their chunk, so only the first chunk yields a point;
    // the combine call may cite page 1.
    let script n = Reply """{"points":[{"text":"Main idea","pages":[1]}]}"""
    let status, events, fake, _ = summarizeWith "long-40.pdf" (script 0) []
    Assert.Equal(HttpStatusCode.OK, status)
    let kinds = events |> Array.map (fun e -> e.GetProperty("type").GetString()) |> Array.toList
    Assert.Equal("summary", List.last kinds)
    Assert.Equal(1, kinds |> List.filter ((=) "summary") |> List.length)
    Assert.Contains("combining", kinds)
    let progress = events |> Array.filter (fun e -> e.GetProperty("type").GetString() = "progress")
    Assert.True(progress.Length >= 3, "several chunks mean several progress events")
    let counts = progress |> Array.map (fun e -> e.GetProperty("pagesProcessed").GetInt32())
    Assert.Equal<int[]>(Array.sort counts, counts)
    Assert.Equal(40, counts |> Array.last)
    Assert.All(progress, fun e -> Assert.Equal(40, e.GetProperty("totalPages").GetInt32()))
    // Progress precedes the combine step and the combine call is the last request.
    Assert.True(List.findIndex ((=) "combining") kinds > (List.rev kinds |> List.findIndex ((=) "progress") |> fun i -> kinds.Length - 1 - i))
    Assert.Equal(progress.Length, fake.Requests.Length) // one map call per chunk (progress has an extra opening event)

let private elsewhere = [ "LexScholar:Summary:Endpoint", "http://summary.example.test:8080/v1"; "LexScholar:Summary:Model", "gpt" ]
let private answer = """{"points":[{"text":"Main idea","pages":[1,2]}]}"""

/// Posts `body` to the summary endpoint of an imported fixture; returns status, body text and the fake.
let private summarizeBody fixtureName endpointSettings (body: string) =
    let fake = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = endpointSettings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = import harness.Client fixtureName
    associate harness.Client project document
    let status, text =
        send harness.Client HttpMethod.Post $"/api/projects/{project}/documents/{document}/summary"
            (Some (new StringContent(body, Encoding.UTF8, "application/json")))
    status, text, fake

[<Theory; Trait("Task", "T007")>]
[<InlineData("""{"confirmSend":false}""")>]
[<InlineData("""{}""")>]
[<InlineData("""{"confirmSend":"true"}""")>]
[<InlineData("""{"confirmSend":true}""")>]
[<InlineData("""{"confirmSend":true,"confirmedHost":"summary.example.test:8080"}""")>]
[<InlineData("""{"confirmSend":true,"confirmedModel":"gpt"}""")>]
[<InlineData("""{"confirmSend":true,"confirmedHost":"other.example.test:8080","confirmedModel":"gpt"}""")>]
[<InlineData("""{"confirmSend":true,"confirmedHost":"summary.example.test:8080","confirmedModel":"other-model"}""")>]
[<InlineData("""{"confirmSend":false,"confirmedHost":"summary.example.test:8080","confirmedModel":"gpt"}""")>]
[<InlineData("""{"confirmSend":true,"confirmedHost":1,"confirmedModel":"gpt"}""")>]
[<InlineData("""not json""")>]
[<InlineData("")>]
let ``a service elsewhere without a confirmation of this host and model is 409 and receives nothing`` (body: string) =
    let status, text, fake = summarizeBody "article-en.pdf" elsewhere body
    Assert.Equal(HttpStatusCode.Conflict, status)
    use json = JsonDocument.Parse(text)
    Assert.Equal("confirmation_required", json.RootElement.GetProperty("code").GetString())
    Assert.Equal("summary.example.test:8080", json.RootElement.GetProperty("serviceHost").GetString())
    Assert.Equal("gpt", json.RootElement.GetProperty("model").GetString())
    Assert.Empty(fake.Requests)

[<Fact; Trait("Task", "T007")>]
let ``local checks come before the confirmation`` () =
    let status, text, fake = summarizeBody "scanned.pdf" elsewhere """{"confirmSend":false}"""
    Assert.Equal(enum<HttpStatusCode> 422, status)
    Assert.Contains("no_text", text)
    Assert.Empty(fake.Requests)

[<Fact; Trait("Task", "T007")>]
let ``a service elsewhere with confirmation of that host and model gets the text and answers with a summary`` () =
    let status, text, fake =
        summarizeBody "article-en.pdf" elsewhere """{"confirmSend":true,"confirmedHost":"summary.example.test:8080","confirmedModel":"gpt"}"""
    Assert.Equal(HttpStatusCode.OK, status)
    use json = JsonDocument.Parse(text.Split('\n', StringSplitOptions.RemoveEmptyEntries).[0])
    Assert.Equal("summary", json.RootElement.GetProperty("type").GetString())
    Assert.Equal("summary.example.test:8080", json.RootElement.GetProperty("serviceHost").GetString())
    let request = Assert.Single(fake.Requests)
    Assert.Equal(Uri("http://summary.example.test:8080/v1/chat/completions"), request.Uri)

[<Theory; Trait("Task", "T007")>]
[<InlineData("""{"confirmSend":false}""")>]
[<InlineData("""{"confirmSend":true}""")>]
let ``a local service ignores confirmSend`` (body: string) =
    let status, text, fake = summarizeBody "article-en.pdf" settings body
    Assert.Equal(HttpStatusCode.OK, status)
    Assert.Contains("\"type\":\"summary\"", text)
    Assert.Single(fake.Requests) |> ignore
