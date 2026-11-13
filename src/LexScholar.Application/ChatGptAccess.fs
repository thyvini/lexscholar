/// Capabilities of the ChatGPT sign-in (F005). Workflows (`startSignIn`,
/// `completeSignIn`, `signOut`, `accessToken`) arrive with T004, T005 and T007;
/// each takes only the functions it needs.
module LexScholar.Application.ChatGptAccess

open System.Threading
open System.Threading.Tasks
open LexScholar.Domain

/// The stored sign-in record. `Save` and `Delete` are the only writers.
type CredentialStore =
    { Read: unit -> Credentials option
      Save: Credentials -> unit
      Delete: unit -> unit }

/// Why a refresh did not give new tokens.
type RefreshFailure =
    /// A terminal error: the sign-in has ended.
    | Ended
    /// Network error or 5xx: the record is kept.
    | Unreachable

/// Calls to the sign-in service (auth.openai.com). Errors never carry response bodies.
type AuthClient =
    { /// code, code verifier, redirect address -> access token, refresh token, ID token
      Exchange: string -> string -> string -> CancellationToken -> Task<Result<string * string * string, unit>>
      Refresh: Credentials -> CancellationToken -> Task<Result<Credentials, RefreshFailure>>
      Revoke: Credentials -> CancellationToken -> Task<bool> }
