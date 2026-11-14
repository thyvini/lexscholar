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
type private CapturingProvider() =
    let lines = System.Collections.Concurrent.ConcurrentQueue<string>()
    member _.Lines = lines |> Seq.toList
    interface ILoggerProvider with
        member _.CreateLogger(_category) =
            { new ILogger with
                member _.BeginScope(_state) = null
                member _.IsEnabled(_level) = true
                member _.Log(_level, _eventId, state, error, formatter) = lines.Enqueue(formatter.Invoke(state, error)) }
        member _.Dispose() = ()

let private token (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    json.RootElement.GetProperty("requestToken").GetString()

/// POST with the Origin header and, when `csrf` is given, the antiforgery token.
let private post (client: HttpClient) (path: string) (csrf: string option) =
    use message = new HttpRequestMessage(HttpMethod.Post, path)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    csrf |> Option.iter (fun value -> message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", value) |> ignore)
    message.Content <- new StringContent("{}", Text.Encoding.UTF8, "application/json")
    use response = client.Send(message)
    response.StatusCode

let private destinations (client: HttpClient) =
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

let private workspaceRoot (harness: TestHarness) = harness.Root

/// A client that does not follow redirects, for the callback.
let private plainClient (harness: TestHarness) = new HttpClient(new HttpClientHandler(AllowAutoRedirect = false), true, BaseAddress = harness.Client.BaseAddress)

let private startSignIn (harness: TestHarness) =
    use message = new HttpRequestMessage(HttpMethod.Post, "/api/chatgpt/sign-in")
    message.Headers.TryAddWithoutValidation("Origin", harness.Client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token harness.Client) |> ignore
    message.Content <- new StringContent("{}", Text.Encoding.UTF8, "application/json")
    use response = harness.Client.Send message
    let body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    response.StatusCode, body

let private urlOf (body: string) =
    use json = JsonDocument.Parse body
    Uri(json.RootElement.GetProperty("authorizationUrl").GetString())

let private query (url: Uri) =
    url.Query.TrimStart('?').Split('&') |> Array.map (fun p -> let parts = p.Split('=', 2) in parts[0], Uri.UnescapeDataString parts[1]) |> dict

let private callbackAt (harness: TestHarness) (path: string) =
    use client = plainClient harness
    use response = client.GetAsync(path).GetAwaiter().GetResult()
    response.StatusCode, (if isNull response.Headers.Location then "" else response.Headers.Location.ToString())

/// Starts a sign-in and returns the callback path the browser would reach with these extras.
let private callbackPath (url: Uri) (extra: string) =
    let state = Uri.EscapeDataString((query url).["state"])
    $"/auth/callback?state={state}&code=code-1{extra}"

let private signedIn (harness: TestHarness) =
    use json = destinations harness.Client
    json.RootElement.GetProperty("chatgpt").GetProperty("signedIn").GetBoolean()

let private recordPath (harness: TestHarness) = IO.Path.Combine(workspaceRoot harness, "chatgpt-signin.json")

let private signInThrough (harness: TestHarness) (fake: FakeOpenAi) (extra: string) =
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

let private postBody (harness: TestHarness) (path: string) =
    use message = new HttpRequestMessage(HttpMethod.Post, path)
    message.Headers.TryAddWithoutValidation("Origin", harness.Client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token harness.Client) |> ignore
    message.Content <- new StringContent("{}", Text.Encoding.UTF8, "application/json")
    use response = harness.Client.Send message
    response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

let private signOut (harness: TestHarness) =
    let status, body = postBody harness "/api/chatgpt/sign-out"
    Assert.Equal(HttpStatusCode.OK, status)
    use json = JsonDocument.Parse body
    json.RootElement.GetProperty("revoked").GetBoolean()

let private dismissWelcome (harness: TestHarness) =
    Assert.Equal(HttpStatusCode.NoContent, fst (postBody harness "/api/chatgpt/welcome/dismiss"))

let private welcomePending (harness: TestHarness) =
    use json = destinations harness.Client
    json.RootElement.GetProperty("welcomePending").GetBoolean()

let private marker (harness: TestHarness) = IO.Path.Combine(workspaceRoot harness, "chatgpt-welcome-shown")

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
