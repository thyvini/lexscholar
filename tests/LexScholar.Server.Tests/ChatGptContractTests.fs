/// HTTP contract of the F005 sign-in routes (contracts/chatgpt-api.yaml), shared pieces only (T001).
module LexScholar.Server.Tests.ChatGptContractTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Xunit

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
[<InlineData("/api/chatgpt/sign-in")>]
[<InlineData("/api/chatgpt/sign-out")>]
[<InlineData("/api/chatgpt/welcome/dismiss")>]
let ``unsafe chatgpt routes need the antiforgery token and are not implemented yet`` (path: string) =
    use harness = TestHarness.Start()
    Assert.Equal(HttpStatusCode.Forbidden, post harness.Client path None)
    Assert.Equal(HttpStatusCode.NotImplemented, post harness.Client path (Some (token harness.Client)))

[<Fact; Trait("Task", "T001")>]
let ``the callback route exists`` () =
    use harness = TestHarness.Start()
    use response = harness.Client.GetAsync("/auth/callback?error=access_denied").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)

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
