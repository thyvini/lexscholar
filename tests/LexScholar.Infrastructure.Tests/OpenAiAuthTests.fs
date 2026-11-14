/// The sign-in service client against FakeOpenAi (F005 T003): exact request fields, refresh
/// outcomes, revocation, ID-token signature checks, no redirects, no response bodies in errors.
module LexScholar.Infrastructure.Tests.OpenAiAuthTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Application.ChatGptAccess
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Server.Tests
open Xunit

let private now = DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero)
let private clock () = now
let private none = CancellationToken.None
let private clientOf (fake: HttpMessageHandler) = new HttpClient(fake)

let private credentials : Credentials =
    { ClientId = "client-test"; HostId = "urn:uuid:11111111-1111-1111-1111-111111111111"; Subject = "user-test"
      Account = "researcher@example.test"; AccessToken = "old-access"; RefreshToken = "old-refresh"; IdToken = "old-id"
      ExpiresAt = now; Scopes = [ "openid"; ChatGptSignIn.planScope ]; SignedInAt = now; ConfirmedAt = None }

let private fields (body: string) =
    body.Split('&')
    |> Array.map (fun pair -> let parts = pair.Split('=', 2) in Uri.UnescapeDataString(parts[0].Replace('+', ' ')), Uri.UnescapeDataString(parts[1].Replace('+', ' ')))
    |> Array.sortBy fst
    |> List.ofArray

let private tokenRequests (fake: FakeOpenAi) = fake.RequestsTo("auth.openai.com", "/api/accounts/oauth/token")

[<Fact; Trait("Task", "T003")>]
let ``exchange posts exactly the authorization-code fields and returns the grant`` () = task {
    let fake = new FakeOpenAi()
    use http = clientOf fake
    let! result = OpenAiAuth.exchange http clock "client-test" "code-1" "verifier-1" "http://127.0.0.1:5000/auth/callback" none
    Assert.Equal<(string * string) list>(
        [ "client_id", "client-test"; "code", "code-1"; "code_verifier", "verifier-1"; "grant_type", "authorization_code"
          "redirect_uri", "http://127.0.0.1:5000/auth/callback"; "resource", "https://api.openai.com/v1" ],
        fields (tokenRequests fake).Head.Body)
    Assert.Equal("POST", (tokenRequests fake).Head.Method)
    match result with
    | Ok grant ->
        Assert.Equal("access-token-1", grant.AccessToken)
        Assert.Equal("refresh-token-1", grant.RefreshToken)
        Assert.Equal(now.AddSeconds 3600., grant.ExpiresAt)
        Assert.Contains(ChatGptSignIn.planScope, grant.Scopes)
        Assert.False(String.IsNullOrEmpty grant.IdToken)
    | Error () -> failwith "exchange failed" }

[<Fact; Trait("Task", "T003")>]
let ``a failed exchange is an error without the response body`` () = task {
    let fake = new FakeOpenAi(TokenFailure = Some(400, """{"error":"invalid_grant","access_token":"SECRET-TOKEN"}"""))
    use http = clientOf fake
    let! result = OpenAiAuth.exchange http clock "client-test" "code-1" "verifier-1" "http://127.0.0.1:5000/auth/callback" none
    Assert.Equal(Error(), result)
    Assert.DoesNotContain("SECRET-TOKEN", sprintf "%A" result) }

[<Fact; Trait("Task", "T003")>]
let ``refresh posts the refresh fields without scope and returns the rotated tokens`` () = task {
    let fake = new FakeOpenAi(RefreshToken = "refresh-rotated", AccessToken = "access-new")
    use http = clientOf fake
    let! result = OpenAiAuth.refresh http clock credentials none
    Assert.Equal<(string * string) list>(
        [ "client_id", "client-test"; "grant_type", "refresh_token"; "refresh_token", "old-refresh"; "resource", "https://api.openai.com/v1" ],
        fields (tokenRequests fake).Head.Body)
    match result with
    | Ok updated ->
        Assert.Equal("refresh-rotated", updated.RefreshToken)
        Assert.Equal("access-new", updated.AccessToken)
        Assert.Equal(now.AddSeconds 3600., updated.ExpiresAt)
        Assert.Equal(credentials.Subject, updated.Subject)
        Assert.Equal(credentials.HostId, updated.HostId)
    | Error _ -> failwith "refresh failed" }

[<Theory; Trait("Task", "T003")>]
[<InlineData("invalid_grant")>]
[<InlineData("invalid_refresh_token")>]
[<InlineData("token_expired")>]
[<InlineData("refresh_token_expired")>]
[<InlineData("refresh_token_invalidated")>]
[<InlineData("refresh_token_reused")>]
let ``each terminal refresh code ends the sign-in`` (code: string) = task {
    let fake = new FakeOpenAi(TokenFailure = Some(400, $$"""{"error":"{{code}}"}"""))
    use http = clientOf fake
    let! result = OpenAiAuth.refresh http clock credentials none
    Assert.Equal(Error Ended, result) }

[<Fact; Trait("Task", "T003")>]
let ``a nested error code also ends the sign-in`` () = task {
    let fake = new FakeOpenAi(TokenFailure = Some(401, """{"error":{"code":"refresh_token_reused","message":"x"}}"""))
    use http = clientOf fake
    let! result = OpenAiAuth.refresh http clock credentials none
    Assert.Equal(Error Ended, result) }

[<Fact; Trait("Task", "T003")>]
let ``a 5xx keeps the record`` () = task {
    let fake = new FakeOpenAi(TokenFailure = Some(503, """{"error":"invalid_grant"}"""))
    use http = clientOf fake
    let! result = OpenAiAuth.refresh http clock credentials none
    Assert.Equal(Error Unreachable, result) }

type private Throwing() =
    inherit HttpMessageHandler()
    override _.SendAsync(_, _) : Task<HttpResponseMessage> = raise (HttpRequestException "no network")

[<Fact; Trait("Task", "T003")>]
let ``a network error keeps the record and exchange and revoke fail quietly`` () = task {
    use http = new HttpClient(new Throwing())
    let! refreshed = OpenAiAuth.refresh http clock credentials none
    Assert.Equal(Error Unreachable, refreshed)
    let! exchanged = OpenAiAuth.exchange http clock "client-test" "c" "v" "http://127.0.0.1:5000/auth/callback" none
    Assert.Equal(Error(), exchanged)
    let! revoked = OpenAiAuth.revoke http credentials none
    Assert.False revoked }

type private Redirecting() =
    inherit HttpMessageHandler()
    member val Calls = 0 with get, set
    override this.SendAsync(_, _) =
        this.Calls <- this.Calls + 1
        let response = new HttpResponseMessage(HttpStatusCode.Found)
        response.Headers.Location <- Uri "https://evil.example/steal"
        Task.FromResult response

[<Fact; Trait("Task", "T003")>]
let ``a redirect is an error and the handler never follows redirects`` () = task {
    use handler = OpenAiAuth.createHandler ()
    Assert.False((handler :?> SocketsHttpHandler).AllowAutoRedirect)
    let redirecting = new Redirecting()
    use http = new HttpClient(redirecting)
    let! refreshed = OpenAiAuth.refresh http clock credentials none
    Assert.Equal(Error Unreachable, refreshed)
    let! exchanged = OpenAiAuth.exchange http clock "client-test" "c" "v" "http://127.0.0.1:5000/auth/callback" none
    Assert.Equal(Error(), exchanged)
    Assert.Equal(2, redirecting.Calls) }

[<Fact; Trait("Task", "T003")>]
let ``a refresh error body containing a token never reaches the result`` () = task {
    let fake = new FakeOpenAi(TokenFailure = Some(500, """{"refresh_token":"SECRET-TOKEN"}"""))
    use http = clientOf fake
    let! result = OpenAiAuth.refresh http clock credentials none
    Assert.DoesNotContain("SECRET-TOKEN", sprintf "%A" result) }

[<Fact; Trait("Task", "T003")>]
let ``revoke posts the token fields to the discovered endpoint and reports confirmation`` () = task {
    let fake = new FakeOpenAi()
    use http = clientOf fake
    let! confirmed = OpenAiAuth.revoke http credentials none
    Assert.True confirmed
    Assert.Equal<(string * string) list>(
        [ "client_id", "client-test"; "token", "old-refresh"; "token_type_hint", "refresh_token" ],
        fields (fake.RequestsTo("auth.openai.com", "/api/accounts/oauth/revoke")).Head.Body) }

[<Fact; Trait("Task", "T003")>]
let ``revoke is not confirmed when the service refuses`` () = task {
    let fake = new FakeOpenAi(RevocationStatus = 500)
    use http = clientOf fake
    let! confirmed = OpenAiAuth.revoke http credentials none
    Assert.False confirmed }

let private claims (nonce: string) = [ "iss", box "https://auth.openai.com"; "aud", box "client-test"; "sub", box "user-test"; "email", box "researcher@example.test"; "nonce", box nonce; "exp", box (now.AddHours(1).ToUnixTimeSeconds()) ]

let private verify (token: string) = task {
    let fake = new FakeOpenAi()
    use http = clientOf fake
    return! OpenAiAuth.verifyIdToken http clock "client-test" "nonce-1" token none }

[<Fact; Trait("Task", "T003")>]
let ``a correctly signed ID token gives the identity`` () = task {
    let! result = verify (TestIdToken.build "RS256" TestIdToken.keyId (claims "nonce-1"))
    Assert.Equal(Ok { Subject = "user-test"; Account = "researcher@example.test" }, result) }

[<Fact; Trait("Task", "T003")>]
let ``claims are validated after the signature, so a wrong nonce is refused`` () = task {
    let! result = verify (TestIdToken.build "RS256" TestIdToken.keyId (claims "other-nonce"))
    Assert.Equal(Error(OpenAiAuth.Claims NonceMismatch), result) }

[<Fact; Trait("Task", "T003")>]
let ``a tampered payload fails the signature check`` () = task {
    let token = TestIdToken.build "RS256" TestIdToken.keyId (claims "nonce-1")
    let other = TestIdToken.build "RS256" TestIdToken.keyId (claims "nonce-1" |> List.map (fun (n, v) -> if n = "sub" then n, box "attacker" else n, v))
    let forged = String.Join(".", token.Split('.')[0], other.Split('.')[1], token.Split('.')[2])
    let! result = verify forged
    Assert.Equal(Error OpenAiAuth.BadSignature, result) }

[<Theory; Trait("Task", "T003")>]
[<InlineData("HS256")>]
[<InlineData("none")>]
[<InlineData("RS512")>]
let ``any algorithm other than RS256 is rejected`` (algorithm: string) = task {
    let! result = verify (TestIdToken.build algorithm TestIdToken.keyId (claims "nonce-1"))
    Assert.Equal(Error OpenAiAuth.BadSignature, result) }

[<Fact; Trait("Task", "T003")>]
let ``an unknown kid is rejected`` () = task {
    let! result = verify (TestIdToken.build "RS256" "unknown-key" (claims "nonce-1"))
    Assert.Equal(Error OpenAiAuth.BadSignature, result) }

[<Fact; Trait("Task", "T003")>]
let ``a malformed token is rejected`` () = task {
    for token in [ ""; "abc"; "a.b"; "a.b.c"; "!!.!!.!!" ] do
        let! result = verify token
        Assert.Equal(Error OpenAiAuth.BadSignature, result) }
