/// Sign-in workflows (F005 T004) over in-memory fakes: what is stored, and that every
/// unsuccessful path stores nothing.
module LexScholar.Application.Tests.ChatGptAccessTests

open System
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Application.ChatGptAccess
open LexScholar.Domain
open Xunit

let private now = DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero)
let private none = CancellationToken.None
let private redirect = "http://127.0.0.1:5000/auth/callback"

type private Rig(?scopes: string list, ?exchange: Result<TokenGrant, unit>, ?identity: Result<Identity, unit>, ?saveFails: bool, ?hold: Task) =
    let mutable record : Credentials option = None
    let mutable exchanges = 0
    let mutable revocations = 0
    let mutable saves = 0
    let grant =
        { AccessToken = "access"; RefreshToken = "refresh"; IdToken = "id"; ExpiresAt = now.AddHours 1.
          Scopes = defaultArg scopes [ "openid"; ChatGptSignIn.planScope ] }
    let store =
        { Read = fun () -> record
          Save = fun c -> saves <- saves + 1; (if defaultArg saveFails false then failwith "disk full"); record <- Some c
          Delete = fun () -> record <- None }
    let auth =
        { Exchange =
            fun _ _ _ _ _ ->
                task {
                    exchanges <- exchanges + 1
                    match hold with
                    | Some pause -> do! pause
                    | None -> ()
                    return defaultArg exchange (Ok grant)
                }
          VerifyIdToken = fun _ _ _ _ -> Task.FromResult(defaultArg identity (Ok { Subject = "user-1"; Account = "researcher@example.test" }))
          Refresh = fun _ _ -> Task.FromResult(Error Ended)
          Revoke = fun _ _ -> revocations <- revocations + 1; Task.FromResult true }
    member val Vault = Vault(store)
    member val Session = SignInSession()
    member _.Auth = auth
    member _.Record = record
    member _.Exchanges = exchanges
    member _.Revocations = revocations
    member this.Start() =
        match (startSignIn this.Vault this.Session (fun () -> now) redirect).GetAwaiter().GetResult() with
        | Ok url -> Uri url
        | Error () -> failwith "already signed in"
    member this.Complete(query) = (completeSignIn this.Vault this.Session this.Auth (fun () -> now) query none).GetAwaiter().GetResult()

let private parameters (url: Uri) =
    url.Query.TrimStart('?').Split('&') |> Array.map (fun p -> let parts = p.Split('=', 2) in parts[0], Uri.UnescapeDataString parts[1]) |> dict

let private callback (url: Uri) =
    { Code = Some "code-1"; State = Some((parameters url).["state"]); Error = None; ClientId = Some "client-issued" }

[<Fact; Trait("Task", "T004")>]
let ``a completed sign-in stores a record with every field of the data model`` () =
    let rig = Rig()
    let url = rig.Start()
    let p = parameters url
    Assert.Equal("dynamic_agent_client", p["client_id"])
    Assert.StartsWith("urn:uuid:", p["ext_agent_host_id"])
    Assert.Equal(redirect, p["redirect_uri"])
    Assert.Equal(SignedIn, rig.Complete(callback url))
    let record = rig.Record.Value
    Assert.Equal("client-issued", record.ClientId)
    Assert.Equal(p["ext_agent_host_id"], record.HostId)
    Assert.Equal("user-1", record.Subject)
    Assert.Equal("researcher@example.test", record.Account)
    Assert.Equal("access", record.AccessToken)
    Assert.Equal("refresh", record.RefreshToken)
    Assert.Equal("id", record.IdToken)
    Assert.Equal(now.AddHours 1., record.ExpiresAt)
    Assert.Contains(ChatGptSignIn.planScope, record.Scopes)
    Assert.Equal(now, record.SignedInAt)
    Assert.Equal(None, record.ConfirmedAt)

[<Fact; Trait("Task", "T004")>]
let ``starting while signed in is refused and each start uses fresh secrets`` () =
    let rig = Rig()
    let first = parameters (rig.Start())
    let second = parameters (rig.Start())
    Assert.NotEqual<string>(first["state"], second["state"])
    Assert.NotEqual<string>(first["nonce"], second["nonce"])
    Assert.NotEqual<string>(first["code_challenge"], second["code_challenge"])
    rig.Complete { Code = Some "c"; State = Some second["state"]; Error = None; ClientId = Some "client-issued" } |> ignore
    Assert.True((startSignIn rig.Vault rig.Session (fun () -> now) redirect).GetAwaiter().GetResult() = Error ())

[<Fact; Trait("Task", "T004")>]
let ``a new start replaces the pending attempt and a reused or unknown state is cancelled`` () =
    let rig = Rig()
    let old = rig.Start()
    let current = rig.Start()
    Assert.Equal(Cancelled, rig.Complete(callback old))
    Assert.Equal(SignedIn, rig.Complete(callback current))
    Assert.Equal(Cancelled, rig.Complete(callback current))
    Assert.Equal(Cancelled, rig.Complete { callback current with State = Some "nope" })
    Assert.Equal(1, rig.Exchanges)

[<Fact; Trait("Task", "T004")>]
let ``access_denied and no pending attempt are cancelled without an exchange`` () =
    let rig = Rig()
    let url = rig.Start()
    Assert.Equal(Cancelled, rig.Complete { callback url with Code = None; Error = Some "access_denied" })
    Assert.Equal(Cancelled, rig.Complete(callback url))
    Assert.Equal(0, rig.Exchanges)
    Assert.Equal(None, rig.Record)

[<Fact; Trait("Task", "T004")>]
let ``a grant without plan usage is revoked and stores nothing`` () =
    let rig = Rig(scopes = [ "openid" ])
    let url = rig.Start()
    Assert.Equal(NoPlanAccess, rig.Complete(callback url))
    Assert.Equal(1, rig.Revocations)
    Assert.Equal(None, rig.Record)

[<Fact; Trait("Task", "T004")>]
let ``failed exchange, invalid ID token, missing client id and a failing save store nothing`` () =
    let attempts : (Rig * (Uri -> CallbackQuery)) list =
        [ Rig(exchange = Error ()), callback
          Rig(identity = Error ()), callback
          Rig(), (fun url -> { callback url with ClientId = None })
          Rig(), (fun url -> { callback url with ClientId = Some "dynamic_agent_client" })
          Rig(), (fun url -> { callback url with Code = None })
          Rig(saveFails = true), callback ]
    for rig, query in attempts do
        let url = rig.Start()
        Assert.Equal(Failed, rig.Complete(query url))
        Assert.Equal(None, rig.Record)

[<Fact; Trait("Task", "T004")>]
let ``the vault runs one change at a time`` () = task {
    let store = { Read = (fun () -> None); Save = ignore; Delete = ignore }
    let vault = Vault(store)
    let release = TaskCompletionSource()
    let entered = TaskCompletionSource()
    let mutable second = false
    let first = vault.Locked(fun _ -> task { entered.SetResult(); do! release.Task })
    do! entered.Task
    let other = vault.Locked(fun _ -> task { second <- true })
    // The first holder has not released, so the second cannot have started.
    do! Task.Yield()
    Assert.False(other.IsCompleted)
    Assert.False(second)
    release.SetResult()
    do! first
    do! other
    Assert.True(second) }

[<Fact; Trait("Task", "T004")>]
let ``an error without the current state leaves the pending attempt alone`` () =
    let rig = Rig()
    let url = rig.Start()
    Assert.Equal(Cancelled, rig.Complete { Code = None; State = None; Error = Some "access_denied"; ClientId = None })
    Assert.Equal(Cancelled, rig.Complete { Code = None; State = Some "stale"; Error = Some "access_denied"; ClientId = None })
    Assert.Equal(SignedIn, rig.Complete(callback url))

[<Fact; Trait("Task", "T004")>]
let ``a sign-in that finishes second does not replace the first and is revoked`` () =
    let rig = Rig()
    let url = rig.Start()
    // Another sign-in saved its record while this one was in flight.
    let other : Credentials =
        { ClientId = "client-other"; HostId = "urn:uuid:1"; Subject = "other"; Account = "other@example.test"; AccessToken = "a"; RefreshToken = "r"
          IdToken = "i"; ExpiresAt = now; Scopes = [ ChatGptSignIn.planScope ]; SignedInAt = now; ConfirmedAt = None }
    (rig.Vault.Locked(fun store -> task { store.Save other })).GetAwaiter().GetResult()
    Assert.Equal(Cancelled, rig.Complete(callback url))
    Assert.Equal(1, rig.Revocations)
    Assert.Equal("other", rig.Record.Value.Subject)

[<Fact; Trait("Task", "T004")>]
let ``a callback in flight is superseded by a newer start and is never saved`` () = task {
    let release = TaskCompletionSource()
    let rig = Rig(hold = release.Task)
    let a = rig.Start()
    let inFlight = completeSignIn rig.Vault rig.Session rig.Auth (fun () -> now) (callback a) none
    let b = rig.Start()
    release.SetResult()
    let! outcome = inFlight
    Assert.Equal(Cancelled, outcome)
    Assert.Equal(None, rig.Record)
    Assert.Equal(1, rig.Revocations)
    // the newer attempt still completes
    Assert.Equal(SignedIn, rig.Complete(callback b)) }

[<Fact; Trait("Task", "T004")>]
let ``any error with an unknown, reused or missing state is cancelled`` () =
    let rig = Rig()
    let url = rig.Start()
    for error in [ "access_denied"; "server_error"; "invalid_request" ] do
        Assert.Equal(Cancelled, rig.Complete { Code = None; State = Some "unknown"; Error = Some error; ClientId = None })
        Assert.Equal(Cancelled, rig.Complete { Code = None; State = None; Error = Some error; ClientId = None })
    Assert.Equal(Failed, rig.Complete { callback url with Code = None; Error = Some "server_error" })
    Assert.Equal(Cancelled, rig.Complete { callback url with Code = None; Error = Some "server_error" })

[<Fact; Trait("Task", "T004")>]
let ``a callback that reaches the vault while a start is replacing the attempt is never saved`` () = task {
    let rig = Rig()
    let a = rig.Start()
    let mutable finishing : Task<SignInOutcome> option = None
    // The start's clock runs between its "not signed in" check and the replacement of the pending
    // attempt. Run the older callback right there: if the vault is not held, it finishes and saves
    // inside the window; if it is held, it waits and sees the replacement.
    let clock () =
        if finishing.IsNone then
            finishing <- Some(completeSignIn rig.Vault rig.Session rig.Auth (fun () -> now) (callback a) none)
        now
    let! started = startSignIn rig.Vault rig.Session clock redirect
    Assert.True(started.IsOk)
    let! outcome = finishing.Value
    Assert.Equal(Cancelled, outcome)
    Assert.Equal(None, rig.Record) }


// ---- T005: sign-out ordering and issued tokens ----

let private storedRecord : Credentials =
    { ClientId = "client-issued"; HostId = "urn:uuid:1"; Subject = "user-1"; Account = "researcher@example.test"
      AccessToken = "access"; RefreshToken = "refresh"; IdToken = "id"; ExpiresAt = now.AddHours 1.
      Scopes = [ ChatGptSignIn.planScope ]; SignedInAt = now; ConfirmedAt = None }

let private memoryStore (initial: Credentials option) =
    let mutable record = initial
    let store = { Read = (fun () -> record); Save = (fun c -> record <- Some c); Delete = (fun () -> record <- None) }
    store, (fun () -> record)

let private failingAuth (revoke: Credentials -> CancellationToken -> Task<bool>) : AuthClient =
    { Exchange = fun _ _ _ _ _ -> Task.FromResult(Error())
      VerifyIdToken = fun _ _ _ _ -> Task.FromResult(Error())
      Refresh = fun _ _ -> Task.FromResult(Error Ended)
      Revoke = revoke }

[<Fact; Trait("Task", "T005")>]
let ``sign-out deletes the record before it asks the service to revoke`` () =
    let store, current = memoryStore (Some storedRecord)
    let mutable recordWhenRevoking = Some storedRecord
    let mutable revoked : string option = None
    let auth =
        failingAuth (fun credentials _ ->
            recordWhenRevoking <- current ()
            revoked <- Some credentials.RefreshToken
            Task.FromResult true)
    let result = (signOut (Vault store) (SignInSession()) auth none).GetAwaiter().GetResult()
    Assert.True result
    Assert.Equal(None, recordWhenRevoking)
    Assert.Equal(Some "refresh", revoked)
    Assert.Equal(None, current ())

[<Fact; Trait("Task", "T005")>]
let ``sign-out whose revocation is stopped has already deleted the record`` () = task {
    let store, current = memoryStore (Some storedRecord)
    let reached = TaskCompletionSource()
    let auth = failingAuth (fun _ ct -> reached.SetResult(); Task.Delay(Timeout.Infinite, ct).ContinueWith(fun _ -> true))
    use request = new CancellationTokenSource()
    let pending = signOut (Vault store) (SignInSession()) auth request.Token
    do! reached.Task
    request.Cancel()
    let! _ = pending
    Assert.Equal(None, current ()) }

[<Fact; Trait("Task", "T005")>]
let ``a sign-in cancelled by sign-out while its ID token is checked still revokes the issued tokens`` () = task {
    let store, current = memoryStore None
    let vault = Vault(store)
    let session = SignInSession()
    let reached = TaskCompletionSource()
    let gate = TaskCompletionSource()
    let revocations = System.Collections.Concurrent.ConcurrentQueue<string * bool>()
    let grant =
        { AccessToken = "access"; RefreshToken = "issued-refresh"; IdToken = "id"; ExpiresAt = now.AddHours 1.
          Scopes = [ "openid"; ChatGptSignIn.planScope ] }
    let auth =
        { failingAuth (fun credentials ct -> revocations.Enqueue((credentials.RefreshToken, ct.IsCancellationRequested)); Task.FromResult true) with
            Exchange = fun _ _ _ _ _ -> Task.FromResult(Ok grant)
            VerifyIdToken =
                fun _ _ _ ct ->
                    task {
                        reached.SetResult()
                        do! gate.Task
                        ct.ThrowIfCancellationRequested()
                        return Ok { Subject = "user-1"; Account = "researcher@example.test" }
                    } }
    let url = match (startSignIn vault session (fun () -> now) redirect).GetAwaiter().GetResult() with Ok u -> Uri u | Error () -> failwith "signed in"
    let outcome = completeSignIn vault session auth (fun () -> now) (callback url) none
    do! reached.Task
    let! _ = signOut vault session auth none
    gate.SetResult()
    let! result = outcome
    Assert.Equal(Cancelled, result)
    Assert.Equal(None, current ())
    Assert.Equal<(string * bool) list>([ "issued-refresh", false ], revocations |> Seq.toList) }
