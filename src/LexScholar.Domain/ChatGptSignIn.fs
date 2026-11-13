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
    let authorizationEndpoint = "https://auth.openai.com/oauth/authorize"
    let issuer = "https://auth.openai.com"
    let planScope = "chatgpt.tokens.use.direct"
    let resource = "https://api.openai.com/v1"

    let challengeOf (verifier: string) : string = ""

    let authorizationUrl (clientId: string) (redirectUri: string) (state: string) (nonce: string) (challenge: string) (hostId: string option) : string = ""

    let validateIdToken (now: DateTimeOffset) (clientId: string) (nonce: string) (claimsJson: string) : Result<Identity, IdTokenProblem> =
        Error Malformed

    let grantsPlanUsage (scopes: string) : bool = false

    let needsRefresh (now: DateTimeOffset) (credentials: Credentials) : bool = false

    let pickModel (models: ModelEntry list) : string option = None
