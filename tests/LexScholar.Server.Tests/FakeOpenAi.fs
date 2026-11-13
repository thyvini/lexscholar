namespace LexScholar.Server.Tests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

/// What the fake Responses service answers with (research §8). Qualified access, because
/// `Status` also names a case of F003's `ChatScript`.
[<RequireQualifiedAccess>]
type ResponsesScript =
    /// 200 with an SSE stream: text deltas, then `response.completed`.
    | Completed of text: string
    /// 200 with an SSE stream that ends in `response.failed` carrying this error code.
    | Failed of code: string
    /// A non-200 answer whose JSON body carries this error code.
    | Status of status: int * code: string
    /// Never answers until the request is cancelled.
    | WaitUntilCancelled

/// One request the fake saw. Bodies are kept so tests can assert on exact fields.
type RecordedOpenAiRequest =
    { Host: string
      Method: string
      Path: string
      Authorization: string option
      Body: string }

/// A throwaway RSA key and the ID tokens signed with it, for the JWKS the fake serves.
module TestIdToken =
    let key = RSA.Create(2048)
    let keyId = "test-key-1"

    let private base64Url (bytes: byte[]) = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

    /// A signed ID token. `algorithm` and `kid` can be changed to build tokens that must be rejected.
    let build (algorithm: string) (kid: string) (claims: (string * obj) list) =
        let header = JsonSerializer.Serialize(dict [ "alg", box algorithm; "kid", box kid; "typ", box "JWT" ])
        let payload = JsonSerializer.Serialize(dict claims)
        let signingInput = base64Url (Encoding.UTF8.GetBytes header) + "." + base64Url (Encoding.UTF8.GetBytes payload)
        let signature = key.SignData(Encoding.ASCII.GetBytes signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
        signingInput + "." + base64Url signature

    let standard (audience: string) (nonce: string) (email: string) =
        let expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        build "RS256" keyId
            [ "iss", box "https://auth.openai.com"; "aud", box audience; "sub", box "user-test"
              "email", box email; "nonce", box nonce; "exp", box expires ]

    let jwks () =
        let parameters = key.ExportParameters false
        JsonSerializer.Serialize(
            {| keys = [| {| kty = "RSA"; kid = keyId; alg = "RS256"; ``use`` = "sig"; n = base64Url parameters.Modulus; e = base64Url parameters.Exponent |} |] |})

/// Stands in for auth.openai.com and api.openai.com (research §5, §8). It is the outgoing
/// HttpMessageHandler, so no socket is opened; requests are routed by host and path.
type FakeOpenAi(responses: int -> ResponsesScript) =
    inherit HttpMessageHandler()
    let requests = ConcurrentQueue<RecordedOpenAiRequest>()
    let mutable responseCalls = 0
    let waiting = TaskCompletionSource()
    let cancelled = TaskCompletionSource()
    let tokenArrived = TaskCompletionSource()
    let mutable tokenHold : TaskCompletionSource option = None

    new(always: ResponsesScript) = new FakeOpenAi(fun _ -> always)
    new() = new FakeOpenAi(ResponsesScript.Completed "ok")

    /// The client id the fake issues and the audience of its ID tokens.
    member val ClientId = "client-test" with get, set
    member val Email = "researcher@example.test" with get, set
    /// Nonce placed in the ID token; tests copy it from the authorization URL.
    member val Nonce = "" with get, set
    member val Scope = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct" with get, set
    member val AccessToken = "access-token-1" with get, set
    member val RefreshToken = "refresh-token-1" with get, set
    member val ExpiresIn = 3600 with get, set
    /// When set, the token endpoint answers this status and body instead of issuing tokens.
    member val TokenFailure : (int * string) option = None with get, set
    /// Status of the revocation endpoint.
    member val RevocationStatus = 200 with get, set
    member val Models = [ "gpt-test", "list"; "gpt-hidden", "hide" ] with get, set

    member _.Requests = requests.ToArray() |> Array.toList
    member this.RequestsTo(host: string, path: string) = this.Requests |> List.filter (fun r -> r.Host = host && r.Path = path)

    /// Completes when a `WaitUntilCancelled` Responses request waits, and when it is cancelled (handshakes, no polling).
    member _.Waiting : Task = waiting.Task
    member _.Cancelled : Task = cancelled.Task
    /// Completes when the first token request arrives.
    member _.TokenArrived : Task = tokenArrived.Task

    /// Holds every token response until the returned function is called (for race tests).
    member _.HoldTokens() : (unit -> unit) =
        let hold = TaskCompletionSource()
        tokenHold <- Some hold
        fun () -> hold.TrySetResult() |> ignore

    member private this.Json(status: int, body: string) =
        new HttpResponseMessage(enum<HttpStatusCode> status, Content = new StringContent(body, Encoding.UTF8, "application/json"))

    member private this.Sse(events: (string * obj) list) =
        let text = StringBuilder()
        for name, data in events do
            text.Append("event: ").Append(name).Append('\n').Append("data: ").Append(JsonSerializer.Serialize data).Append("\n\n") |> ignore
        new HttpResponseMessage(HttpStatusCode.OK, Content = new StringContent(text.ToString(), Encoding.UTF8, "text/event-stream"))

    override this.SendAsync(request, cancellation) = task {
        let! body = if isNull request.Content then Task.FromResult "" else request.Content.ReadAsStringAsync(cancellation)
        let authorization = request.Headers.Authorization |> Option.ofObj |> Option.map string
        let host = request.RequestUri.Host
        let path = request.RequestUri.AbsolutePath
        requests.Enqueue { Host = host; Method = request.Method.Method; Path = path; Authorization = authorization; Body = body }
        match host, path with
        | "auth.openai.com", "/api/accounts/oauth/token" ->
            tokenArrived.TrySetResult() |> ignore
            match tokenHold with
            | Some hold -> do! hold.Task.WaitAsync(cancellation)
            | None -> ()
            match this.TokenFailure with
            | Some (status, failure) -> return this.Json(status, failure)
            | None ->
                let idToken = TestIdToken.standard this.ClientId this.Nonce this.Email
                return this.Json(200, JsonSerializer.Serialize(
                    {| access_token = this.AccessToken; refresh_token = this.RefreshToken; id_token = idToken
                       token_type = "Bearer"; expires_in = this.ExpiresIn; scope = this.Scope |}))
        | "auth.openai.com", "/api/accounts/oauth/revoke" ->
            return this.Json(this.RevocationStatus, "{}")
        | "auth.openai.com", "/.well-known/openid-configuration" ->
            return this.Json(200, JsonSerializer.Serialize(
                {| issuer = "https://auth.openai.com"
                   revocation_endpoint = "https://auth.openai.com/api/accounts/oauth/revoke"
                   jwks_uri = "https://auth.openai.com/.well-known/jwks.json" |}))
        | "auth.openai.com", "/.well-known/jwks.json" -> return this.Json(200, TestIdToken.jwks ())
        | "api.openai.com", "/v1/models" ->
            return this.Json(200, JsonSerializer.Serialize(
                {| models = this.Models |> List.map (fun (slug, visibility) -> {| slug = slug; visibility = visibility |}) |}))
        | "api.openai.com", "/v1/responses" ->
            let number = Interlocked.Increment(&responseCalls)
            match responses number with
            | ResponsesScript.Completed text ->
                let half = text.Length / 2
                return this.Sse
                    [ "response.output_text.delta", box {| ``type`` = "response.output_text.delta"; delta = text.Substring(0, half) |}
                      "response.output_text.delta", box {| ``type`` = "response.output_text.delta"; delta = text.Substring half |}
                      "response.completed", box {| ``type`` = "response.completed" |} ]
            | ResponsesScript.Failed code ->
                return this.Sse
                    [ "response.failed", box {| ``type`` = "response.failed"; response = {| error = {| code = code; message = "service detail" |} |} |} ]
            | ResponsesScript.Status (status, code) ->
                return this.Json(status, JsonSerializer.Serialize({| error = {| code = code; message = "service detail" |} |}))
            | ResponsesScript.WaitUntilCancelled ->
                waiting.TrySetResult() |> ignore
                try do! Task.Delay(Timeout.Infinite, cancellation)
                with :? OperationCanceledException as error -> cancelled.TrySetResult() |> ignore; raise error
                return new HttpResponseMessage(HttpStatusCode.OK)
        | _ -> return new HttpResponseMessage(HttpStatusCode.NotFound)
    }
