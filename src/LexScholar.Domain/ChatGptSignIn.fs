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
