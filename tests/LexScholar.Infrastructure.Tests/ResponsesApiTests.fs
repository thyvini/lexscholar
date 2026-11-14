/// The Responses client and model listing against FakeOpenAi (F005 T006): exact request fields,
/// the event stream, error-code mapping, time limits, cancellation and log hygiene.
module LexScholar.Infrastructure.Tests.ResponsesApiTests

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Server.Tests
open Microsoft.Extensions.Logging
open Xunit

type private CapturingLogger() =
    let lines = List<string>()
    member _.Lines = lines |> Seq.toList
    interface ILogger with
        member _.BeginScope _ = null
        member _.IsEnabled _ = true
        member _.Log(_, _, state, error, formatter) =
            lines.Add(formatter.Invoke(state, error))
            if not (isNull error) then lines.Add(string error)

let private none = CancellationToken.None
let private settings : ResponsesApi.Settings = { Model = "gpt-test"; AccessToken = "SECRET-ACCESS" }
let private messages : Summarize.ChatMessage list =
    [ { Role = "system"; Content = "be brief" }
      { Role = "user"; Content = "SECRET-TEXT page one" } ]

let private complete (fake: HttpMessageHandler) (logger: ILogger) =
    use client = new HttpClient(fake)
    ResponsesApi.complete logger client settings messages none

let private responseRequests (fake: FakeOpenAi) = fake.RequestsTo("api.openai.com", "/v1/responses")

[<Fact; Trait("Task", "T006")>]
let ``the request has exactly the Responses fields and the Bearer token`` () = task {
    let fake = new FakeOpenAi()
    let! _ = complete fake (CapturingLogger())
    let request = (responseRequests fake).Head
    Assert.Equal("POST", request.Method)
    Assert.Equal(Some "Bearer SECRET-ACCESS", request.Authorization)
    use document = JsonDocument.Parse request.Body
    let root = document.RootElement
    let names = [ for property in root.EnumerateObject() -> property.Name ] |> List.sort
    Assert.Equal<string list>([ "input"; "instructions"; "model"; "store"; "stream" ], names)
    Assert.Equal("gpt-test", root.GetProperty("model").GetString())
    Assert.Equal("be brief", root.GetProperty("instructions").GetString())
    Assert.False(root.GetProperty("store").GetBoolean())
    Assert.True(root.GetProperty("stream").GetBoolean())
    let input = root.GetProperty "input"
    Assert.Equal(1, input.GetArrayLength())
    Assert.Equal("user", input[0].GetProperty("role").GetString())
    Assert.Contains("SECRET-TEXT", input[0].GetRawText()) }

[<Fact; Trait("Task", "T006")>]
let ``the text deltas are joined when the stream completes`` () = task {
    let! result = complete (new FakeOpenAi(ResponsesScript.Completed "{\"points\": []}")) (CapturingLogger())
    Assert.Equal(Ok "{\"points\": []}", result) }

let private codes =
    [ "subscription_sharing_usage_limit_exceeded", Summarize.ServiceError.UsageLimitReached
      "subscription_sharing_user_not_eligible", Summarize.ServiceError.NoPlanAccess
      "subscription_sharing_invalid_user", Summarize.ServiceError.SignedOut ]

[<Fact; Trait("Task", "T006")>]
let ``each error code before the stream maps to its failure`` () = task {
    for code, expected in codes do
        let! result = complete (new FakeOpenAi(ResponsesScript.Status(429, code))) (CapturingLogger())
        Assert.Equal(Error expected, result) }

[<Fact; Trait("Task", "T006")>]
let ``each error code as response.failed mid-stream maps to its failure`` () = task {
    for code, expected in codes do
        let! result = complete (new FakeOpenAi(ResponsesScript.Failed code)) (CapturingLogger())
        Assert.Equal(Error expected, result) }

[<Fact; Trait("Task", "T006")>]
let ``an unknown error code is an error status with only the sanitized service message`` () = task {
    let! before = complete (new FakeOpenAi(ResponsesScript.Status(503, "subscription_sharing_usage_unavailable"))) (CapturingLogger())
    Assert.Equal(Error(Summarize.ServiceError.ErrorStatus(503, Some "service detail")), before)
    let! mid = complete (new FakeOpenAi(ResponsesScript.Failed "something_else")) (CapturingLogger())
    match mid with
    | Error(Summarize.ServiceError.ErrorStatus(_, Some "service detail")) -> ()
    | other -> failwith $"unexpected {other}" }

/// A handler that answers every request with fixed status, content type and body.
type private FixedHandler(status: HttpStatusCode, contentType: string, body: string) =
    inherit HttpMessageHandler()
    override _.SendAsync(_, _) =
        Task.FromResult(new HttpResponseMessage(status, Content = new StringContent(body, Encoding.UTF8, contentType)))

let private sse (events: (string * string) list) =
    String.Join("", events |> List.map (fun (name, data) -> $"event: {name}\ndata: {data}\n\n"))

[<Fact; Trait("Task", "T006")>]
let ``response.incomplete, an ended stream and an empty answer are unusable output`` () = task {
    let delta = ("response.output_text.delta", """{"type":"response.output_text.delta","delta":"part"}""")
    for body in
        [ sse [ delta; "response.incomplete", """{"type":"response.incomplete"}""" ]
          sse [ delta ]
          sse [ "response.completed", """{"type":"response.completed"}""" ] ] do
        let! result = complete (new FixedHandler(HttpStatusCode.OK, "text/event-stream", body)) (CapturingLogger())
        Assert.Equal(Error Summarize.ServiceError.UnusableOutput, result) }

[<Fact; Trait("Task", "T006")>]
let ``a failure after some text gives the failure and no partial text`` () = task {
    let delta text = ("response.output_text.delta", $$"""{"type":"response.output_text.delta","delta":"{{text}}"}""")
    let! failed =
        complete (new FixedHandler(HttpStatusCode.OK, "text/event-stream",
                                   sse [ delta "partial"; "response.failed", """{"type":"response.failed","response":{"error":{"code":"subscription_sharing_usage_limit_exceeded"}}}""" ]))
            (CapturingLogger())
    Assert.Equal(Error Summarize.ServiceError.UsageLimitReached, failed) }

[<Fact; Trait("Task", "T006")>]
let ``a stream over the one megabyte cap is unusable output`` () = task {
    let big = String('x', 2_000_000)
    let body = sse [ "response.output_text.delta", $$"""{"type":"response.output_text.delta","delta":"{{big}}"}"""; "response.completed", """{"type":"response.completed"}""" ]
    let! result = complete (new FixedHandler(HttpStatusCode.OK, "text/event-stream", body)) (CapturingLogger())
    Assert.Equal(Error Summarize.ServiceError.UnusableOutput, result) }

[<Fact; Trait("Task", "T006")>]
let ``a redirect is not followed`` () = task {
    let! result = complete (new FixedHandler(HttpStatusCode.Found, "text/plain", "")) (CapturingLogger())
    Assert.Equal(Error Summarize.ServiceError.Redirected, result) }

[<Fact; Trait("Task", "T006")>]
let ``another refusal gives the sanitized status and message only`` () = task {
    let body = """{"error":{"message":"bad\nthing SECRET-ACCESS","code":"other"},"extra":"SECRET-EXTRA"}"""
    let! result = complete (new FixedHandler(HttpStatusCode.BadRequest, "application/json", body)) (CapturingLogger())
    match result with
    | Error(Summarize.ServiceError.ErrorStatus(400, Some message)) ->
        Assert.DoesNotContain("\n", message)
        Assert.DoesNotContain("SECRET", message)
    | other -> failwith $"unexpected {other}" }

/// Sends one event, then stalls without ending the stream.
type private StallingStreamHandler() =
    inherit HttpMessageHandler()
    let stalled = TaskCompletionSource()
    /// Completes when the client waits for more of the stream after the first event (headers are already in).
    member _.Stalled : Task = stalled.Task
    override _.SendAsync(_, cancellation) = task {
        let head = Encoding.UTF8.GetBytes(sse [ "response.output_text.delta", """{"type":"response.output_text.delta","delta":"a"}""" ])
        let served = ref false
        let stream =
            { new Stream() with
                member _.CanRead = true
                member _.CanSeek = false
                member _.CanWrite = false
                member _.Length = raise (NotSupportedException())
                member _.Position with get () = raise (NotSupportedException()) and set _ = raise (NotSupportedException())
                member _.Flush() = ()
                member _.Read(_, _, _) = raise (NotSupportedException())
                member _.Seek(_, _) = raise (NotSupportedException())
                member _.SetLength _ = raise (NotSupportedException())
                member _.Write(_, _, _) = raise (NotSupportedException())
                member _.ReadAsync(buffer: Memory<byte>, cancellation: CancellationToken) =
                    ValueTask<int>(task {
                        if not served.Value then
                            served.Value <- true
                            head.AsMemory().CopyTo buffer
                            return head.Length
                        else
                            stalled.TrySetResult() |> ignore
                            do! Task.Delay(Timeout.Infinite, cancellation)
                            return 0 }) }
        return new HttpResponseMessage(HttpStatusCode.OK, Content = new StreamContent(stream)) }

/// Delivers the body in the given chunks, one per read.
type private ChunkedHandler(chunks: byte array list) =
    inherit HttpMessageHandler()
    override _.SendAsync(_, _) =
        let remaining = ref chunks
        let stream =
            { new Stream() with
                member _.CanRead = true
                member _.CanSeek = false
                member _.CanWrite = false
                member _.Length = raise (NotSupportedException())
                member _.Position with get () = raise (NotSupportedException()) and set _ = raise (NotSupportedException())
                member _.Flush() = ()
                member _.Read(_, _, _) = raise (NotSupportedException())
                member _.Seek(_, _) = raise (NotSupportedException())
                member _.SetLength _ = raise (NotSupportedException())
                member _.Write(_, _, _) = raise (NotSupportedException())
                member _.ReadAsync(buffer: Memory<byte>, _: CancellationToken) =
                    match remaining.Value with
                    | [] -> ValueTask<int>(0)
                    | next :: rest ->
                        remaining.Value <- rest
                        next.AsMemory().CopyTo buffer
                        ValueTask<int>(next.Length) }
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK, Content = new StreamContent(stream)))

[<Fact; Trait("Task", "T006")>]
let ``a multi-byte character split across reads is decoded and the text is kept`` () = task {
    let text = "\U0001F600" + String('a', 9000)
    let body =
        Encoding.UTF8.GetBytes(
            sse [ "response.output_text.delta", $$"""{"type":"response.output_text.delta","delta":"{{text}}"}"""
                  "response.completed", """{"type":"response.completed"}""" ])
    // The first read ends after three of the four bytes of the emoji; the second is a full read.
    let cut = Array.findIndex (fun b -> b = 0xF0uy) body + 3
    let chunks = [ body[.. cut - 1]; body[cut .. cut + 8191]; body[cut + 8192 ..] ]
    let! result = complete (new ChunkedHandler(chunks)) (CapturingLogger())
    Assert.Equal(Ok text, result) }

let private guard (call: Task<'a>) =
    let finished = Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds 10.0)).GetAwaiter().GetResult()
    Assert.True(obj.ReferenceEquals(finished, call), "the call is still pending")

[<Fact; Trait("Task", "T006")>]
let ``a service that never answers times out`` () =
    use client = new HttpClient(new FakeOpenAi(ResponsesScript.WaitUntilCancelled), Timeout = TimeSpan.FromMilliseconds 300.0)
    let call = ResponsesApi.complete (CapturingLogger()) client settings messages none
    guard call
    Assert.Equal(Error Summarize.ServiceError.TimedOut, call.GetAwaiter().GetResult())

[<Fact; Trait("Task", "T006")>]
let ``a stream that stalls after some text times out`` () =
    use client = new HttpClient(new StallingStreamHandler(), Timeout = TimeSpan.FromMilliseconds 300.0)
    let call = ResponsesApi.complete (CapturingLogger()) client settings messages none
    guard call
    Assert.Equal(Error Summarize.ServiceError.TimedOut, call.GetAwaiter().GetResult())

[<Fact; Trait("Task", "T006")>]
let ``the deadline ends the call after the headers while the stream stalls`` () =
    let handler = new StallingStreamHandler()
    use client = new HttpClient(handler, Timeout = TimeSpan.FromSeconds 30.0)
    use deadline = new CancellationTokenSource()
    let call = ResponsesApi.completeWithDeadline (CapturingLogger()) client settings messages deadline.Token none
    // The test fires the deadline once the stream is stalling; the 10 s guard only stops a hang.
    let reached = Task.WhenAny(handler.Stalled, Task.Delay(TimeSpan.FromSeconds 10.0)).GetAwaiter().GetResult()
    Assert.True(obj.ReferenceEquals(reached, handler.Stalled), "the stream never started")
    deadline.Cancel()
    guard call
    Assert.Equal(Error Summarize.ServiceError.TimedOut, call.GetAwaiter().GetResult())

[<Fact; Trait("Task", "T006")>]
let ``the deadline ends the call while the service has not answered the headers`` () =
    let fake = new FakeOpenAi(ResponsesScript.WaitUntilCancelled)
    use client = new HttpClient(fake, Timeout = TimeSpan.FromSeconds 30.0)
    use deadline = new CancellationTokenSource()
    let call = ResponsesApi.completeWithDeadline (CapturingLogger()) client settings messages deadline.Token none
    let reached = Task.WhenAny(fake.Waiting, Task.Delay(TimeSpan.FromSeconds 10.0)).GetAwaiter().GetResult()
    Assert.True(obj.ReferenceEquals(reached, fake.Waiting), "the request never reached the service")
    deadline.Cancel()
    guard call
    Assert.Equal(Error Summarize.ServiceError.TimedOut, call.GetAwaiter().GetResult())

[<Fact; Trait("Task", "T006")>]
let ``a malformed text event is unusable output and never a partial summary`` () = task {
    let delta text = ("response.output_text.delta", $$"""{"type":"response.output_text.delta","delta":"{{text}}"}""")
    let completed = ("response.completed", """{"type":"response.completed"}""")
    for bad in
        [ "response.output_text.delta", """{"type":"response.output_text.delta"}"""
          "response.output_text.delta", """{"type":"response.output_text.delta","delta":5}"""
          "response.output_text.delta", "{not json" ] do
        let! result = complete (new FixedHandler(HttpStatusCode.OK, "text/event-stream", sse [ delta "good "; bad; delta "rest"; completed ])) (CapturingLogger())
        Assert.Equal(Error Summarize.ServiceError.UnusableOutput, result) }

[<Fact; Trait("Task", "T006")>]
let ``a caller's cancellation is a cancellation, not a timeout`` () =
    let fake = new FakeOpenAi(ResponsesScript.WaitUntilCancelled)
    use client = new HttpClient(fake, Timeout = TimeSpan.FromSeconds 30.0)
    use source = new CancellationTokenSource()
    let call = ResponsesApi.complete (CapturingLogger()) client settings messages source.Token
    // Guards so a missing request fails the test instead of hanging it.
    let first = Task.WhenAny(fake.Waiting, Task.Delay(TimeSpan.FromSeconds 10.0)).GetAwaiter().GetResult()
    Assert.True(obj.ReferenceEquals(first, fake.Waiting), "the request never reached the service")
    source.Cancel()
    let second = Task.WhenAny(fake.Cancelled, Task.Delay(TimeSpan.FromSeconds 10.0)).GetAwaiter().GetResult()
    Assert.True(obj.ReferenceEquals(second, fake.Cancelled), "the service never saw the cancellation")
    guard call
    Assert.True(call.IsCanceled, "an abandoned call is cancelled, not reported as a failure")

[<Fact; Trait("Task", "T006")>]
let ``a failure or a success logs only model, message count, outcome and duration`` () = task {
    let logger = CapturingLogger()
    let! _ = complete (new FakeOpenAi(ResponsesScript.Completed "SECRET-OUTPUT")) logger
    let! _ = complete (new FakeOpenAi(ResponsesScript.Status(429, "subscription_sharing_usage_limit_exceeded"))) logger
    let text = String.Join("\n", logger.Lines)
    Assert.Contains("gpt-test", text)
    Assert.Contains("UsageLimitReached", text)
    for secret in [ "SECRET-ACCESS"; "SECRET-TEXT"; "SECRET-OUTPUT"; "be brief"; "service detail" ] do
        Assert.DoesNotContain(secret, text) }

let private listModels (fake: FakeOpenAi) =
    use client = new HttpClient(fake)
    ResponsesApi.listModels client "SECRET-ACCESS" none

[<Fact; Trait("Task", "T006")>]
let ``the model list is read in the server's order and the first listed one is picked`` () = task {
    let fake = new FakeOpenAi(Models = [ "gpt-hidden", "hide"; "gpt-a", "list"; "gpt-b", "list" ])
    let! result = listModels fake
    let request = (fake.RequestsTo("api.openai.com", "/v1/models")).Head
    Assert.Equal("GET", request.Method)
    Assert.Equal(Some "Bearer SECRET-ACCESS", request.Authorization)
    match result with
    | Ok models ->
        Assert.Equal<string list>([ "gpt-hidden"; "gpt-a"; "gpt-b" ], models |> List.map (fun m -> m.Slug))
        Assert.Equal(Some "gpt-a", ChatGptSignIn.pickModel models)
    | Error error -> failwith $"{error}" }

[<Fact; Trait("Task", "T006")>]
let ``an unreadable model list and a refusal are failures`` () = task {
    use client = new HttpClient(new FixedHandler(HttpStatusCode.OK, "application/json", "not json"))
    let! unreadable = ResponsesApi.listModels client "t" none
    Assert.Equal(Error Summarize.ServiceError.UnusableOutput, unreadable)
    use refused = new HttpClient(new FixedHandler(HttpStatusCode.Unauthorized, "application/json", """{"error":{"code":"subscription_sharing_invalid_user"}}"""))
    let! signedOut = ResponsesApi.listModels refused "t" none
    Assert.Equal(Error Summarize.ServiceError.SignedOut, signedOut) }
