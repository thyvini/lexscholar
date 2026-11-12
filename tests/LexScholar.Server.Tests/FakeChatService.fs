namespace LexScholar.Server.Tests

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks

/// What the fake service answers with.
type ChatScript =
    /// 200 with a chat-completions body whose single choice has this content.
    | Reply of content: string
    /// An error status with a body that must never reach the researcher.
    | Status of code: int
    /// Never answers until the request is cancelled.
    | WaitUntilCancelled

/// One request the fake received, parsed enough to assert on.
type RecordedChatRequest =
    { Uri: Uri
      Authorization: string option
      Body: string }

/// Stands in for the configured chat-completions service (research §7). It is the
/// outgoing HttpMessageHandler, so no socket is opened. The script is a function of
/// the request number so tests can vary answers per call.
type FakeChatService(script: int -> ChatScript) =
    inherit HttpMessageHandler()
    let requests = ConcurrentQueue<RecordedChatRequest>()

    new(always: ChatScript) = new FakeChatService(fun _ -> always)

    member _.Requests = requests.ToArray() |> Array.toList

    override _.SendAsync(request, cancellation) = task {
        let! body = if isNull request.Content then Task.FromResult "" else request.Content.ReadAsStringAsync(cancellation)
        let authorization = request.Headers.Authorization |> Option.ofObj |> Option.map string
        requests.Enqueue { Uri = request.RequestUri; Authorization = authorization; Body = body }
        match script requests.Count with
        | Reply content ->
            let json = System.Text.Json.JsonSerializer.Serialize({| choices = [| {| message = {| role = "assistant"; content = content |} |} |] |})
            return new HttpResponseMessage(HttpStatusCode.OK, Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"))
        | Status code ->
            return new HttpResponseMessage(enum<HttpStatusCode> code, Content = new StringContent("{\"error\":\"service detail\"}"))
        | WaitUntilCancelled ->
            do! Task.Delay(Timeout.Infinite, cancellation)
            return new HttpResponseMessage(HttpStatusCode.OK)
    }
