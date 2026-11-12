/// Client for the OpenAI-compatible Chat Completions protocol (F003, research.md
/// section 1). The body has only `model` and `messages`, plus `temperature` and
/// `max_tokens` when configured. Nothing here logs text, prompts, output or the key.
module LexScholar.Infrastructure.ChatCompletions

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open LexScholar.Application
open LexScholar.Domain

/// What the researcher configured for the service.
type Settings =
    { Endpoint: Uri
      Model: string
      ApiKey: string option
      Temperature: float option
      MaxOutputTokens: int option }

/// A response larger than this is a failure.
let maximumResponseBytes = 1_048_576

/// Connects only when every address the name resolves to is loopback, so name
/// resolution cannot send the text off this machine.
let private connectLoopbackOnly
    (resolve: string -> CancellationToken -> Task<IPAddress array>)
    (context: SocketsHttpConnectionContext)
    (cancellation: CancellationToken)
    : ValueTask<Stream> =
    ValueTask<Stream>(task {
        let! addresses = resolve context.DnsEndPoint.Host cancellation
        if addresses.Length = 0 || addresses |> Array.exists (IPAddress.IsLoopback >> not) then
            raise (HttpRequestException "The local service name does not resolve to loopback.")
        // Every address is loopback; try them in order so an IPv4-only service works when ::1 is listed first.
        let mutable stream : Stream = null
        let mutable failure : exn = null
        for address in addresses do
            if isNull stream then
                let socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                socket.NoDelay <- true
                try
                    do! socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellation)
                    stream <- new NetworkStream(socket, true)
                with ex ->
                    socket.Dispose()
                    failure <- ex
        if isNull stream then
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
        return stream })

/// The outgoing handler: never follows redirects. For a local service it also ignores
/// environment proxies and connects to loopback only. `resolve` is a seam for tests.
let createHandlerWith (resolve: string -> CancellationToken -> Task<IPAddress array>) (endpoint: Uri) : SocketsHttpHandler =
    let handler = new SocketsHttpHandler(AllowAutoRedirect = false)
    if ServiceLocation.classify endpoint.Host = Local then
        handler.UseProxy <- false
        handler.ConnectCallback <- fun context cancellation -> connectLoopbackOnly resolve context cancellation
    handler

let createHandler (endpoint: Uri) : SocketsHttpHandler =
    createHandlerWith (fun host cancellation -> Dns.GetHostAddressesAsync(host, cancellation)) endpoint

let private body (settings: Settings) (messages: Summarize.ChatMessage list) =
    let fields = Collections.Generic.Dictionary<string, obj>()
    fields["model"] <- box settings.Model
    fields["messages"] <- box [ for message in messages -> {| role = message.Role; content = message.Content |} ]
    settings.Temperature |> Option.iter (fun value -> fields["temperature"] <- box value)
    settings.MaxOutputTokens |> Option.iter (fun value -> fields["max_tokens"] <- box value)
    JsonSerializer.Serialize fields

let private readLimited (stream: Stream) (cancellation: CancellationToken) : Task<byte array option> = task {
    use buffer = new MemoryStream()
    let chunk = Array.zeroCreate<byte> 16384
    let mutable over = false
    let mutable finished = false
    while not over && not finished do
        let! read = stream.ReadAsync(chunk, 0, chunk.Length, cancellation)
        if read = 0 then finished <- true
        else
            buffer.Write(chunk, 0, read)
            if buffer.Length > int64 maximumResponseBytes then over <- true
    return if over then None else Some (buffer.ToArray()) }

let private content (bytes: byte array) : string option =
    try
        use document = JsonDocument.Parse bytes
        let choices = document.RootElement.GetProperty "choices"
        let message = choices.[0].GetProperty "message"
        match message.GetProperty("content").GetString() with
        | null -> None
        | text -> Some text
    with _ -> None

/// One non-streaming completion; the answer's text, or why it failed (never the service's body).
let private send (client: HttpClient) (settings: Settings) (messages: Summarize.ChatMessage list) (cancellation: CancellationToken)
    : Task<Result<string, Summarize.ServiceError>> = task {
    let uri = Uri(settings.Endpoint.AbsoluteUri.TrimEnd('/') + "/chat/completions")
    use request = new HttpRequestMessage(HttpMethod.Post, uri)
    request.Content <- new StringContent(body settings messages, Encoding.UTF8, "application/json")
    settings.ApiKey |> Option.iter (fun key -> request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", key))
    try
        use! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation)
        let status = int response.StatusCode
        if status >= 300 && status < 400 then return Error Summarize.ServiceError.Redirected
        elif not response.IsSuccessStatusCode then return Error (Summarize.ServiceError.ErrorStatus status)
        else
            use! stream = response.Content.ReadAsStreamAsync(cancellation)
            match! readLimited stream cancellation with
            | None -> return Error Summarize.ServiceError.UnusableOutput
            | Some bytes ->
                match content bytes with
                | Some text -> return Ok text
                | None -> return Error Summarize.ServiceError.UnusableOutput
    with
    | :? OperationCanceledException when cancellation.IsCancellationRequested -> return raise (OperationCanceledException(cancellation))
    | :? OperationCanceledException -> return Error Summarize.ServiceError.TimedOut
    | :? HttpRequestException | :? IOException -> return Error Summarize.ServiceError.Unreachable }


/// `send`, logging only host, model, message count, outcome and duration.
let complete (logger: ILogger) (client: HttpClient) (settings: Settings) (messages: Summarize.ChatMessage list) (cancellation: CancellationToken)
    : Task<Result<string, Summarize.ServiceError>> = task {
    let clock = Stopwatch.StartNew()
    let! result = send client settings messages cancellation
    let outcome = match result with Ok _ -> "ok" | Error error -> $"{error}".Split(' ').[0]
    logger.LogInformation(
        "Summary request to {Host}, model {Model}: {Messages} messages, {Outcome}, {Milliseconds} ms",
        settings.Endpoint.Host, settings.Model, List.length messages, outcome, clock.ElapsedMilliseconds)
    return result }
