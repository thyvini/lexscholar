/// The ChatGPT sign-in (F005). `startSignIn` and `completeSignIn` arrive with T004;
/// `signOut` and `accessToken` follow in T005 and T007. Each takes only what it needs.
module LexScholar.Application.ChatGptAccess

open System
open System.Security.Cryptography
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

/// What the token endpoint issued for an authorization code.
type TokenGrant =
    { AccessToken: string
      RefreshToken: string
      IdToken: string
      ExpiresAt: System.DateTimeOffset
      Scopes: string list }

/// Calls to the sign-in service (auth.openai.com). Errors never carry response bodies.
type AuthClient =
    { /// issued client id, code, code verifier, redirect address -> the issued tokens, expiry and granted scopes
      Exchange: string -> string -> string -> string -> CancellationToken -> Task<Result<TokenGrant, unit>>
      /// issued client id, nonce, ID token -> who signed in, once signature and claims check out
      VerifyIdToken: string -> string -> string -> CancellationToken -> Task<Result<Identity, unit>>
      Refresh: Credentials -> CancellationToken -> Task<Result<Credentials, RefreshFailure>>
      Revoke: Credentials -> CancellationToken -> Task<bool> }


/// The one lock around the stored credentials. Every read, save and delete of the record goes
/// through `Locked`, so a read never deletes the temporary file of a save in progress and later
/// slices (sign-out, refresh) serialize with sign-in the same way.
type Vault(store: CredentialStore) =
    let gate = new SemaphoreSlim(1, 1)
    member _.Locked(work: CredentialStore -> Task<'a>) : Task<'a> = task {
        do! gate.WaitAsync()
        try return! work store
        finally gate.Release() |> ignore }
    member this.Read() : Task<Credentials option> = this.Locked(fun s -> Task.FromResult(s.Read()))

/// A started sign-in that has not come back yet. Memory only, never written (data-model.md).
type PendingSignIn =
    { State: string
      Nonce: string
      Verifier: string
      RedirectUri: string
      HostId: string
      ExpiresAt: DateTimeOffset }

/// At most one pending sign-in; a new start replaces the previous one.
type SignInSession() =
    let sync = obj ()
    let mutable pending : PendingSignIn option = None
    member _.Start(attempt: PendingSignIn) = lock sync (fun () -> pending <- Some attempt)
    /// Consumes the attempt when `state` matches and it has not expired; a reused state finds nothing.
    member _.Take(state: string, now: DateTimeOffset) : PendingSignIn option =
        lock sync (fun () ->
            match pending with
            | Some attempt when attempt.State = state && attempt.ExpiresAt > now ->
                pending <- None
                Some attempt
            | _ -> None)
    member _.Clear() = lock sync (fun () -> pending <- None)

/// What came back to the callback address.
type CallbackQuery =
    { Code: string option
      State: string option
      Error: string option
      /// The client id issued at registration.
      ClientId: string option }

let private pendingLifetime = TimeSpan.FromMinutes 10.

let private randomToken () =
    RandomNumberGenerator.GetBytes 32 |> Convert.ToBase64String |> fun text -> text.TrimEnd('=').Replace('+', '-').Replace('/', '_')

/// Starts a sign-in: new state, nonce, code verifier and host id; returns the address her
/// browser goes to. `Error ()` when she is already signed in. Nothing is stored.
let startSignIn (vault: Vault) (session: SignInSession) (clock: unit -> DateTimeOffset) (redirectUri: string) : Task<Result<string, unit>> =
    Task.FromResult(Ok "")

/// Finishes a sign-in at the callback. Nothing is saved until the exchange and every check
/// succeed, so any other path leaves her signed out.
let completeSignIn (vault: Vault) (session: SignInSession) (auth: AuthClient) (clock: unit -> DateTimeOffset) (query: CallbackQuery) (cancellation: CancellationToken) : Task<SignInOutcome> =
    Task.FromResult Failed
