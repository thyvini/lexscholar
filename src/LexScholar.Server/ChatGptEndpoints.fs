/// Routes of the ChatGPT sign-in (F005, contracts/chatgpt-api.yaml): destinations, sign-in
/// start and the callback. Sign-out and the welcome dismissal.
module LexScholar.Server.ChatGptEndpoints

open System
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open LexScholar.Application
open LexScholar.Application.ChatGptAccess
open LexScholar.Domain
open LexScholar.Infrastructure

/// The query parameter of the callback that carries the client id issued at registration.
/// Unverified against the real service; T012 confirms the name.
[<Literal>]
let private IssuedClientIdParameter = "client_id"

let private problem (status: int) (code: string) (title: string) : IResult =
    Results.Json({| title = title; status = status; code = code |}, System.Text.Json.JsonSerializerOptions.Web, "application/problem+json", status)

let private json (value: obj) : IResult = Results.Json(value, System.Text.Json.JsonSerializerOptions.Web)

/// The outgoing handler comes from DI when one is registered (tests), else the real one.
let private serviceClient (context: HttpContext) =
    match context.RequestServices.GetService<HttpMessageHandler>() with
    | null -> new HttpClient(OpenAiAuth.createHandler (), true)
    | injected -> new HttpClient(injected, false)

let authClient (http: HttpClient) (clock: unit -> DateTimeOffset) : AuthClient =
    { Exchange = fun clientId code verifier redirect cancellation -> OpenAiAuth.exchange http clock clientId code verifier redirect cancellation
      VerifyIdToken =
        fun clientId nonce idToken cancellation ->
            task {
                let! result = OpenAiAuth.verifyIdToken http clock clientId nonce idToken cancellation
                return result |> Result.mapError ignore
            }
      Refresh = OpenAiAuth.refresh http clock
      Revoke = OpenAiAuth.revoke http }

let private destinations (configuration: WorkspaceConfiguration) (vault: Vault) : Task<IResult> = task {
    let configured : obj =
        match configuration.Summary with
        | None -> null
        | Some summary ->
            box {| serviceHost = summary.Endpoint.Authority
                   model = summary.Model
                   local = (ServiceLocation.classify summary.Endpoint.Host = Local) |}
    let! credentials = vault.Read()
    let chatgpt : obj =
        match credentials with
        | None -> box {| signedIn = false |}
        | Some record -> box {| signedIn = true; account = record.Account; confirmed = record.ConfirmedAt.IsSome |}
    let welcomePending = credentials.IsSome && not (ChatGptCredentials.isShown configuration.DataDirectory)
    return json {| chatgpt = chatgpt; configured = configured; welcomePending = welcomePending |} }

let private outcomeName = function
    | SignedIn -> "signed_in"
    | Cancelled -> "cancelled"
    | NoPlanAccess -> "no_plan_access"
    | Failed -> "failed"

/// Registers the routes and returns the vault they use, so the summary endpoint takes the same lock.
let map (app: WebApplication) (configuration: WorkspaceConfiguration) : Vault =
    let directory = configuration.DataDirectory
    let vault =
        Vault(
            { Read = fun () -> ChatGptCredentials.read directory
              Save = ChatGptCredentials.save directory
              Delete = fun () -> ChatGptCredentials.delete directory })
    let session = SignInSession()
    let clock () = DateTimeOffset.UtcNow
    app.MapGet("/api/summary/destinations", Func<Task<IResult>>(fun () -> destinations configuration vault)) |> ignore
    app.MapPost("/api/chatgpt/sign-in", Func<HttpContext, Task<IResult>>(fun context -> task {
        // The host is loopback-only, so the port of this request is a loopback port.
        let port = if context.Request.Host.Port.HasValue then context.Request.Host.Port.Value else context.Connection.LocalPort
        match! startSignIn vault session clock $"http://127.0.0.1:{port}/auth/callback" with
        | Ok url -> return json {| authorizationUrl = url |}
        | Error () -> return problem 409 "already_signed_in" "Already signed in; sign out first." })) |> ignore
    app.MapGet("/auth/callback", Func<HttpContext, Task<IResult>>(fun context -> task {
        // Never log this query: it holds the authorization code and the state.
        let value (name: string) =
            match context.Request.Query[name].ToString() with
            | "" -> None
            | text -> Some text
        let query =
            { Code = value "code"
              State = value "state"
              Error = value "error"
              ClientId = value IssuedClientIdParameter }
        use http = serviceClient context
        let! outcome = completeSignIn vault session (authClient http clock) clock query context.RequestAborted
        return Results.Redirect($"/index.html?chatgpt={outcomeName outcome}") })) |> ignore
    app.MapPost("/api/chatgpt/sign-out", Func<HttpContext, Task<IResult>>(fun context -> task {
        use http = serviceClient context
        let! revoked = signOut vault session (authClient http clock) context.RequestAborted
        return json {| revoked = revoked |} })) |> ignore
    app.MapPost("/api/chatgpt/welcome/dismiss", Func<IResult>(fun () ->
        ChatGptCredentials.markShown directory
        Results.NoContent())) |> ignore
    vault
