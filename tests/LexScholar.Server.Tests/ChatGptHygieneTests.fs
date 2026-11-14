/// Credential hygiene of the F005 flows (T008, FR-005, FR-008, FR-009, FR-011, FR-012, SC-002, SC-005, SC-006):
/// no secret reaches a log line, a response of this API, or a file left after sign-out.
module LexScholar.Server.Tests.ChatGptHygieneTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open LexScholar.Server
open LexScholar.Server.Tests.ChatGptContractTests
open Xunit

/// Collects every formatted log line (with its exception text).
type private Lines() =
    let lines = System.Collections.Concurrent.ConcurrentQueue<string>()
    member _.All = lines |> Seq.toList
    interface ILoggerProvider with
        member _.CreateLogger(_category) =
            { new ILogger with
                member _.BeginScope(_state) = null
                member _.IsEnabled(_level) = true
                member _.Log(_level, _eventId, state, error, formatter) =
                    lines.Enqueue(formatter.Invoke(state, error) + (if isNull error then "" else " " + string error)) }
        member _.Dispose() = ()

/// What the API answered, as one searchable text: status, every header, the body.
let private observed = System.Collections.Concurrent.ConcurrentQueue<string * string>()

let private observe (what: string) (response: HttpResponseMessage) (body: string) =
    let all =
        [ for h in response.Headers -> $"{h.Key}: {String.Join(',', h.Value)}"
          for h in response.Content.Headers -> $"{h.Key}: {String.Join(',', h.Value)}" ]
    observed.Enqueue(what, $"{int response.StatusCode}\n{String.Join('\n', all)}\n{body}")

let private call (harness: TestHarness) (what: string) (method': HttpMethod) (path: string) (body: string) =
    use message = new HttpRequestMessage(method', path)
    message.Headers.TryAddWithoutValidation("Origin", harness.Client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    if method' = HttpMethod.Post then
        message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token harness.Client) |> ignore
        message.Content <- new StringContent(body, Text.Encoding.UTF8, "application/json")
    use response = harness.Client.Send message
    let text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    observe what response text
    response.StatusCode, text

let private callbackObserved (harness: TestHarness) (path: string) =
    use client = plainClient harness
    use response = client.GetAsync(path).GetAwaiter().GetResult()
    observe "callback" response (response.Content.ReadAsStringAsync().GetAwaiter().GetResult())

let private answerJson = """{"points":[{"text":"Main idea","pages":[1,2]}]}"""

[<Fact; Trait("Task", "T008")>]
let ``no secret reaches a log line, a response or a file in any flow`` () =
    observed.Clear()
    let lines = new Lines()
    // Call 1 succeeds, 2 hits the usage limit, 3 is refused (ended sign-in), later ones succeed.
    let script n =
        match n with
        | 2 -> ResponsesScript.Status(429, "subscription_sharing_usage_limit_exceeded")
        | 3 -> ResponsesScript.Status(401, "subscription_sharing_invalid_user")
        | _ -> ResponsesScript.Completed answerJson
    let fake = new FakeOpenAi(script)
    fake.Email <- "hygiene.researcher@example.test"
    fake.AccessToken <- "ACCESSSECRET-A"
    fake.RefreshToken <- "REFRESHSECRET-A"
    use harness =
        TestHarness.Start(
            summaryHandler = fake,
            configure = fun host -> host.Services.GetRequiredService<ILoggerFactory>().AddProvider lines)
    let path = documentPath harness
    // Sign in (the access token expires within ten minutes, so the first summary refreshes it).
    fake.ExpiresIn <- 300
    let _, start = call harness "start" HttpMethod.Post "/api/chatgpt/sign-in" "{}"
    let url = urlOf start
    fake.Nonce <- (query url)["nonce"]
    let state = (query url)["state"]
    let nonce = (query url)["nonce"]
    callbackObserved harness (callbackPath url $"&client_id={fake.ClientId}")
    fake.ExpiresIn <- 3600
    fake.AccessToken <- "ACCESSSECRET-B"
    fake.RefreshToken <- "REFRESHSECRET-B"
    // Summary with confirmation and refresh, then the usage limit, then an ended sign-in.
    call harness "summary" HttpMethod.Post path confirmed |> ignore
    call harness "summary-limit" HttpMethod.Post path confirmed |> ignore
    call harness "summary-ended" HttpMethod.Post path confirmed |> ignore
    call harness "destinations" HttpMethod.Get "/api/summary/destinations" "" |> ignore
    // A failed sign-in.
    fake.TokenFailure <- Some(400, """{"error":"invalid_grant","error_description":"CODESECRET"}""")
    let _, start2 = call harness "start" HttpMethod.Post "/api/chatgpt/sign-in" "{}"
    let url2 = urlOf start2
    callbackObserved harness (callbackPath url2 $"&client_id={fake.ClientId}")
    fake.TokenFailure <- None
    // Sign in again and sign out.
    fake.AccessToken <- "ACCESSSECRET-C"
    fake.RefreshToken <- "REFRESHSECRET-C"
    let _, start3 = call harness "start" HttpMethod.Post "/api/chatgpt/sign-in" "{}"
    let url3 = urlOf start3
    fake.Nonce <- (query url3)["nonce"]
    callbackObserved harness (callbackPath url3 $"&client_id={fake.ClientId}")
    let verifiers =
        fake.Requests
        |> List.choose (fun r ->
            r.Body.Split('&') |> Array.tryPick (fun part -> if part.StartsWith "code_verifier=" then Some(Uri.UnescapeDataString(part.Substring 14)) else None))
    Assert.NotEmpty verifiers
    call harness "destinations" HttpMethod.Get "/api/summary/destinations" "" |> ignore
    call harness "sign-out" HttpMethod.Post "/api/chatgpt/sign-out" "{}" |> ignore
    call harness "sign-out again" HttpMethod.Post "/api/chatgpt/sign-out" "{}" |> ignore

    let tokens =
        [ for letter in [ "A"; "B"; "C" ] do
            yield $"ACCESSSECRET-{letter}"
            yield $"REFRESHSECRET-{letter}" ]
    let secrets = tokens @ [ "code-1"; "CODESECRET" ] @ verifiers
    let idTokenMarker = "eyJ" // every JWT starts with this
    let logged = String.Join('\n', lines.All)
    Assert.NotEmpty lines.All
    for secret in secrets @ [ state; nonce; fake.Email; idTokenMarker ] do
        Assert.DoesNotContain(secret, logged)
    // Responses of this API: tokens, code and verifier never; state and nonce only in the start response's address; the
    // account display name only in the `account` field.
    for what, text in observed do
        for secret in secrets @ [ idTokenMarker ] do
            Assert.False(text.Contains secret, $"{what} response contains a secret")
        if what <> "start" then
            Assert.False(text.Contains state, $"{what} response contains the state")
            Assert.False(text.Contains nonce, $"{what} response contains the nonce")
        if what = "destinations" then
            use json = JsonDocument.Parse(text.Substring(text.IndexOf('{')))
            let chatgpt = json.RootElement.GetProperty "chatgpt"
            if chatgpt.GetProperty("signedIn").GetBoolean() then
                Assert.Equal(fake.Email, chatgpt.GetProperty("account").GetString())
        elif what <> "start" then
            Assert.False(text.Contains fake.Email, $"{what} response contains the account")
    // After sign-out no file in the data directory holds the account or a token.
    Assert.False(IO.File.Exists(recordPath harness))
    for file in IO.Directory.EnumerateFiles(harness.Root, "*", IO.SearchOption.AllDirectories) do
        let bytes = IO.File.ReadAllBytes file
        let content = Text.Encoding.Latin1.GetString bytes
        for secret in tokens @ [ fake.Email ] do
            Assert.False(content.Contains secret, $"{IO.Path.GetFileName file} contains a secret")

[<Fact; Trait("Task", "T008")>]
let ``the working tree contains no chatgpt-signin.json`` () =
    let rec findRoot (directory: string) =
        if IO.Directory.Exists(IO.Path.Combine(directory, ".git")) || IO.File.Exists(IO.Path.Combine(directory, ".git")) then directory
        else findRoot (IO.Path.GetDirectoryName directory)
    let root = findRoot AppContext.BaseDirectory
    let options = IO.EnumerationOptions(RecurseSubdirectories = true, IgnoreInaccessible = true)
    let found =
        IO.Directory.EnumerateFiles(root, "chatgpt-signin.json", options)
        |> Seq.filter (fun file -> not (file.Contains "/.git/"))
        |> Seq.toList
    Assert.Empty found
