/// Client for the OpenAI-compatible Chat Completions protocol (F003, research.md
/// section 1). The body has only `model` and `messages`, plus `temperature` and
/// `max_tokens` when configured. Nothing here logs text, prompts, output or the key.
module LexScholar.Infrastructure.ChatCompletions

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open LexScholar.Application

/// What the researcher configured for the service.
type Settings =
    { Endpoint: Uri
      Model: string
      ApiKey: string option
      Temperature: float option
      MaxOutputTokens: int option }

/// A response larger than this is a failure.
let maximumResponseBytes = 1_048_576

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
let complete (client: HttpClient) (settings: Settings) (messages: Summarize.ChatMessage list) (cancellation: CancellationToken)
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
    | :? HttpRequestException -> return Error Summarize.ServiceError.Unreachable }
