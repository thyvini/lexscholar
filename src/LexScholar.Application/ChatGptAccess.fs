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


/// One running summary through her plan. Its token is cancelled when she signs out or the sign-in
/// ends (research section 11); disposing it unregisters the run.
type SendRun(source: CancellationTokenSource, release: unit -> unit) =
    member _.Token = source.Token
    interface IDisposable with
        member _.Dispose() = release ()

/// The one lock around the stored credentials. Every read, save and delete of the record goes
/// through `Locked`, so a read never deletes the temporary file of a save in progress and later
/// slices (sign-out, refresh) serialize with sign-in the same way. It also knows the summaries
/// running on the current sign-in, so ending the sign-in can cancel them.
type Vault(store: CredentialStore) =
    let gate = new SemaphoreSlim(1, 1)
    let runSync = obj ()
    let runs = System.Collections.Generic.List<CancellationTokenSource>()
    /// Registers a run that also ends with `outer`. Call it inside `Locked`, so an end of the sign-in cannot slip in before.
    member _.BeginRun(outer: CancellationToken) : SendRun =
        let source = CancellationTokenSource.CreateLinkedTokenSource outer
        lock runSync (fun () -> runs.Add source)
        new SendRun(source, fun () -> lock runSync (fun () -> runs.Remove source |> ignore; source.Dispose()))
    /// Cancels every registered run. Call it inside `Locked`, together with the deletion of the record.
    member _.EndRuns() =
        lock runSync (fun () ->
            for source in runs do source.Cancel()
            runs.Clear())
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
    // Cancels the exchange of the attempt that is mid-exchange.
    let mutable running : CancellationTokenSource option = None
    let endRunning () =
        running |> Option.iter (fun source -> source.Cancel())
        running <- None
    /// A start replaces the pending attempt and also supersedes one that is mid-exchange.
    member _.Start(attempt: PendingSignIn) =
        lock sync (fun () ->
            pending <- Some attempt
            inFlight <- None
            endRunning ())
    /// Forgets the pending attempt, supersedes one that is mid-exchange and cancels its calls.
    /// Sign-out calls this under the vault lock, so no attempt saves after it.
    member _.Clear() =
        lock sync (fun () ->
            pending <- None
            inFlight <- None
            endRunning ())
    /// Whether a newer start replaced this attempt after it was taken.
    member _.Superseded(attempt: PendingSignIn) : bool = lock sync (fun () -> inFlight <> Some attempt.State)
    /// Consumes the attempt when `state` matches and it has not expired; a reused state finds nothing.
    /// The token is cancelled when a start or a sign-out supersedes the attempt.
    member _.Take(state: string, now: DateTimeOffset) : (PendingSignIn * CancellationToken) option =
        lock sync (fun () ->
            match pending with
            | Some attempt when attempt.State = state && attempt.ExpiresAt > now ->
                endRunning ()
                pending <- None
                inFlight <- Some attempt.State
                let source = new CancellationTokenSource()
                running <- Some source
                Some(attempt, source.Token)
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
let startSignIn (vault: Vault) (session: SignInSession) (clock: unit -> DateTimeOffset) (redirectUri: string) : Task<Result<string, unit>> =
    // The check and the replacement run under the vault lock, the same lock the final
    // check-and-save of a callback holds, so a callback sees the replacement or finishes first.
    vault.Locked(fun store -> task {
        if store.Read().IsSome then return Error()
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
                    (ChatGptSignIn.challengeOf attempt.Verifier) (Some attempt.HostId)) })

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
        | Some(attempt, superseded) ->
            use linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, superseded)
            let cancellation = linked.Token
            // Cleanup revocations must not inherit a cancellation caused by sign-out or a newer
            // start; `Revoke` has its own short time limit.
            let cleanup = CancellationToken.None
            // A sign-out or a newer start cancelled the calls: that is not a failure.
            let failedUnlessSuperseded attempt = if session.Superseded attempt then Cancelled else Failed
            match query.Code, query.ClientId with
            | Some code, Some clientId when clientId <> "" && clientId <> "dynamic_agent_client" ->
                try
                    match! auth.Exchange clientId code attempt.Verifier attempt.RedirectUri cancellation with
                    | Error () -> return failedUnlessSuperseded attempt
                    | Ok grant ->
                        // The tokens exist from here on: any path that does not save them revokes them.
                        let revokeIssued () =
                            task {
                                try
                                    let issued =
                                        { ClientId = clientId; HostId = attempt.HostId; Subject = ""; Account = ""
                                          AccessToken = grant.AccessToken; RefreshToken = grant.RefreshToken; IdToken = grant.IdToken
                                          ExpiresAt = grant.ExpiresAt; Scopes = grant.Scopes; SignedInAt = clock (); ConfirmedAt = None }
                                    let! _ = auth.Revoke issued cleanup
                                    ()
                                with _ -> ()
                            }
                        let! verified =
                            task {
                                try return! auth.VerifyIdToken clientId attempt.Nonce grant.IdToken cancellation
                                with _ -> return Error()
                            }
                        match verified with
                        | Error () ->
                            do! revokeIssued ()
                            return failedUnlessSuperseded attempt
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
                                let! _ = auth.Revoke credentials cleanup
                                return NoPlanAccess
                            else
                                // A sign-in that finished first wins, and one replaced by a newer start is never saved.
                                // If the save itself throws, the issued tokens are revoked like any other unsaved grant.
                                let! saved =
                                    vault.Locked(fun store -> task {
                                        if store.Read().IsSome || session.Superseded attempt then return Some false
                                        else
                                            try
                                                store.Save credentials
                                                return Some true
                                            with _ -> return None })
                                match saved with
                                | Some true -> return SignedIn
                                | Some false ->
                                    let! _ = auth.Revoke credentials cleanup
                                    return Cancelled
                                | None ->
                                    let! _ = auth.Revoke credentials cleanup
                                    return Failed
                with _ ->
                    return failedUnlessSuperseded attempt
            | _ -> return Failed }

/// Signs out. Under the vault lock: supersedes any pending or mid-exchange attempt (cancelling
/// its calls), deletes the record, then asks the service to revoke the stored sign-in (best effort). `revoked` is whether the service confirmed. Lock order is vault, then session.
let signOut (vault: Vault) (session: SignInSession) (auth: AuthClient) (cancellation: CancellationToken) : Task<bool> =
    vault.Locked(fun store -> task {
        session.Clear()
        vault.EndRuns()
        // The local removal is the guarantee: delete first, then revoke from memory, best effort.
        let credentials = store.Read()
        store.Delete()
        let! revoked =
            task {
                match credentials with
                | None -> return false
                | Some credentials ->
                    try return! auth.Revoke credentials cancellation
                    with _ -> return false
            }
        return revoked })


/// What `prepareSend` found for a summary through her plan.
type SendPreparation =
    /// Confirmed, and the access token is good for the run; the run is registered and ends with a sign-out.
    | Prepared of Credentials * SendRun
    /// Not confirmed in this sign-in and the request did not confirm: nothing was refreshed or sent.
    | AwaitingConfirmation of account: string
    /// No sign-in (never, or signed out).
    | NoSignIn
    /// The refresh ended the sign-in (terminal error): the record was deleted and running summaries cancelled.
    | SignInEnded
    /// The refresh failed on the network or with a non-terminal answer; the record is kept.
    | RefreshUnreachable

/// Everything that must hold before text goes to her plan, in one locked step on one record:
/// the confirmation (recorded only when the run goes on), the refresh when the access token
/// expires within ten minutes, and the save of the result. Confirmation is checked before any
/// network call. A refresh saves only if the record still exists with the same refresh token.
/// Lock order is vault, then session.
let prepareSend (vault: Vault) (auth: AuthClient) (clock: unit -> DateTimeOffset) (confirmSend: bool) (cancellation: CancellationToken) : Task<SendPreparation> =
    vault.Locked(fun store -> task {
        match store.Read() with
        | None -> return NoSignIn
        | Some record ->
            let confirming = record.ConfirmedAt.IsNone
            if confirming && not confirmSend then return AwaitingConfirmation record.Account
            else
                let stamp (credentials: Credentials) =
                    { credentials with ConfirmedAt = (if confirming then Some(clock ()) else record.ConfirmedAt) }
                if not (ChatGptSignIn.needsRefresh (clock ()) record) then
                    let ready = stamp record
                    if confirming then store.Save ready
                    return Prepared(ready, vault.BeginRun cancellation)
                else
                    let! refreshed =
                        task {
                            try return! auth.Refresh record cancellation
                            with error when not (error :? OperationCanceledException) -> return Error RefreshFailure.Unreachable
                        }
                    match refreshed with
                    | Error RefreshFailure.Ended ->
                        store.Delete()
                        vault.EndRuns()
                        return SignInEnded
                    | Error RefreshFailure.Unreachable -> return RefreshUnreachable
                    | Ok renewed ->
                        match store.Read() with
                        | Some current when current.RefreshToken = record.RefreshToken ->
                            let ready = stamp renewed
                            store.Save ready
                            return Prepared(ready, vault.BeginRun cancellation)
                        | _ -> return NoSignIn })


/// Ends the sign-in a run used after the service refused it (signed out, no plan access): deletes the
/// record and cancels the running summaries, but only if the record is still that sign-in, so a
/// newer sign-in is never deleted.
let endSignIn (vault: Vault) (ended: Credentials) : Task<unit> =
    vault.Locked(fun store -> task {
        match store.Read() with
        | Some current when current.HostId = ended.HostId ->
            store.Delete()
            vault.EndRuns()
        | _ -> () })
