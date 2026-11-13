/// Guards on the outgoing connection to the summary service and log hygiene
/// (F003 T009, research.md sections 1 and 2): loopback-only connects for a local
/// service, no proxy, no redirects, and logs without text, prompt, output or key.
module LexScholar.Infrastructure.Tests.ChatCompletionsTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Infrastructure
open Microsoft.Extensions.Logging
open Xunit

/// A loopback server that answers every request with one fixed raw HTTP response and counts connections.
type private FixedServer(response: string) =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    let mutable accepted = 0
    do listener.Start()
    let loop =
        task {
            try
                while true do
                    use! client = listener.AcceptTcpClientAsync()
                    Interlocked.Increment(&accepted) |> ignore
                    let stream = client.GetStream()
                    let buffer = Array.zeroCreate<byte> 65536
                    let mutable read = 0
                    let mutable text = ""
                    while not (text.Contains "\r\n\r\n") do
                        let! n = stream.ReadAsync(buffer, 0, buffer.Length)
                        read <- n
                        text <- text + Encoding.UTF8.GetString(buffer, 0, n)
                    let bytes = Encoding.UTF8.GetBytes response
                    do! stream.WriteAsync(bytes, 0, bytes.Length)
            with _ -> () }
    member _.Loop = loop
    member _.Port = (listener.LocalEndpoint :?> IPEndPoint).Port
    member _.Accepted = Volatile.Read(&accepted)
    interface IDisposable with
        member _.Dispose() = listener.Stop()

let private ok (content: string) =
    let body = $"{{\"choices\":[{{\"message\":{{\"content\":\"{content}\"}}}}]}}"
    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount body}\r\nConnection: close\r\n\r\n{body}"

let private settings (endpoint: string) key : ChatCompletions.Settings =
    { Endpoint = Uri endpoint; Model = "test-model"; ApiKey = key; Temperature = None; MaxOutputTokens = None }

let private messages text : Summarize.ChatMessage list = [ { Role = "user"; Content = text } ]

type private CapturingLogger() =
    member val Lines = List<string>()
    interface ILogger with
        member _.BeginScope _ = null
        member _.IsEnabled _ = true
        member this.Log(_, _, state, error, formatter) =
            lock this.Lines (fun () ->
                this.Lines.Add(formatter.Invoke(state, error) + (if isNull error then "" else error.ToString())))

let private complete handler logger endpoint key text =
    use client = new HttpClient(handler, true)
    (ChatCompletions.complete logger client (settings endpoint key) (messages text) CancellationToken.None).GetAwaiter().GetResult()

let private resolvesTo (addresses: string list) =
    fun (_: string) (_: CancellationToken) -> Task.FromResult(addresses |> List.map IPAddress.Parse |> Array.ofList)

[<Fact; Trait("Task", "T009")>]
let ``connect guard refuses a name that resolves outside loopback`` () =
    use server = new FixedServer(ok "x")
    let endpoint = $"http://localhost:{server.Port}/v1"
    let handler = ChatCompletions.createHandlerWith (resolvesTo [ "192.168.1.5" ]) (Uri endpoint)
    let result = complete handler (CapturingLogger()) endpoint None "hello"
    Assert.Equal(Error Summarize.ServiceError.Unreachable, result)
    Assert.Equal(0, server.Accepted)

[<Fact; Trait("Task", "T009")>]
let ``connect guard refuses a name that resolves to loopback and elsewhere`` () =
    use server = new FixedServer(ok "x")
    let endpoint = $"http://localhost:{server.Port}/v1"
    let handler = ChatCompletions.createHandlerWith (resolvesTo [ "127.0.0.1"; "10.0.0.2" ]) (Uri endpoint)
    Assert.Equal(Error Summarize.ServiceError.Unreachable, complete handler (CapturingLogger()) endpoint None "hello")
    Assert.Equal(0, server.Accepted)

[<Fact; Trait("Task", "T009")>]
let ``a local service is reached through the guard when the name resolves to loopback`` () =
    use server = new FixedServer(ok "fine")
    let endpoint = $"http://localhost:{server.Port}/v1"
    let handler = ChatCompletions.createHandlerWith (resolvesTo [ "127.0.0.1" ]) (Uri endpoint)
    Assert.Equal(Ok "fine", complete handler (CapturingLogger()) endpoint None "hello")

[<Fact; Trait("Task", "T009")>]
let ``a local service ignores environment proxies and a remote one keeps the default`` () =
    let local = ChatCompletions.createHandler (Uri "http://127.0.0.1:11434/v1")
    let ipv6 = ChatCompletions.createHandler (Uri "http://[::1]:8080/v1")
    let remote = ChatCompletions.createHandler (Uri "https://api.example.com/v1")
    Assert.False local.UseProxy
    Assert.False ipv6.UseProxy
    Assert.True remote.UseProxy
    Assert.Null remote.ConnectCallback
    Assert.NotNull local.ConnectCallback
    for handler in [ local; ipv6; remote ] do Assert.False handler.AllowAutoRedirect

[<Fact; Trait("Task", "T009")>]
let ``a redirect is a failure and its target is not contacted`` () =
    use target = new FixedServer(ok "followed")
    let targetPort = target.Port
    use server =
        new FixedServer($"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{targetPort}/elsewhere\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")
    let endpoint = $"http://127.0.0.1:{server.Port}/v1"
    let result = complete (ChatCompletions.createHandler (Uri endpoint)) (CapturingLogger()) endpoint None "hello"
    Assert.Equal(Error Summarize.ServiceError.Redirected, result)
    Assert.Equal(0, target.Accepted)

[<Fact; Trait("Task", "T009")>]
let ``logs name host, model and message count but no text, prompt, output or key`` () =
    let prompt = "PROMPT-SENTINEL-4471"
    let output = "OUTPUT-SENTINEL-9023"
    let key = "KEY-SENTINEL-5518"
    use server = new FixedServer(ok output)
    let endpoint = $"http://127.0.0.1:{server.Port}/v1"
    let logger = CapturingLogger()
    let result = complete (ChatCompletions.createHandler (Uri endpoint)) logger endpoint (Some key) prompt
    Assert.Equal(Ok output, result)
    let logged = String.Join("\n", logger.Lines)
    Assert.Contains("127.0.0.1", logged)
    Assert.Contains("test-model", logged)
    Assert.Contains("1 messages", logged)
    for secret in [ prompt; output; key; "/chat/completions" ] do Assert.DoesNotContain(secret, logged)

[<Fact; Trait("Task", "T009")>]
let ``a local service is reached on a later loopback address when the first refuses`` () =
    use server = new FixedServer(ok "fine")
    let endpoint = $"http://localhost:{server.Port}/v1"
    // Nothing listens on ::1 at this port, so the connect falls through to 127.0.0.1.
    let handler = ChatCompletions.createHandlerWith (resolvesTo [ "::1"; "127.0.0.1" ]) (Uri endpoint)
    Assert.Equal(Ok "fine", complete handler (CapturingLogger()) endpoint None "hello")

/// Sends valid headers, then stalls without sending the promised body until the connection closes.
type private StallingBodyServer() =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    let stop = new CancellationTokenSource()
    do listener.Start()
    let loop =
        task {
            try
                use! client = listener.AcceptTcpClientAsync(stop.Token)
                let stream = client.GetStream()
                let buffer = Array.zeroCreate<byte> 65536
                let mutable text = ""
                while not (text.Contains "\r\n\r\n") do
                    let! n = stream.ReadAsync(buffer, 0, buffer.Length, stop.Token)
                    text <- text + Encoding.UTF8.GetString(buffer, 0, n)
                let head = Encoding.UTF8.GetBytes "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 500\r\n\r\n{\"choices\""
                do! stream.WriteAsync(head, 0, head.Length, stop.Token)
                do! Task.Delay(Timeout.Infinite, stop.Token)
            with _ -> () }
    member _.Port = (listener.LocalEndpoint :?> IPEndPoint).Port
    member _.Loop = loop
    interface IDisposable with
        // Ends the stalled connection so the accepted socket is closed with the fixture.
        member _.Dispose() =
            // Cancelling ends the accept, the header read and the stall, whatever the client did.
            stop.Cancel()
            listener.Stop()
            loop.GetAwaiter().GetResult()
            stop.Dispose()

[<Fact; Trait("Task", "T010")>]
let ``a service that stalls after its headers times out instead of leaving the request pending`` () =
    use server = new StallingBodyServer()
    let endpoint = $"http://127.0.0.1:{server.Port}/v1"
    use client = new HttpClient(ChatCompletions.createHandler (Uri endpoint), true, Timeout = TimeSpan.FromMilliseconds 300.0)
    let call = ChatCompletions.complete (CapturingLogger()) client (settings endpoint None) (messages "hello") CancellationToken.None
    // A guard so the RED run fails on the assertion instead of hanging.
    let finished = Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds 10.0)).GetAwaiter().GetResult()
    Assert.True(obj.ReferenceEquals(finished, call), "the call is still pending after its timeout")
    Assert.Equal(Error Summarize.ServiceError.TimedOut, call.GetAwaiter().GetResult())

[<Fact; Trait("Task", "T010")>]
let ``a caller's cancellation while the body stalls is a cancellation, not a timeout`` () =
    use server = new StallingBodyServer()
    let endpoint = $"http://127.0.0.1:{server.Port}/v1"
    use client = new HttpClient(ChatCompletions.createHandler (Uri endpoint), true, Timeout = TimeSpan.FromSeconds 30.0)
    use source = new CancellationTokenSource(TimeSpan.FromMilliseconds 300.0)
    let call = ChatCompletions.complete (CapturingLogger()) client (settings endpoint None) (messages "hello") source.Token
    let finished = Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds 10.0)).GetAwaiter().GetResult()
    Assert.True(obj.ReferenceEquals(finished, call), "the call is still pending after cancellation")
    Assert.True(call.IsCanceled, "an abandoned call is cancelled, not reported as a failure")

let private refusal (status: string) (body: string) =
    $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount body}\r\nConnection: close\r\n\r\n{body}"

let private errorBody (message: string) =
    System.Text.Json.JsonSerializer.Serialize({| error = {| message = message; ``type`` = "invalid_request_error"; code = "secret-code-77"; param = "secret-param-88" |} |})

/// The detail the researcher would see after the service answers with this raw response.
let private detailFor (response: string) (key: string option) (logger: ILogger) =
    use server = new FixedServer(response)
    let endpoint = $"http://127.0.0.1:{server.Port}/v1"
    match complete (ChatCompletions.createHandler (Uri endpoint)) logger endpoint key "PROMPT-SENTINEL-1" with
    | Error error -> Summarize.failureDetail error
    | Ok _ -> failwith "expected a failure"

[<Fact; Trait("Task", "T013")>]
let ``only error.message is taken, as short plain single-line text`` () =
    let detail = detailFor (refusal "400 Bad Request" (errorBody "Your credit balance is too low")) None (CapturingLogger())
    Assert.Contains("Your credit balance is too low", detail)
    for other in [ "secret-code-77"; "secret-param-88"; "invalid_request_error" ] do Assert.DoesNotContain(other, detail)
    // Multi-line, control characters and HTML-looking text become one line of plain characters.
    let messy = detailFor (refusal "400 Bad Request" (errorBody "line one\r\nline\ttwo\u0007\u001b[31m <b>bold</b>")) None (CapturingLogger())
    Assert.DoesNotContain("\n", messy)
    Assert.DoesNotContain("\r", messy)
    Assert.DoesNotContain("\t", messy)
    Assert.DoesNotContain("\u0007", messy)
    Assert.DoesNotContain("\u001b", messy)
    Assert.Contains("line one line two", messy)
    Assert.Contains("<b>bold</b>", messy) // shown literally: the client renders plain text

[<Fact; Trait("Task", "T013")>]
let ``an oversized message is capped and an oversized body gives no message`` () =
    let capped = detailFor (refusal "400 Bad Request" (errorBody (String('m', 5000)))) None (CapturingLogger())
    Assert.Contains(String('m', 300), capped)
    Assert.DoesNotContain(String('m', 301), capped)
    let huge = detailFor (refusal "400 Bad Request" (errorBody (String('m', 1_100_000)))) None (CapturingLogger())
    Assert.DoesNotContain("mmmm", huge)
    Assert.Contains("MaxOutputTokens", huge)

[<Fact; Trait("Task", "T013")>]
let ``a message that echoes the key or the prompt never shows the key`` () =
    let key = "KEY-SENTINEL-6602"
    let detail = detailFor (refusal "401 Unauthorized" (errorBody $"Incorrect API key provided: {key}.")) (Some key) (CapturingLogger())
    Assert.DoesNotContain(key, detail)
    Assert.Contains("Incorrect API key provided", detail)

[<Fact; Trait("Task", "T013")>]
let ``the service's message is never logged`` () =
    let logger = CapturingLogger()
    let detail = detailFor (refusal "400 Bad Request" (errorBody "MESSAGE-SENTINEL-3391 PROMPT-SENTINEL-1")) (Some "KEY-SENTINEL-5518") logger
    Assert.Contains("MESSAGE-SENTINEL-3391", detail)
    let logged = String.Join("\n", logger.Lines)
    Assert.Contains("127.0.0.1", logged)
    for secret in [ "MESSAGE-SENTINEL-3391"; "PROMPT-SENTINEL-1"; "KEY-SENTINEL-5518" ] do Assert.DoesNotContain(secret, logged)
