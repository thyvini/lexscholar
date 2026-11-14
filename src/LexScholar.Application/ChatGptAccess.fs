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
    // The state of the attempt whose exchange is running, if it is still the current one.
    let mutable inFlight : string option = None
    /// A start replaces the pending attempt and also supersedes one that is mid-exchange.
    member _.Start(attempt: PendingSignIn) =
        lock sync (fun () ->
            pending <- Some attempt
            inFlight <- None)
    /// Whether a newer start replaced this attempt after it was taken.
    member _.Superseded(attempt: PendingSignIn) : bool = lock sync (fun () -> inFlight <> Some attempt.State)
    /// Consumes the attempt when `state` matches and it has not expired; a reused state finds nothing.
    member _.Take(state: string, now: DateTimeOffset) : PendingSignIn option =
        lock sync (fun () ->
            match pending with
            | Some attempt when attempt.State = state && attempt.ExpiresAt > now ->
                pending <- None
                inFlight <- Some attempt.State
                Some attempt
            | _ -> None)

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
let startSignIn (vault: Vault) (session: SignInSession) (clock: unit -> DateTimeOffset) (redirectUri: string) : Task<Result<string, unit>> = task {
    let! existing = vault.Read()
    if existing.IsSome then return Error()
    else
        let attempt =
            { State = randomToken ()
              Nonce = randomToken ()
              Verifier = randomToken ()
              RedirectUri = redirectUri
              HostId = $"urn:uuid:{Guid.NewGuid()}"
              ExpiresAt = clock().Add pendingLifetime }
        session.Start attempt
        return
            Ok(ChatGptSignIn.authorizationUrl "dynamic_agent_client" redirectUri attempt.State attempt.Nonce
                (ChatGptSignIn.challengeOf attempt.Verifier) (Some attempt.HostId)) }

/// Finishes a sign-in at the callback. Nothing is saved until the exchange and every check
/// succeed, so any other path leaves her signed out.
let completeSignIn (vault: Vault) (session: SignInSession) (auth: AuthClient) (clock: unit -> DateTimeOffset) (query: CallbackQuery) (cancellation: CancellationToken) : Task<SignInOutcome> = task {
    match query.Error, query.State with
    | Some error, state ->
        // Only the attempt this error belongs to ends; a stale, reused or missing state leaves
        // the current one alone and is cancelled.
        let live = state |> Option.bind (fun state -> session.Take(state, clock ()))
        return (if live.IsNone || error = "access_denied" then Cancelled else Failed)
    | None, None -> return Cancelled
    | None, Some state ->
        match session.Take(state, clock ()) with
        | None -> return Cancelled
        | Some attempt ->
            match query.Code, query.ClientId with
            | Some code, Some clientId when clientId <> "" && clientId <> "dynamic_agent_client" ->
                try
                    match! auth.Exchange clientId code attempt.Verifier attempt.RedirectUri cancellation with
                    | Error () -> return Failed
                    | Ok grant ->
                        match! auth.VerifyIdToken clientId attempt.Nonce grant.IdToken cancellation with
                        | Error () -> return Failed
                        | Ok identity ->
                            let credentials =
                                { ClientId = clientId
                                  HostId = attempt.HostId
                                  Subject = identity.Subject
                                  Account = identity.Account
                                  AccessToken = grant.AccessToken
                                  RefreshToken = grant.RefreshToken
                                  IdToken = grant.IdToken
                                  ExpiresAt = grant.ExpiresAt
                                  Scopes = grant.Scopes
                                  SignedInAt = clock ()
                                  ConfirmedAt = None }
                            if not (ChatGptSignIn.grantsPlanUsage (String.Join(' ', grant.Scopes))) then
                                let! _ = auth.Revoke credentials cancellation
                                return NoPlanAccess
                            else
                                // A sign-in that finished first wins, and one replaced by a newer start is never saved.
                                let! saved =
                                    vault.Locked(fun store -> task {
                                        if store.Read().IsSome || session.Superseded attempt then return false
                                        else
                                            store.Save credentials
                                            return true })
                                if saved then return SignedIn
                                else
                                    let! _ = auth.Revoke credentials cancellation
                                    return Cancelled
                with _ -> return Failed
            | _ -> return Failed }
