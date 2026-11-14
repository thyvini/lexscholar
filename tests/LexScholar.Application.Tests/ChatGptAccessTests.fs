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

type private Rig(?scopes: string list, ?exchange: Result<TokenGrant, unit>, ?identity: Result<Identity, unit>, ?saveFails: bool) =
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
        { Exchange = fun _ _ _ _ _ -> exchanges <- exchanges + 1; Task.FromResult(defaultArg exchange (Ok grant))
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
    { Code = Some "code-1"; State = Some (parameters url)["state"]; Error = None; ClientId = Some "client-issued" }

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
    Assert.NotEqual(first["state"], second["state"])
    Assert.NotEqual(first["nonce"], second["nonce"])
    Assert.NotEqual(first["code_challenge"], second["code_challenge"])
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
