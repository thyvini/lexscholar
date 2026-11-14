/// Types for signing in with a ChatGPT account (F005, data-model.md). Pure rules
/// (PKCE, authorization address, ID-token claims) arrive with T002.
namespace LexScholar.Domain

open System

/// The stored sign-in record (`chatgpt-signin.json`). The three tokens are secrets:
/// never returned by an endpoint and never logged.
type Credentials =
    { ClientId: string
      HostId: string
      Subject: string
      Account: string
      AccessToken: string
      RefreshToken: string
      IdToken: string
      ExpiresAt: DateTimeOffset
      Scopes: string list
      SignedInAt: DateTimeOffset
      /// Set when she confirmed sending text to her plan in this sign-in.
      ConfirmedAt: DateTimeOffset option }

/// How a sign-in attempt ended, as reported to the client by the callback.
type SignInOutcome =
    | SignedIn
    | Cancelled
    | NoPlanAccess
    | Failed

/// One entry of the account's model list (`slug`, `visibility`).
type ModelEntry = { Slug: string; Visibility: string }

/// Why an ID token's claims were refused.
type IdTokenProblem =
    | Malformed
    | WrongIssuer
    | WrongAudience
    | Expired
    | NonceMismatch

/// Who signed in, from validated claims.
type Identity = { Subject: string; Account: string }

module ChatGptSignIn =
    open System.Security.Cryptography
    open System.Text
    open System.Text.Json

    /// `authorization_endpoint` of https://auth.openai.com/.well-known/openid-configuration (read 2026-11-13).
    let authorizationEndpoint = "https://auth.openai.com/api/accounts/authorize"
    let issuer = "https://auth.openai.com"
    let planScope = "chatgpt.tokens.use.direct"
    let resource = "https://api.openai.com/v1"
    let private scope = $"openid profile email offline_access resource.invoke {planScope}"
    let private refreshWindow = TimeSpan.FromMinutes 10.

    /// PKCE S256 challenge: base64url of the SHA-256 of the verifier, without padding (RFC 7636).
    let challengeOf (verifier: string) : string =
        SHA256.HashData(Encoding.ASCII.GetBytes verifier)
        |> Convert.ToBase64String
        |> fun text -> text.TrimEnd('=').Replace('+', '-').Replace('/', '_')

    /// The address the researcher's browser is sent to. The registration hints are sent
    /// only on a first registration (`hostId` is `Some`).
    let authorizationUrl (clientId: string) (redirectUri: string) (state: string) (nonce: string) (challenge: string) (hostId: string option) : string =
        let parameters =
            [ "response_type", "code"
              "client_id", clientId
              "redirect_uri", redirectUri
              "scope", scope
              "resource", resource
              "state", state
              "nonce", nonce
              "code_challenge", challenge
              "code_challenge_method", "S256" ]
            @ match hostId with
              | Some host -> [ "agent_name_hint", "LexScholar"; "ext_agent_host_id", host ]
              | None -> []
        let query = parameters |> List.map (fun (name, value) -> $"{name}={Uri.EscapeDataString value}") |> String.concat "&"
        $"{authorizationEndpoint}?{query}"

    let private stringClaim (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetString())) -> Some(value.GetString())
        | _ -> None

    /// Checks the claims of an ID token whose signature was already verified: issuer, audience
    /// (the issued client id), expiry and nonce. The account is the email, else the name.
    let validateIdToken (now: DateTimeOffset) (clientId: string) (nonce: string) (claimsJson: string) : Result<Identity, IdTokenProblem> =
        try
            use document = JsonDocument.Parse claimsJson
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then Error Malformed else
            let audiences =
                match root.TryGetProperty "aud" with
                | true, value when value.ValueKind = JsonValueKind.String -> Some [ value.GetString() ]
                | true, value when value.ValueKind = JsonValueKind.Array ->
                    Some [ for item in value.EnumerateArray() do if item.ValueKind = JsonValueKind.String then item.GetString() ]
                | _ -> None
            let expiry =
                match root.TryGetProperty "exp" with
                | true, value when value.ValueKind = JsonValueKind.Number ->
                    match value.TryGetInt64() with
                    | true, seconds -> Some(DateTimeOffset.FromUnixTimeSeconds seconds)
                    | _ -> None
                | _ -> None
            match stringClaim root "iss", audiences, expiry, stringClaim root "nonce", stringClaim root "sub" with
            | Some iss, _, _, _, _ when iss <> issuer -> Error WrongIssuer
            | _, Some auds, _, _, _ when not (List.contains clientId auds) -> Error WrongAudience
            | _, _, Some exp, _, _ when exp <= now -> Error Expired
            | _, _, _, Some actual, _ when actual <> nonce -> Error NonceMismatch
            | Some _, Some _, Some _, Some _, Some subject ->
                let account =
                    stringClaim root "email"
                    |> Option.orElse (stringClaim root "name")
                    |> Option.defaultValue "your ChatGPT account"
                Ok { Subject = subject; Account = account }
            | _ -> Error Malformed
        with
        | :? JsonException -> Error Malformed
        | :? ArgumentOutOfRangeException -> Error Malformed

    /// Plan usage exists only when the granted scope (space separated) contains the direct-token scope.
    let grantsPlanUsage (scopes: string) : bool =
        scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.contains planScope

    /// True when the access token expires within ten minutes.
    let needsRefresh (now: DateTimeOffset) (credentials: Credentials) : bool =
        credentials.ExpiresAt <= now + refreshWindow

    /// The first model the account lists (`visibility` is `list`).
    let pickModel (models: ModelEntry list) : string option =
        models |> List.tryFind (fun model -> model.Visibility = "list") |> Option.map (fun model -> model.Slug)
