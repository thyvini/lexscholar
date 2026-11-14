/// Client for OpenAI's sign-in service (F005, research.md sections 5 and 7). Only
/// auth.openai.com over HTTPS, no redirects (see `createHandler`). Response bodies are read
/// only to pick fields out of them; they never enter an error value or a log line.
module LexScholar.Infrastructure.OpenAiAuth

open System
open System.Collections.Generic
open System.IO
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Application.ChatGptAccess
open LexScholar.Domain

/// Why an ID token was refused.
type IdTokenFailure =
    | BadSignature
    | KeyUnavailable
    | Claims of IdTokenProblem

let private tokenEndpoint = Uri "https://auth.openai.com/api/accounts/oauth/token"
let private discoveryEndpoint = Uri "https://auth.openai.com/.well-known/openid-configuration"
let private jwksEndpoint = Uri "https://auth.openai.com/.well-known/jwks.json"
let private authHost = "auth.openai.com"
let private revokeTimeout = TimeSpan.FromSeconds 5.

let private terminalCodes =
    set [ "invalid_grant"; "invalid_refresh_token"; "token_expired"; "refresh_token_expired"; "refresh_token_invalidated"; "refresh_token_reused" ]

/// The handler for the outgoing client: never follows redirects.
let createHandler () : HttpMessageHandler = new SocketsHttpHandler(AllowAutoRedirect = false) :> HttpMessageHandler

let private postForm (http: HttpClient) (uri: Uri) (fields: (string * string) list) (cancellation: CancellationToken) = task {
    use content = new FormUrlEncodedContent(fields |> List.map (fun (name, value) -> KeyValuePair(name, value)))
    use! response = http.PostAsync(uri, content, cancellation)
    let! body = response.Content.ReadAsStringAsync cancellation
    return int response.StatusCode, body }

let private tryParse (body: string) : JsonDocument option =
    try Some(JsonDocument.Parse body) with :? JsonException -> None

let private text (element: JsonElement) (name: string) =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.String && not (String.IsNullOrEmpty(value.GetString())) -> Some(value.GetString())
    | _ -> None

/// `error` as a string, or `error.code`.
let private errorCode (body: string) : string option =
    tryParse body
    |> Option.bind (fun document ->
        use document = document
        let root = document.RootElement
        if root.ValueKind <> JsonValueKind.Object then None else
        match root.TryGetProperty "error" with
        | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
        | true, value when value.ValueKind = JsonValueKind.Object -> text value "code"
        | _ -> None)

/// The tokens of a successful token response.
let private readGrant (clock: unit -> DateTimeOffset) (body: string) : (TokenGrant * string option) option =
    tryParse body
    |> Option.bind (fun document ->
        use document = document
        let root = document.RootElement
        if root.ValueKind <> JsonValueKind.Object then None else
        let seconds =
            match root.TryGetProperty "expires_in" with
            | true, value when value.ValueKind = JsonValueKind.Number ->
                match value.TryGetInt32() with
                | true, seconds when seconds > 0 -> Some seconds
                | _ -> None
            | _ -> None
        match text root "access_token", seconds with
        | Some access, Some seconds ->
            let grant =
                { AccessToken = access
                  RefreshToken = text root "refresh_token" |> Option.defaultValue ""
                  IdToken = text root "id_token" |> Option.defaultValue ""
                  ExpiresAt = clock().AddSeconds(float seconds)
                  Scopes = text root "scope" |> Option.map (fun scope -> scope.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray) |> Option.defaultValue [] }
            Some(grant, text root "id_token")
        | _ -> None)

let private attempt (work: unit -> Task<'a>) (onFailure: 'a) : Task<'a> = task {
    try return! work ()
    with
    | :? HttpRequestException | :? TaskCanceledException | :? IOException -> return onFailure }

let exchange (http: HttpClient) (clock: unit -> DateTimeOffset) (clientId: string) (code: string) (verifier: string) (redirectUri: string) (cancellation: CancellationToken) : Task<Result<TokenGrant, unit>> =
    attempt
        (fun () -> task {
            let! status, body =
                postForm http tokenEndpoint
                    [ "grant_type", "authorization_code"; "client_id", clientId; "code", code
                      "code_verifier", verifier; "redirect_uri", redirectUri; "resource", ChatGptSignIn.resource ]
                    cancellation
            if status <> 200 then return Error()
            else
                match readGrant clock body with
                | Some (grant, _) when grant.RefreshToken <> "" && grant.IdToken <> "" -> return Ok grant
                | _ -> return Error() })
        (Error())

let refresh (http: HttpClient) (clock: unit -> DateTimeOffset) (credentials: Credentials) (cancellation: CancellationToken) : Task<Result<Credentials, RefreshFailure>> =
    attempt
        (fun () -> task {
            let! status, body =
                postForm http tokenEndpoint
                    [ "grant_type", "refresh_token"; "client_id", credentials.ClientId
                      "refresh_token", credentials.RefreshToken; "resource", ChatGptSignIn.resource ]
                    cancellation
            if status = 200 then
                match readGrant clock body with
                | Some (grant, _) ->
                    return
                        Ok { credentials with
                                AccessToken = grant.AccessToken
                                RefreshToken = (if grant.RefreshToken = "" then credentials.RefreshToken else grant.RefreshToken)
                                IdToken = (if grant.IdToken = "" then credentials.IdToken else grant.IdToken)
                                ExpiresAt = grant.ExpiresAt
                                Scopes = (if grant.Scopes.IsEmpty then credentials.Scopes else grant.Scopes) }
                | None -> return Error Unreachable
            elif status >= 400 && status < 500 && (errorCode body |> Option.exists terminalCodes.Contains) then return Error Ended
            else return Error Unreachable })
        (Error Unreachable)

/// Whether the service confirmed the revocation. Any failure is `false`; it never throws.
let revoke (http: HttpClient) (credentials: Credentials) (cancellation: CancellationToken) : Task<bool> =
    attempt
        (fun () -> task {
            use limit = CancellationTokenSource.CreateLinkedTokenSource cancellation
            limit.CancelAfter revokeTimeout
            use! discovery = http.GetAsync(discoveryEndpoint, limit.Token)
            if not discovery.IsSuccessStatusCode then return false else
            let! body = discovery.Content.ReadAsStringAsync limit.Token
            let endpoint =
                tryParse body
                |> Option.bind (fun document ->
                    use document = document
                    if document.RootElement.ValueKind <> JsonValueKind.Object then None
                    else text document.RootElement "revocation_endpoint")
                |> Option.bind (fun address ->
                    match Uri.TryCreate(address, UriKind.Absolute) with
                    | true, uri when uri.Scheme = Uri.UriSchemeHttps && uri.Host = authHost -> Some uri
                    | _ -> None)
            match endpoint with
            | None -> return false
            | Some uri ->
                let! status, _ =
                    postForm http uri
                        [ "token", credentials.RefreshToken; "token_type_hint", "refresh_token"; "client_id", credentials.ClientId ]
                        limit.Token
                return status >= 200 && status < 300 })
        false

let private base64UrlDecode (value: string) : byte array option =
    try
        let padded = value.Replace('-', '+').Replace('_', '/')
        let padded = padded + String('=', (4 - padded.Length % 4) % 4)
        Some(Convert.FromBase64String padded)
    with :? FormatException -> None

let private fetchJwks (http: HttpClient) (cancellation: CancellationToken) : Task<string option> =
    attempt
        (fun () -> task {
            use! response = http.GetAsync(jwksEndpoint, cancellation)
            if response.IsSuccessStatusCode then
                let! body = response.Content.ReadAsStringAsync cancellation
                return Some body
            else return None })
        None

let private headerKeyId (header: byte array) : string option =
    tryParse (Encoding.UTF8.GetString header)
    |> Option.bind (fun document ->
        use document = document
        let root = document.RootElement
        if root.ValueKind <> JsonValueKind.Object then None
        else
            match text root "alg", text root "kid" with
            | Some "RS256", Some kid -> Some kid
            | _ -> None)

let private findKey (jwks: string) (kid: string) : RSAParameters option =
    tryParse jwks
    |> Option.bind (fun document ->
        use document = document
        let root = document.RootElement
        match root.ValueKind, root.TryGetProperty "keys" with
        | JsonValueKind.Object, (true, keys) when keys.ValueKind = JsonValueKind.Array ->
            [ for item in keys.EnumerateArray() do
                if item.ValueKind = JsonValueKind.Object && text item "kid" = Some kid && text item "kty" = Some "RSA" then
                    match text item "n" |> Option.bind base64UrlDecode, text item "e" |> Option.bind base64UrlDecode with
                    | Some n, Some e -> yield RSAParameters(Modulus = n, Exponent = e)
                    | _ -> () ]
            |> List.tryHead
        | _ -> None)

let private signatureValid (parameters: RSAParameters) (signingInput: string) (signature: byte array) : bool =
    try
        use rsa = RSA.Create()
        rsa.ImportParameters parameters
        rsa.VerifyData(Encoding.ASCII.GetBytes signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
    with :? CryptographicException -> false

/// Checks the RS256 signature against the published key with the token's `kid`, then the
/// claims (issuer, audience, expiry, nonce). Any other algorithm or unknown `kid` is rejected.
let verifyIdToken (http: HttpClient) (clock: unit -> DateTimeOffset) (clientId: string) (nonce: string) (idToken: string) (cancellation: CancellationToken) : Task<Result<Identity, IdTokenFailure>> =
    let parts = idToken.Split('.')
    let decoded = if parts.Length = 3 then parts |> Array.map base64UrlDecode else [||]
    if decoded.Length <> 3 || decoded |> Array.exists Option.isNone then Task.FromResult(Error BadSignature) else
    match headerKeyId decoded[0].Value with
    | None -> Task.FromResult(Error BadSignature)
    | Some kid ->
        task {
            let! jwks = fetchJwks http cancellation
            match jwks with
            | None -> return Error KeyUnavailable
            | Some body ->
                match findKey body kid with
                | Some parameters when signatureValid parameters (parts[0] + "." + parts[1]) decoded[2].Value ->
                    return ChatGptSignIn.validateIdToken (clock ()) clientId nonce (Encoding.UTF8.GetString decoded[1].Value) |> Result.mapError Claims
                | _ -> return Error BadSignature
        }
