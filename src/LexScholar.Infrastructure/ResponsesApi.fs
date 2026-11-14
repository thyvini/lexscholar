/// Client for ChatGPT's Responses API and the account's model list (F005, research.md
/// sections 8 and 9). Same shape as `ChatCompletions.complete`; the event stream and every
/// error body are untrusted: only named fields are read, the whole thing is capped, and
/// nothing here logs text, prompts, output or the token.
module LexScholar.Infrastructure.ResponsesApi

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open LexScholar.Application
open LexScholar.Domain

/// The model to ask and the account's access token.
type Settings = { Model: string; AccessToken: string }

let private responsesUri = Uri "https://api.openai.com/v1/responses"
let private modelsUri = Uri "https://api.openai.com/v1/models"

/// The outgoing handler: never follows redirects.
let createHandler () : HttpMessageHandler =
    ChatCompletions.createHandler (Uri "https://api.openai.com") :> HttpMessageHandler

let private mapCode (code: string) : Summarize.ServiceError option =
    match code with
    | "subscription_sharing_usage_limit_exceeded" -> Some Summarize.ServiceError.UsageLimitReached
    | "subscription_sharing_user_not_eligible" -> Some Summarize.ServiceError.NoPlanAccess
    | "subscription_sharing_invalid_user" -> Some Summarize.ServiceError.SignedOut
    | _ -> None

/// `error.code` of an error object, when it is a string.
let private codeOf (error: JsonElement) : string option =
    if error.ValueKind <> JsonValueKind.Object then None
    else
        match error.TryGetProperty "code" with
        | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
        | _ -> None

/// The failure for an error object: a recognized code, else the status and the sanitized message.
let private failure (token: string) (status: int) (error: JsonElement option) : Summarize.ServiceError =
    let mapped = error |> Option.bind codeOf |> Option.bind mapCode
    match mapped with
    | Some known -> known
    | None ->
        let message =
            error
            |> Option.bind (fun element -> ChatCompletions.errorMessage (Some token) (Encoding.UTF8.GetBytes("{\"error\":" + element.GetRawText() + "}")))
        Summarize.ServiceError.ErrorStatus(status, message)

let private failureOfBody (token: string) (status: int) (bytes: byte array option) : Summarize.ServiceError =
    let error =
        bytes
        |> Option.bind (fun body ->
            try
                use document = JsonDocument.Parse body
                match document.RootElement.TryGetProperty "error" with
                | true, value -> Some(value.Clone())
                | _ -> None
            with _ -> None)
    failure token status error

let private readLimited (stream: Stream) (cancellation: CancellationToken) : Task<byte array option> = task {
    use buffer = new MemoryStream()
    let chunk = Array.zeroCreate<byte> 16384
    let mutable over = false
    let mutable finished = false
    while not over && not finished do
        let! read = stream.ReadAsync(chunk.AsMemory(), cancellation)
        if read = 0 then finished <- true
        else
            buffer.Write(chunk, 0, read)
            if buffer.Length > int64 ChatCompletions.maximumResponseBytes then over <- true
    return if over then None else Some(buffer.ToArray()) }

let private body (settings: Settings) (messages: Summarize.ChatMessage list) =
    let instructions =
        messages |> List.filter (fun message -> message.Role = "system") |> List.map (fun message -> message.Content) |> String.concat "\n\n"
    let input =
        [ for message in messages do
            if message.Role <> "system" then yield {| role = message.Role; content = message.Content |} ]
    JsonSerializer.Serialize
        {| model = settings.Model; instructions = instructions; input = input; store = false; stream = true |}

type private Event =
    | Delta of string
    | Completed
    | Failed of Summarize.ServiceError
    | Incomplete
    | Malformed
    | Other

let private textDelta = "response.output_text.delta"

/// Kinds that carry no answer text and may be skipped: lifecycle and structure events, pings, and
/// deltas of other content (reasoning, tool arguments).
let private harmless (kind: string) =
    kind = "ping"
    || [ ".added"; ".done"; ".created"; ".in_progress"; ".queued" ] |> List.exists kind.EndsWith
    || (kind.StartsWith "response.reasoning" || kind.StartsWith "response.function_call_arguments") && kind.EndsWith ".delta"

/// One server-sent event: its `event:` name and its `data:` payload, either of which may be missing.
/// The kind is the JSON `type`, and the name must agree with it when both are present. A text delta
/// needs both to be exactly the text-delta kind plus a string `delta`; an event whose kind cannot be
/// determined, that is unknown, or whose two names disagree is `Malformed`, so success is never
/// returned from partial text. Only the known harmless kinds are skipped.
let private parseEvent (token: string) (name: string option) (data: string option) : Event =
    try
        let document = data |> Option.map (fun payload -> JsonDocument.Parse payload)
        use _ = { new IDisposable with member _.Dispose() = document |> Option.iter (fun d -> d.Dispose()) }
        let root = document |> Option.map (fun d -> d.RootElement)
        let objectRoot = root |> Option.forall (fun element -> element.ValueKind = JsonValueKind.Object)
        let jsonType =
            match root with
            | Some element when element.ValueKind = JsonValueKind.Object ->
                match element.TryGetProperty "type" with
                | true, kind when kind.ValueKind = JsonValueKind.String -> Some(kind.GetString())
                | true, _ -> None
                | _ -> None
            | _ -> None
        let hasTypeField =
            match root with
            | Some element when element.ValueKind = JsonValueKind.Object -> fst (element.TryGetProperty "type")
            | _ -> false
        if not objectRoot || (hasTypeField && jsonType.IsNone) then Malformed
        else
            match name, jsonType with
            | Some declared, Some kind when declared <> kind -> Malformed
            | _, Some kind ->
                match kind with
                | k when k = textDelta ->
                    match root |> Option.bind (fun element -> match element.TryGetProperty "delta" with | true, d when d.ValueKind = JsonValueKind.String -> Some(d.GetString()) | _ -> None) with
                    | Some delta -> Delta delta
                    | None -> Malformed
                | "response.completed" -> Completed
                | "response.incomplete" -> Incomplete
                | "response.failed" ->
                    let error =
                        match root with
                        | Some element ->
                            match element.TryGetProperty "response" with
                            | true, response when response.ValueKind = JsonValueKind.Object ->
                                match response.TryGetProperty "error" with
                                | true, value -> Some value
                                | _ -> None
                            | _ -> None
                        | None -> None
                    // The stream itself answered 200; the failure carries no HTTP status of its own.
                    Failed(failure token 200 error)
                | other when harmless other -> Other
                | _ -> Malformed
            // No JSON type: only a harmless name can be trusted; a text delta named without its type is not.
            | Some declared, None when harmless declared -> Other
            | _ -> Malformed
    with _ -> Malformed

/// Reads the event stream under the size cap until a terminal event. An event is complete at its blank line,
/// or at the end of the stream.
let private readStream (token: string) (stream: Stream) (cancellation: CancellationToken) : Task<Result<string, Summarize.ServiceError>> = task {
    let text = StringBuilder()
    let pending = StringBuilder()
    let decoder = Encoding.UTF8.GetDecoder()
    let chunk = Array.zeroCreate<byte> 8192
    let chars = Array.zeroCreate<char> (Encoding.UTF8.GetMaxCharCount chunk.Length)
    let mutable total = 0
    let mutable outcome : Result<string, Summarize.ServiceError> option = None
    let mutable eventName : string option = None
    let dataLines = Collections.Generic.List<string>()
    let dispatch () =
        if eventName.IsSome || dataLines.Count > 0 then
            let data = if dataLines.Count = 0 then None else Some(String.Join("\n", dataLines))
            match parseEvent token eventName data with
            | Delta delta -> text.Append delta |> ignore
            | Completed ->
                outcome <- Some(if text.Length = 0 then Error Summarize.ServiceError.UnusableOutput else Ok(text.ToString()))
            | Incomplete -> outcome <- Some(Error Summarize.ServiceError.UnusableOutput)
            | Failed error -> outcome <- Some(Error error)
            | Malformed -> outcome <- Some(Error Summarize.ServiceError.UnusableOutput)
            | Other -> ()
        eventName <- None
        dataLines.Clear()
    let handleLine (line: string) =
        let line = line.TrimEnd '\r'
        if line = "" then dispatch ()
        elif line.StartsWith "event:" then eventName <- Some(line.Substring(6).Trim())
        elif line.StartsWith "data:" then dataLines.Add(line.Substring(5).TrimStart ' ')
    while outcome.IsNone do
        let! read = stream.ReadAsync(chunk.AsMemory(), cancellation)
        if read = 0 then
            if pending.Length > 0 then handleLine (pending.ToString())
            if outcome.IsNone then dispatch ()
            if outcome.IsNone then outcome <- Some(Error Summarize.ServiceError.UnusableOutput)
        else
            total <- total + read
            if total > ChatCompletions.maximumResponseBytes then outcome <- Some(Error Summarize.ServiceError.UnusableOutput)
            else
                let count = decoder.GetChars(chunk, 0, read, chars, 0)
                pending.Append(chars, 0, count) |> ignore
                let mutable newline = -1
                let mutable index = 0
                let current = pending.ToString()
                while outcome.IsNone && (newline <- current.IndexOf('\n', index); newline >= 0) do
                    handleLine (current.Substring(index, newline - index))
                    index <- newline + 1
                pending.Clear().Append(current.Substring index) |> ignore
    return outcome.Value }

/// Runs one request and hands a successful response to `onSuccess`. Failures map by status and `error.code`.
let private call
    (client: HttpClient)
    (token: string)
    (request: HttpRequestMessage)
    (deadlineToken: CancellationToken)
    (cancellation: CancellationToken)
    (onSuccess: Stream -> CancellationToken -> Task<Result<'a, Summarize.ServiceError>>)
    : Task<Result<'a, Summarize.ServiceError>> = task {
    // One deadline (the client's timeout) covers the headers and the whole body.
    use deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadlineToken)
    try
        use! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
        let status = int response.StatusCode
        if status >= 300 && status < 400 then return Error Summarize.ServiceError.Redirected
        else
            use! stream = response.Content.ReadAsStreamAsync(deadline.Token)
            if not response.IsSuccessStatusCode then
                let! refusal =
                    task {
                        try return! readLimited stream deadline.Token
                        with
                        // A cancellation (the caller's, or the deadline) is not swallowed: the outer handler
                        // rethrows the caller's and reports the deadline as a timeout.
                        | :? IOException | :? HttpRequestException -> return None }
                return Error(failureOfBody token status refusal)
            else
                return! onSuccess stream deadline.Token
    with
    | :? OperationCanceledException when cancellation.IsCancellationRequested -> return raise (OperationCanceledException(cancellation))
    | :? OperationCanceledException -> return Error Summarize.ServiceError.TimedOut
    | :? HttpRequestException | :? IOException -> return Error Summarize.ServiceError.Unreachable }

let private authorized (method': HttpMethod) (uri: Uri) (token: string) =
    let request = new HttpRequestMessage(method', uri)
    request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
    request

let private send (client: HttpClient) (settings: Settings) (messages: Summarize.ChatMessage list) (deadline: CancellationToken) (cancellation: CancellationToken)
    : Task<Result<string, Summarize.ServiceError>> = task {
    use request = authorized HttpMethod.Post responsesUri settings.AccessToken
    request.Content <- new StringContent(body settings messages, Encoding.UTF8, "application/json")
    return! call client settings.AccessToken request deadline cancellation (readStream settings.AccessToken) }

/// One streamed completion; the joined text of `response.output_text.delta` events once
/// `response.completed` arrives, or why it failed. `deadline` is the single limit for the whole call
/// (headers and stream); `complete` supplies the client's timeout, tests fire their own at a handshake.
/// Logs only model, message count, outcome and duration.
let completeWithDeadline (logger: ILogger) (client: HttpClient) (settings: Settings) (messages: Summarize.ChatMessage list) (deadline: CancellationToken) (cancellation: CancellationToken)
    : Task<Result<string, Summarize.ServiceError>> = task {
    let clock = Stopwatch.StartNew()
    let! result = send client settings messages deadline cancellation
    let outcome = match result with Ok _ -> "ok" | Error error -> $"{error}".Split(' ').[0]
    logger.LogInformation(
        "Summary request to ChatGPT plan, model {Model}: {Messages} messages, {Outcome}, {Milliseconds} ms",
        settings.Model, List.length messages, outcome, clock.ElapsedMilliseconds)
    return result }

/// `completeWithDeadline` with the client's timeout as the deadline.
let complete (logger: ILogger) (client: HttpClient) (settings: Settings) (messages: Summarize.ChatMessage list) (cancellation: CancellationToken)
    : Task<Result<string, Summarize.ServiceError>> = task {
    use limit = new CancellationTokenSource(client.Timeout)
    return! completeWithDeadline logger client settings messages limit.Token cancellation }

let private parseModels (stream: Stream) (cancellation: CancellationToken) : Task<Result<ModelEntry list, Summarize.ServiceError>> = task {
    match! readLimited stream cancellation with
    | None -> return Error Summarize.ServiceError.UnusableOutput
    | Some bytes ->
        try
            use document = JsonDocument.Parse bytes
            let entries =
                [ for entry in document.RootElement.GetProperty("models").EnumerateArray() do
                    match entry.TryGetProperty "slug", entry.TryGetProperty "visibility" with
                    | (true, slug), (true, visibility)
                        when slug.ValueKind = JsonValueKind.String && visibility.ValueKind = JsonValueKind.String ->
                        yield { Slug = slug.GetString(); Visibility = visibility.GetString() }
                    | _ -> () ]
            return Ok entries
        with _ -> return Error Summarize.ServiceError.UnusableOutput }

/// The account's model list in the server's order; `ChatGptSignIn.pickModel` takes the first listed one.
let listModels (client: HttpClient) (accessToken: string) (cancellation: CancellationToken)
    : Task<Result<ModelEntry list, Summarize.ServiceError>> = task {
    use request = authorized HttpMethod.Get modelsUri accessToken
    use limit = new CancellationTokenSource(client.Timeout)
    return! call client accessToken request limit.Token cancellation parseModels }
