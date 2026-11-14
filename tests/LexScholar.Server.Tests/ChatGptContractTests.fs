/// HTTP contract of the F005 sign-in routes (contracts/chatgpt-api.yaml), shared pieces only (T001).
module LexScholar.Server.Tests.ChatGptContractTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open LexScholar.Server
open Xunit

/// Collects every formatted log line.
type CapturingProvider() =
    let lines = System.Collections.Concurrent.ConcurrentQueue<string>()
    member _.Lines = lines |> Seq.toList
    interface ILoggerProvider with
        member _.CreateLogger(_category) =
            { new ILogger with
                member _.BeginScope(_state) = null
                member _.IsEnabled(_level) = true
                member _.Log(_level, _eventId, state, error, formatter) = lines.Enqueue(formatter.Invoke(state, error)) }
        member _.Dispose() = ()

let token (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    json.RootElement.GetProperty("requestToken").GetString()

/// POST with the Origin header and, when `csrf` is given, the antiforgery token.
let post (client: HttpClient) (path: string) (csrf: string option) =
    use message = new HttpRequestMessage(HttpMethod.Post, path)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    csrf |> Option.iter (fun value -> message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", value) |> ignore)
    message.Content <- new StringContent("{}", Text.Encoding.UTF8, "application/json")
    use response = client.Send(message)
    response.StatusCode

let destinations (client: HttpClient) =
    use response = client.GetAsync("/api/summary/destinations").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())

[<Fact; Trait("Task", "T001")>]
let ``destinations answers signed out with no configured service`` () =
    use harness = TestHarness.Start()
    use json = destinations harness.Client
    let root = json.RootElement
    Assert.False(root.GetProperty("chatgpt").GetProperty("signedIn").GetBoolean())
    Assert.False(root.GetProperty("welcomePending").GetBoolean())
    Assert.Equal(JsonValueKind.Null, root.GetProperty("configured").ValueKind)

[<Fact; Trait("Task", "T001")>]
let ``destinations reports the configured service from the settings`` () =
    let settings = [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ]
    use harness = TestHarness.Start(settings = settings)
    use json = destinations harness.Client
    let configured = json.RootElement.GetProperty("configured")
    Assert.Equal("127.0.0.1:9", configured.GetProperty("serviceHost").GetString())
    Assert.Equal("m", configured.GetProperty("model").GetString())
    Assert.True(configured.GetProperty("local").GetBoolean())

[<Theory; Trait("Task", "T001")>]
[<InlineData("/api/chatgpt/sign-out")>]
[<InlineData("/api/chatgpt/welcome/dismiss")>]
let ``unsafe chatgpt routes need the antiforgery token`` (path: string) =
    use harness = TestHarness.Start()
    Assert.Equal(HttpStatusCode.Forbidden, post harness.Client path None)

[<Fact; Trait("Task", "T001")>]
let ``a client sending Host 127.0.0.1 with the port is accepted`` () =
    use harness = TestHarness.Start()
    use message = new HttpRequestMessage(HttpMethod.Get, "/api/summary/destinations")
    message.Headers.Host <- $"127.0.0.1:{harness.Client.BaseAddress.Port}"
    use response = harness.Client.Send(message)
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

[<Fact; Trait("Task", "T001")>]
let ``the fake OpenAI service answers token, discovery, keys, models and a Responses stream`` () =
    use fake = new FakeOpenAi(ResponsesScript.Completed "hello world")
    use client = new HttpClient(fake, false)
    let get (url: string) = client.GetStringAsync(url).GetAwaiter().GetResult()
    Assert.Contains("revocation_endpoint", get "https://auth.openai.com/.well-known/openid-configuration")
    Assert.Contains("RS256", get "https://auth.openai.com/.well-known/jwks.json")
    Assert.Contains("gpt-test", get "https://api.openai.com/v1/models")
    use token = client.PostAsync("https://auth.openai.com/api/accounts/oauth/token", new FormUrlEncodedContent(dict [ "grant_type", "authorization_code" ])).GetAwaiter().GetResult()
    Assert.Contains("access-token-1", token.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    use stream = client.PostAsync("https://api.openai.com/v1/responses", new StringContent("{}")).GetAwaiter().GetResult()
    let events = stream.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert.Contains("response.completed", events)
    Assert.Equal(5, fake.Requests.Length)


// ---- T004: sign in ----

let workspaceRoot (harness: TestHarness) = harness.Root

/// A client that does not follow redirects, for the callback.
let plainClient (harness: TestHarness) = new HttpClient(new HttpClientHandler(AllowAutoRedirect = false), true, BaseAddress = harness.Client.BaseAddress)

let startSignIn (harness: TestHarness) =
    use message = new HttpRequestMessage(HttpMethod.Post, "/api/chatgpt/sign-in")
    message.Headers.TryAddWithoutValidation("Origin", harness.Client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token harness.Client) |> ignore
    message.Content <- new StringContent("{}", Text.Encoding.UTF8, "application/json")
    use response = harness.Client.Send message
    let body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    response.StatusCode, body

let urlOf (body: string) =
    use json = JsonDocument.Parse body
    Uri(json.RootElement.GetProperty("authorizationUrl").GetString())

let query (url: Uri) =
    url.Query.TrimStart('?').Split('&') |> Array.map (fun p -> let parts = p.Split('=', 2) in parts[0], Uri.UnescapeDataString parts[1]) |> dict

let callbackAt (harness: TestHarness) (path: string) =
    use client = plainClient harness
    use response = client.GetAsync(path).GetAwaiter().GetResult()
    response.StatusCode, (if isNull response.Headers.Location then "" else response.Headers.Location.ToString())

/// Starts a sign-in and returns the callback path the browser would reach with these extras.
let callbackPath (url: Uri) (extra: string) =
    let state = Uri.EscapeDataString((query url).["state"])
    $"/auth/callback?state={state}&code=code-1{extra}"

let signedIn (harness: TestHarness) =
    use json = destinations harness.Client
    json.RootElement.GetProperty("chatgpt").GetProperty("signedIn").GetBoolean()

let recordPath (harness: TestHarness) = IO.Path.Combine(workspaceRoot harness, "chatgpt-signin.json")

let signInThrough (harness: TestHarness) (fake: FakeOpenAi) (extra: string) =
    let status, body = startSignIn harness
    Assert.Equal(HttpStatusCode.OK, status)
    let url = urlOf body
    fake.Nonce <- (query url)["nonce"]
    callbackAt harness (callbackPath url (if extra = "" then $"&client_id={fake.ClientId}" else extra))

[<Fact; Trait("Task", "T004")>]
let ``start returns the authorization address with every parameter and the host's own callback`` () =
    use harness = TestHarness.Start()
    let status, body = startSignIn harness
    Assert.Equal(HttpStatusCode.OK, status)
    let url = urlOf body
    let p = query url
    Assert.Equal("auth.openai.com", url.Host)
    Assert.Equal("dynamic_agent_client", p["client_id"])
    Assert.Equal($"http://127.0.0.1:{harness.Client.BaseAddress.Port}/auth/callback", p["redirect_uri"])
    Assert.Equal("S256", p["code_challenge_method"])
    for name in [ "state"; "nonce"; "code_challenge"; "scope"; "resource"; "ext_agent_host_id" ] do
        Assert.False(String.IsNullOrEmpty p[name], name)
    Assert.Contains("chatgpt.tokens.use.direct", p["scope"])
    Assert.False(IO.File.Exists(recordPath harness))

[<Fact; Trait("Task", "T004")>]
let ``a completed sign-in shows the account and survives a host restart`` () =
    use workspace = TemporaryWorkspace.Create()
    let fake = new FakeOpenAi()
    do
        use harness = TestHarness.Start(summaryHandler = fake, workspace = workspace)
        let status, location = signInThrough harness fake ""
        Assert.Equal(HttpStatusCode.Redirect, status)
        Assert.Equal("/index.html?chatgpt=signed_in", location)
        use json = destinations harness.Client
        let chatgpt = json.RootElement.GetProperty("chatgpt")
        Assert.True(chatgpt.GetProperty("signedIn").GetBoolean())
        Assert.Equal("researcher@example.test", chatgpt.GetProperty("account").GetString())
        Assert.False(chatgpt.GetProperty("confirmed").GetBoolean())
        Assert.True(json.RootElement.GetProperty("welcomePending").GetBoolean())
        Assert.True(IO.File.Exists(recordPath harness))
        let exchange = fake.RequestsTo("auth.openai.com", "/api/accounts/oauth/token") |> List.exactlyOne
        Assert.Contains("client_id=client-test", exchange.Body)
    use again = TestHarness.Start(summaryHandler = fake, workspace = workspace)
    Assert.True(signedIn again)

[<Fact; Trait("Task", "T004")>]
let ``starting while signed in answers 409 already_signed_in`` () =
    let fake = new FakeOpenAi()
    use harness = TestHarness.Start(summaryHandler = fake)
    signInThrough harness fake "" |> ignore
    let status, body = startSignIn harness
    Assert.Equal(HttpStatusCode.Conflict, status)
    Assert.Contains("already_signed_in", body)

[<Fact; Trait("Task", "T004")>]
let ``cancelled outcomes leave no record and exchange nothing for a bad state`` () =
    let fake = new FakeOpenAi()
    use harness = TestHarness.Start(summaryHandler = fake)
    let expectCancelled (path: string) =
        let status, location = callbackAt harness path
        Assert.Equal(HttpStatusCode.Redirect, status)
        Assert.Equal("/index.html?chatgpt=cancelled", location)
        Assert.False(IO.File.Exists(recordPath harness))
    // no pending attempt
    expectCancelled "/auth/callback?state=x&code=c"
    // she declined
    let _, body = startSignIn harness
    expectCancelled "/auth/callback?error=access_denied"
    // unknown state
    let url = urlOf body
    expectCancelled "/auth/callback?state=unknown&code=c"
    Assert.Empty(fake.RequestsTo("auth.openai.com", "/api/accounts/oauth/token"))
    // the declined attempt is gone; a reused state after success is cancelled too
    let _, body2 = startSignIn harness
    let url2 = urlOf body2
    fake.Nonce <- (query url2)["nonce"]
    let ok = callbackPath url2 $"&client_id={fake.ClientId}"
    Assert.Equal("/index.html?chatgpt=signed_in", snd (callbackAt harness ok))
    Assert.Equal("/index.html?chatgpt=cancelled", snd (callbackAt harness ok))
    ignore url

[<Fact; Trait("Task", "T004")>]
let ``a grant without plan usage revokes the tokens and stores nothing`` () =
    let fake = new FakeOpenAi(Scope = "openid profile email offline_access")
    use harness = TestHarness.Start(summaryHandler = fake)
    let _, location = signInThrough harness fake ""
    Assert.Equal("/index.html?chatgpt=no_plan_access", location)
    Assert.False(IO.File.Exists(recordPath harness))
    Assert.Single(fake.RequestsTo("auth.openai.com", "/api/accounts/oauth/revoke")) |> ignore
    Assert.False(signedIn harness)

[<Fact; Trait("Task", "T004")>]
let ``failed outcomes leave no record`` () =
    let cases : (FakeOpenAi * string) list =
        [ new FakeOpenAi(TokenFailure = Some(400, "{\"error\":\"invalid_grant\"}")), ""
          new FakeOpenAi(), "&client_id=other-client"      // ID token audience is not the issued client id
          new FakeOpenAi(), "&code=" ]
    for fake, extra in cases do
        use harness = TestHarness.Start(summaryHandler = fake)
        let _, location = signInThrough harness fake (if extra = "" then $"&client_id={fake.ClientId}" else extra)
        Assert.Equal("/index.html?chatgpt=failed", location)
        Assert.False(IO.File.Exists(recordPath harness))
    // the issued client id is missing from the callback
    let fake = new FakeOpenAi()
    use harness = TestHarness.Start(summaryHandler = fake)
    let _, location = signInThrough harness fake "&other=1"
    Assert.Equal("/index.html?chatgpt=failed", location)
    Assert.False(IO.File.Exists(recordPath harness))

[<Fact; Trait("Task", "T004")>]
let ``the callback query string is never logged`` () =
    use workspace = TemporaryWorkspace.Create()
    let provider = new CapturingProvider()
    let builder = HostSetup.builder workspace.Root []
    builder.Logging.AddProvider provider |> ignore
    let fake = new FakeOpenAi()
    builder.Services.AddSingleton<HttpMessageHandler>(fake) |> ignore
    match Program.create builder false with
    | Error error -> Assert.Fail $"{error}"
    | Ok app ->
        use app = app
        app.StartAsync().GetAwaiter().GetResult()
        let address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses |> Seq.exactlyOne
        use client = new HttpClient(new HttpClientHandler(AllowAutoRedirect = false), true, BaseAddress = Uri address)
        use _ = client.GetAsync("/auth/callback?state=SECRETSTATE&code=SECRETCODE").GetAwaiter().GetResult()
        app.StopAsync().GetAwaiter().GetResult()
        for line in provider.Lines do
            Assert.DoesNotContain("SECRETSTATE", line)
            Assert.DoesNotContain("SECRETCODE", line)


// ---- T005: sign out, the welcome ----

let postBody (harness: TestHarness) (path: string) =
    use message = new HttpRequestMessage(HttpMethod.Post, path)
    message.Headers.TryAddWithoutValidation("Origin", harness.Client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token harness.Client) |> ignore
    message.Content <- new StringContent("{}", Text.Encoding.UTF8, "application/json")
    use response = harness.Client.Send message
    response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

let signOut (harness: TestHarness) =
    let status, body = postBody harness "/api/chatgpt/sign-out"
    Assert.Equal(HttpStatusCode.OK, status)
    use json = JsonDocument.Parse body
    json.RootElement.GetProperty("revoked").GetBoolean()

let dismissWelcome (harness: TestHarness) =
    Assert.Equal(HttpStatusCode.NoContent, fst (postBody harness "/api/chatgpt/welcome/dismiss"))

let welcomePending (harness: TestHarness) =
    use json = destinations harness.Client
    json.RootElement.GetProperty("welcomePending").GetBoolean()

let marker (harness: TestHarness) = IO.Path.Combine(workspaceRoot harness, "chatgpt-welcome-shown")

[<Fact; Trait("Task", "T005")>]
let ``sign-out revokes, deletes the record and keeps the welcome marker`` () =
    let fake = new FakeOpenAi()
    use harness = TestHarness.Start(summaryHandler = fake)
    signInThrough harness fake "" |> ignore
    dismissWelcome harness
    Assert.True(signOut harness)
    Assert.Single(fake.RequestsTo("auth.openai.com", "/api/accounts/oauth/revoke")) |> ignore
    Assert.False(IO.File.Exists(recordPath harness))
    Assert.False(signedIn harness)
    Assert.True(IO.File.Exists(marker harness))

[<Fact; Trait("Task", "T005")>]
let ``sign-out still deletes the record when revocation fails`` () =
    let fake = new FakeOpenAi()
    use harness = TestHarness.Start(summaryHandler = fake)
    signInThrough harness fake "" |> ignore
    fake.RevocationStatus <- 500
    Assert.False(signOut harness)
    Assert.False(IO.File.Exists(recordPath harness))
    Assert.False(signedIn harness)

[<Fact; Trait("Task", "T005")>]
let ``sign-out still deletes the record when the service is down`` () =
    let handler = { new HttpMessageHandler() with member _.SendAsync(_, _) = raise (HttpRequestException "down") }
    use workspace = TemporaryWorkspace.Create()
    let fake = new FakeOpenAi()
    do
        use harness = TestHarness.Start(summaryHandler = fake, workspace = workspace)
        signInThrough harness fake "" |> ignore
    use harness = TestHarness.Start(summaryHandler = handler, workspace = workspace)
    Assert.True(IO.File.Exists(recordPath harness))
    Assert.False(signOut harness)
    Assert.False(IO.File.Exists(recordPath harness))
    Assert.False(signedIn harness)

[<Fact; Trait("Task", "T005")>]
let ``sign-out when signed out answers 200 and is repeatable`` () =
    let fake = new FakeOpenAi()
    use harness = TestHarness.Start(summaryHandler = fake)
    Assert.False(signOut harness)
    Assert.False(signOut harness)
    Assert.Empty(fake.RequestsTo("auth.openai.com", "/api/accounts/oauth/revoke"))

[<Fact; Trait("Task", "T005")>]
let ``the welcome is pending after the first sign-in only`` () =
    let fake = new FakeOpenAi()
    use harness = TestHarness.Start(summaryHandler = fake)
    Assert.False(welcomePending harness)
    signInThrough harness fake "" |> ignore
    Assert.True(welcomePending harness)
    dismissWelcome harness
    Assert.False(welcomePending harness)
    signOut harness |> ignore
    signInThrough harness fake "" |> ignore
    Assert.True(signedIn harness)
    Assert.False(welcomePending harness)

[<Fact; Trait("Task", "T005")>]
let ``a sign-in pending at sign-out cannot finish afterwards`` () =
    let fake = new FakeOpenAi()
    use harness = TestHarness.Start(summaryHandler = fake)
    let _, body = startSignIn harness
    let url = urlOf body
    fake.Nonce <- (query url)["nonce"]
    signOut harness |> ignore
    let _, location = callbackAt harness (callbackPath url $"&client_id={fake.ClientId}")
    Assert.Equal("/index.html?chatgpt=cancelled", location)
    Assert.Empty(fake.RequestsTo("auth.openai.com", "/api/accounts/oauth/token"))
    Assert.False(IO.File.Exists(recordPath harness))
    Assert.False(signedIn harness)

[<Fact; Trait("Task", "T005")>]
let ``a callback mid-exchange when she signs out is never saved`` () =
    let fake = new FakeOpenAi()
    use harness = TestHarness.Start(summaryHandler = fake)
    let release = fake.HoldTokens()
    let _, body = startSignIn harness
    let url = urlOf body
    fake.Nonce <- (query url)["nonce"]
    let callback = Threading.Tasks.Task.Run(fun () -> callbackAt harness (callbackPath url $"&client_id={fake.ClientId}"))
    fake.TokenArrived.GetAwaiter().GetResult()
    signOut harness |> ignore
    release ()
    let _, location = callback.GetAwaiter().GetResult()
    Assert.Equal("/index.html?chatgpt=cancelled", location)
    Assert.False(IO.File.Exists(recordPath harness))
    Assert.False(signedIn harness)


// ---- T007: summary through her plan ----

let answer = """{"points":[{"text":"Main idea","pages":[1,2]}]}"""
let sumSettings = [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ]

/// Sends api.openai.com and auth.openai.com requests to the OpenAI fake and everything else to the chat fake.
/// `onRequest` sees each request first (a handshake seam for tests).
type Router(openai: HttpMessageHandler, chat: HttpMessageHandler, onRequest: HttpRequestMessage -> unit) =
    inherit HttpMessageHandler()
    override _.SendAsync(request, cancellation) =
        onRequest request
        let target = if request.RequestUri.Host.EndsWith "openai.com" then openai else chat
        (new HttpMessageInvoker(target, false)).SendAsync(request, cancellation)

let tokenPath = "/api/accounts/oauth/token"

let seedProject directory =
    use db = LexScholar.Infrastructure.WorkspaceDb.create directory
    let time = DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero)
    let workspace = Guid.NewGuid()
    let project = Guid.NewGuid()
    db.Add(LexScholar.Infrastructure.WorkspaceRow(Id = workspace, CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    db.Add(LexScholar.Infrastructure.ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = "P", NormalizedName = "P", CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    project

let sendJson (harness: TestHarness) (path: string) (body: string) =
    use message = new HttpRequestMessage(HttpMethod.Post, path)
    message.Headers.TryAddWithoutValidation("Origin", harness.Client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token harness.Client) |> ignore
    message.Content <- new StringContent(body, Text.Encoding.UTF8, "application/json")
    use response = harness.Client.Send message
    response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

/// Imports and associates a summary fixture; returns the summary path.
let documentPathFor (harness: TestHarness) (fixture: string) =
    let project = seedProject harness.Root
    use form = new MultipartFormDataContent()
    let part = new ByteArrayContent(IO.File.ReadAllBytes(IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", fixture)))
    part.Headers.ContentType <- Headers.MediaTypeHeaderValue("application/pdf")
    form.Add(part, "file", fixture)
    use message = new HttpRequestMessage(HttpMethod.Post, "/api/documents")
    message.Headers.TryAddWithoutValidation("Origin", harness.Client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token harness.Client) |> ignore
    message.Content <- form
    use response = harness.Client.Send message
    let text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    use json = JsonDocument.Parse text
    let document = json.RootElement.GetProperty("id").GetGuid()
    let status, body = sendJson harness $"/api/projects/{project}/documents" (JsonSerializer.Serialize({| documentId = string document; role = "FocusedSource" |}))
    Assert.Equal(HttpStatusCode.Created, status)
    $"/api/projects/{project}/documents/{document}/summary"

/// Imports and associates article-en.pdf; returns the summary path.
let documentPath (harness: TestHarness) = documentPathFor harness "article-en.pdf"

let events (text: string) =
    text.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun line -> JsonDocument.Parse(line).RootElement.Clone())

let problemCode (text: string) =
    use json = JsonDocument.Parse text
    json.RootElement.GetProperty("code").GetString()

let openAiRequests (fake: FakeOpenAi) = fake.Requests |> List.filter (fun r -> r.Host = "api.openai.com")

let storedRecord (harness: TestHarness) =
    use json = JsonDocument.Parse(IO.File.ReadAllText(recordPath harness))
    json.RootElement.Clone()

let expectSummary (status: HttpStatusCode, text: string) =
    Assert.Equal(HttpStatusCode.OK, status)
    let event = Assert.Single(events text)
    Assert.Equal("summary", event.GetProperty("type").GetString())
    event

let confirmed = """{"confirmSend":true}"""

[<Fact; Trait("Task", "T007")>]
let ``signed in without confirmation is 409 and nothing at all reaches the OpenAI API`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInThrough harness fake "" |> ignore
    let status, text = sendJson harness path """{"confirmSend":false}"""
    Assert.Equal(HttpStatusCode.Conflict, status)
    use json = JsonDocument.Parse text
    Assert.Equal("confirmation_required", json.RootElement.GetProperty("code").GetString())
    Assert.Equal("chatgpt", json.RootElement.GetProperty("destination").GetString())
    Assert.Equal(fake.Email, json.RootElement.GetProperty("account").GetString())
    Assert.Empty(openAiRequests fake)
    Assert.False((storedRecord harness).TryGetProperty("confirmedAt") |> fst)
    // A confirmation that is not the boolean true is not one.
    for body in [ "{}"; """{"confirmSend":"true"}"""; """{"confirmSend":1}""" ] do
        Assert.Equal(HttpStatusCode.Conflict, fst (sendJson harness path body))
    Assert.Empty(openAiRequests fake)

[<Fact; Trait("Task", "T007")>]
let ``confirming sends the summary through the plan with the first listed model and keeps the confirmation`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInThrough harness fake "" |> ignore
    let event = expectSummary (sendJson harness path confirmed)
    Assert.Equal("ChatGPT plan", event.GetProperty("serviceHost").GetString())
    Assert.Equal("gpt-test", event.GetProperty("model").GetString())
    let point = event.GetProperty("points").[0]
    Assert.Equal("Main idea", point.GetProperty("text").GetString())
    Assert.Equal<int list>([ 1; 2 ], [ for p in point.GetProperty("pages").EnumerateArray() -> p.GetInt32() ])
    let responses = fake.RequestsTo("api.openai.com", "/v1/responses")
    let request = Assert.Single responses
    Assert.Equal(Some "Bearer access-token-1", request.Authorization)
    Assert.Contains("gpt-test", request.Body)
    Assert.True((storedRecord harness).TryGetProperty("confirmedAt") |> fst)
    // A second summary of the same sign-in needs no confirmation.
    expectSummary (sendJson harness path """{"confirmSend":false}""") |> ignore
    use destinations = destinations harness.Client
    Assert.True(destinations.RootElement.GetProperty("chatgpt").GetProperty("confirmed").GetBoolean())

[<Fact; Trait("Task", "T007")>]
let ``the confirmation survives a host restart`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    use workspace = TemporaryWorkspace.Create()
    let path =
        use first = TestHarness.Start(workspace = workspace, summaryHandler = fake)
        let path = documentPath first
        signInThrough first fake "" |> ignore
        expectSummary (sendJson first path confirmed) |> ignore
        path
    use second = TestHarness.Start(workspace = workspace, summaryHandler = fake)
    expectSummary (sendJson second path """{"confirmSend":false}""") |> ignore

[<Fact; Trait("Task", "T007")>]
let ``sign-out, then sign-in asks for the confirmation again`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInThrough harness fake "" |> ignore
    expectSummary (sendJson harness path confirmed) |> ignore
    signOut harness |> ignore
    let status, text = sendJson harness path confirmed
    Assert.Equal(HttpStatusCode.ServiceUnavailable, status)
    Assert.Equal("summary_service_not_configured", problemCode text)
    signInThrough harness fake "" |> ignore
    let status, text = sendJson harness path """{"confirmSend":false}"""
    Assert.Equal(HttpStatusCode.Conflict, status)
    Assert.Equal("confirmation_required", problemCode text)
    Assert.Single(fake.RequestsTo("api.openai.com", "/v1/responses")) |> ignore

[<Fact; Trait("Task", "T007")>]
let ``with a service configured, signing out gives chatgpt_signed_out for a named chatgpt`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    let chat = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = sumSettings, summaryHandler = new Router(fake, chat, ignore))
    let path = documentPath harness
    let status, text = sendJson harness path """{"destination":"chatgpt","confirmSend":true}"""
    Assert.Equal(HttpStatusCode.ServiceUnavailable, status)
    Assert.Equal("chatgpt_signed_out", problemCode text)
    Assert.Empty(fake.Requests)
    Assert.Empty(chat.Requests)

[<Fact; Trait("Task", "T007")>]
let ``both destinations without a valid destination is 400 destination_required and nothing is sent`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    let chat = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = sumSettings, summaryHandler = new Router(fake, chat, ignore))
    let path = documentPath harness
    signInThrough harness fake "" |> ignore
    let before = fake.Requests.Length
    for body in [ confirmed; """{"destination":"other","confirmSend":true}"""; """{"destination":1,"confirmSend":true}""" ] do
        let status, text = sendJson harness path body
        Assert.Equal(HttpStatusCode.BadRequest, status)
        Assert.Equal("destination_required", problemCode text)
    Assert.Equal(before, fake.Requests.Length)
    Assert.Empty(chat.Requests)

[<Fact; Trait("Task", "T007")>]
let ``with both available the named destination is used and configured keeps the F003 rule`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    let chat = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = sumSettings, summaryHandler = new Router(fake, chat, ignore))
    let path = documentPath harness
    signInThrough harness fake "" |> ignore
    let configured = expectSummary (sendJson harness path """{"destination":"configured","confirmSend":false}""")
    Assert.Equal("127.0.0.1:9", configured.GetProperty("serviceHost").GetString())
    Assert.Single chat.Requests |> ignore
    Assert.Empty(openAiRequests fake)
    let plan = expectSummary (sendJson harness path """{"destination":"chatgpt","confirmSend":true}""")
    Assert.Equal("ChatGPT plan", plan.GetProperty("serviceHost").GetString())
    Assert.Single chat.Requests |> ignore

[<Fact; Trait("Task", "T007")>]
let ``the destinations configured block is unchanged while signed in`` () =
    let fake = new FakeOpenAi()
    use harness = TestHarness.Start(settings = sumSettings, summaryHandler = fake)
    signInThrough harness fake "" |> ignore
    use json = destinations harness.Client
    let configured = json.RootElement.GetProperty("configured")
    Assert.Equal("127.0.0.1:9", configured.GetProperty("serviceHost").GetString())
    Assert.Equal("m", configured.GetProperty("model").GetString())
    Assert.True(configured.GetProperty("local").GetBoolean())

/// Signs in with an access token that expires within ten minutes, then readies the fake's refresh answer.
let signInExpiring (harness: TestHarness) (fake: FakeOpenAi) =
    fake.ExpiresIn <- 300
    signInThrough harness fake "" |> ignore
    fake.ExpiresIn <- 3600
    fake.AccessToken <- "access-token-2"
    fake.RefreshToken <- "refresh-token-2"

[<Fact; Trait("Task", "T007")>]
let ``a token expiring within ten minutes is refreshed once and the rotated refresh token is stored`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInExpiring harness fake
    expectSummary (sendJson harness path confirmed) |> ignore
    let tokenRequests = fake.RequestsTo("auth.openai.com", tokenPath)
    Assert.Equal(2, tokenRequests.Length)
    Assert.Contains("grant_type=refresh_token", tokenRequests[1].Body)
    Assert.Contains("refresh-token-1", tokenRequests[1].Body)
    let request = Assert.Single(fake.RequestsTo("api.openai.com", "/v1/responses"))
    Assert.Equal(Some "Bearer access-token-2", request.Authorization)
    let record = storedRecord harness
    Assert.Equal("refresh-token-2", record.GetProperty("refreshToken").GetString())
    Assert.Equal("access-token-2", record.GetProperty("accessToken").GetString())
    Assert.True(record.TryGetProperty("confirmedAt") |> fst)
    // The new token is good for an hour: the next summary does not refresh again.
    expectSummary (sendJson harness path confirmed) |> ignore
    Assert.Equal(2, fake.RequestsTo("auth.openai.com", tokenPath).Length)

[<Fact; Trait("Task", "T007")>]
let ``an unconfirmed request does not refresh or touch the network`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInExpiring harness fake
    let before = fake.Requests.Length
    Assert.Equal(HttpStatusCode.Conflict, fst (sendJson harness path """{"confirmSend":false}"""))
    Assert.Equal(before, fake.Requests.Length)
    Assert.Equal("refresh-token-1", (storedRecord harness).GetProperty("refreshToken").GetString())

[<Theory; Trait("Task", "T007")>]
[<InlineData(503, "")>]
[<InlineData(400, "unknown_error")>]
[<InlineData(401, "")>]
let ``a refresh that fails without a terminal code keeps the record and is a retryable failure`` (status: int, code: string) =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInExpiring harness fake
    fake.TokenFailure <- Some(status, (if code = "" then "{}" else $"{{\"error\":\"{code}\"}}"))
    let status, text = sendJson harness path confirmed
    Assert.Equal(HttpStatusCode.OK, status)
    let event = Assert.Single(events text)
    Assert.Equal("failed", event.GetProperty("type").GetString())
    Assert.Equal("service_failed", event.GetProperty("code").GetString())
    Assert.Empty(openAiRequests fake)
    let record = storedRecord harness
    Assert.Equal("refresh-token-1", record.GetProperty("refreshToken").GetString())
    Assert.False(record.TryGetProperty("confirmedAt") |> fst)
    Assert.True(signedIn harness)

[<Fact; Trait("Task", "T007")>]
let ``a terminal refresh error deletes the record and signs her out`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInExpiring harness fake
    fake.TokenFailure <- Some(400, """{"error":"invalid_grant"}""")
    let status, text = sendJson harness path confirmed
    // T008: the run ends with a `failed` event (T010's client treats it like the `chatgpt_signed_out` problem).
    Assert.Equal(HttpStatusCode.OK, status)
    Assert.Equal("signed_out", (Assert.Single(events text)).GetProperty("code").GetString())
    Assert.False(IO.File.Exists(recordPath harness))
    Assert.False(signedIn harness)
    Assert.Empty(openAiRequests fake)

[<Fact; Trait("Task", "T007")>]
let ``a model list that cannot be used fails the run and sends no text`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInThrough harness fake "" |> ignore
    fake.Models <- [ "gpt-hidden", "hide" ]
    let status, text = sendJson harness path confirmed
    Assert.Equal(HttpStatusCode.OK, status)
    let event = Assert.Single(events text)
    Assert.Equal("service_failed", event.GetProperty("code").GetString())
    Assert.Empty(fake.RequestsTo("api.openai.com", "/v1/responses"))

[<Fact; Trait("Task", "T007")>]
let ``a refresh in flight when she signs out leaves her signed out with no record`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    let refreshArrived = Threading.Tasks.TaskCompletionSource()
    let signedInYet = ref false
    let tap (request: HttpRequestMessage) =
        if signedInYet.Value && request.RequestUri.AbsolutePath = tokenPath then refreshArrived.TrySetResult() |> ignore
    use harness = TestHarness.Start(summaryHandler = new Router(fake, new FakeChatService(Reply answer), tap))
    let path = documentPath harness
    signInExpiring harness fake
    signedInYet.Value <- true
    let release = fake.HoldTokens()
    let summary = Threading.Tasks.Task.Run(fun () -> sendJson harness path confirmed)
    refreshArrived.Task.WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
    // The refresh holds the vault lock, so this sign-out can only run after it finishes.
    // Whether or not this request has reached the vault by the time the refresh is released, the
    // lock gives one outcome: sign-out runs after the refresh saved, and deletes what it saved.
    let signOutRun = Threading.Tasks.Task.Run(fun () -> signOut harness)
    release ()
    summary.GetAwaiter().GetResult() |> ignore
    signOutRun.GetAwaiter().GetResult() |> ignore
    Assert.False(IO.File.Exists(recordPath harness))
    Assert.False(signedIn harness)

[<Fact; Trait("Task", "T007")>]
let ``a sign-in whose record cannot be saved fails and revokes the issued tokens`` () =
    let fake = new FakeOpenAi()
    use harness = TestHarness.Start(summaryHandler = fake)
    // A directory where the record belongs makes the save throw.
    IO.Directory.CreateDirectory(recordPath harness) |> ignore
    let _, location = signInThrough harness fake ""
    Assert.Equal("/index.html?chatgpt=failed", location)
    Assert.Single(fake.RequestsTo("auth.openai.com", "/api/accounts/oauth/revoke")) |> ignore

[<Fact; Trait("Task", "T007")>]
let ``a summary naming the configured service does not wait for a refresh in flight`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    let refreshArrived = Threading.Tasks.TaskCompletionSource()
    let signedInYet = ref false
    let tap (request: HttpRequestMessage) =
        if signedInYet.Value && request.RequestUri.AbsolutePath = tokenPath then refreshArrived.TrySetResult() |> ignore
    use harness = TestHarness.Start(settings = sumSettings, summaryHandler = new Router(fake, new FakeChatService(Reply answer), tap))
    let path = documentPath harness
    signInExpiring harness fake
    signedInYet.Value <- true
    let release = fake.HoldTokens()
    let viaPlan = Threading.Tasks.Task.Run(fun () -> sendJson harness path """{"destination":"chatgpt","confirmSend":true}""")
    refreshArrived.Task.WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
    // The refresh holds the vault lock; the local summary must finish while it is held.
    let local = Threading.Tasks.Task.Run(fun () -> sendJson harness path """{"destination":"configured","confirmSend":false}""")
    expectSummary (local.WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()) |> ignore
    release ()
    expectSummary (viaPlan.GetAwaiter().GetResult()) |> ignore


// ---- T008: failure states ----

let failedCode (text: string) =
    let event = Assert.Single(events text)
    Assert.Equal("failed", event.GetProperty("type").GetString())
    event.GetProperty("code").GetString()

let expectSignedOutEverywhere (harness: TestHarness) =
    Assert.False(IO.File.Exists(recordPath harness))
    Assert.False(signedIn harness)

[<Fact; Trait("Task", "T008")>]
let ``a usage limit before the stream is usage_limit_reached and keeps the sign-in`` () =
    let fake = new FakeOpenAi(ResponsesScript.Status(429, "subscription_sharing_usage_limit_exceeded"))
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInThrough harness fake "" |> ignore
    let status, text = sendJson harness path confirmed
    Assert.Equal(HttpStatusCode.OK, status)
    Assert.Equal("usage_limit_reached", failedCode text)
    Assert.True(signedIn harness)

[<Fact; Trait("Task", "T008")>]
let ``a usage limit as response.failed mid-document gives no partial summary`` () =
    let script n =
        if n = 1 then ResponsesScript.Completed """{"points":[{"text":"Main idea","pages":[1]}]}"""
        else ResponsesScript.Failed "subscription_sharing_usage_limit_exceeded"
    let fake = new FakeOpenAi(script)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPathFor harness "long-40.pdf"
    signInThrough harness fake "" |> ignore
    let status, text = sendJson harness path confirmed
    Assert.Equal(HttpStatusCode.OK, status)
    let all = events text
    Assert.DoesNotContain(all, fun e -> e.GetProperty("type").GetString() = "summary")
    let last = Array.last all
    Assert.Equal("failed", last.GetProperty("type").GetString())
    Assert.Equal("usage_limit_reached", last.GetProperty("code").GetString())
    Assert.Equal(2, fake.RequestsTo("api.openai.com", "/v1/responses").Length)
    Assert.True(signedIn harness)

[<Theory; Trait("Task", "T008")>]
[<InlineData(401, "subscription_sharing_invalid_user", "signed_out")>]
[<InlineData(403, "subscription_sharing_user_not_eligible", "no_plan_access")>]
let ``a refused plan deletes the record, ends the run and signs her out`` (status: int, code: string, expected: string) =
    let fake = new FakeOpenAi(ResponsesScript.Status(status, code))
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInThrough harness fake "" |> ignore
    let _, text = sendJson harness path confirmed
    Assert.Equal(expected, failedCode text)
    expectSignedOutEverywhere harness

[<Fact; Trait("Task", "T008")>]
let ``a refusal as response.failed mid-stream also signs her out`` () =
    let fake = new FakeOpenAi(ResponsesScript.Failed "subscription_sharing_invalid_user")
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInThrough harness fake "" |> ignore
    let _, text = sendJson harness path confirmed
    Assert.Equal("signed_out", failedCode text)
    expectSignedOutEverywhere harness

[<Fact; Trait("Task", "T008")>]
let ``signing out while the fake waits ends the request with signed_out and sends nothing more`` () =
    let fake = new FakeOpenAi(ResponsesScript.WaitUntilCancelled)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPathFor harness "long-40.pdf"
    signInThrough harness fake "" |> ignore
    let summary = Threading.Tasks.Task.Run(fun () -> sendJson harness path confirmed)
    fake.Waiting.WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
    signOut harness |> ignore
    let status, text = summary.WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
    fake.Cancelled.WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, status)
    Assert.Equal("signed_out", failedCode text)
    Assert.Equal(1, fake.RequestsTo("api.openai.com", "/v1/responses").Length)
    expectSignedOutEverywhere harness
