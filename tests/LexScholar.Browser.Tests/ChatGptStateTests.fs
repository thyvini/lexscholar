module LexScholar.Browser.Tests.ChatGptStateTests

open Xunit
open LexScholar.Client.ChatGpt

let private assertState<'a when 'a: equality> (expected: 'a) (actual: 'a) =
    Assert.True((expected = actual), sprintf "Expected: %A\nActual: %A" expected actual)

let private account = { Name = "researcher@example.org"; Confirmed = false }

let private request effect =
    match effect with
    | Some (FetchDestinations request) -> request
    | other -> failwithf "Expected a destinations fetch, got %A." other

let private signedIn welcome =
    let model, effect = update Load initial
    fst (update (Loaded (request effect, Ok { Account = Some account; WelcomePending = welcome })) model)

let private signedOut =
    let model, effect = update Load initial
    fst (update (Loaded (request effect, Ok { Account = None; WelcomePending = false })) model)

[<Fact; Trait("Task", "T009")>]
let ``loading requests the destinations and shows the account and the pending welcome`` () =
    let model, effect = update Load initial
    assertState Unknown model.Status
    let model, effect = update (Loaded (request effect, Ok { Account = Some account; WelcomePending = true })) model
    assertState (SignedIn account) model.Status
    assertState true model.WelcomePending
    assertState None effect

[<Fact; Trait("Task", "T009")>]
let ``loading without a sign-in shows signed out`` () =
    assertState SignedOut signedOut.Status
    assertState false signedOut.WelcomePending

[<Fact; Trait("Task", "T009")>]
let ``a failed load leaves the state unknown`` () =
    let model, effect = update Load initial
    let model, _ = update (Loaded (request effect, Error ())) model
    assertState Unknown model.Status

[<Fact; Trait("Task", "T009")>]
let ``a stale destinations response is dropped`` () =
    let first, firstEffect = update Load initial
    let second, secondEffect = update Load first
    let stale, _ = update (Loaded (request firstEffect, Ok { Account = Some account; WelcomePending = false })) second
    assertState Unknown stale.Status
    let fresh, _ = update (Loaded (request secondEffect, Ok { Account = None; WelcomePending = false })) stale
    assertState SignedOut fresh.Status

[<Fact; Trait("Task", "T009")>]
let ``signing in emits one start and a second sign-in is ignored`` () =
    let model, effect = update SignIn signedOut
    assertState Starting model.Status
    assertState (Some StartSignIn) effect
    let again, effect = update SignIn model
    assertState Starting again.Status
    assertState None effect

[<Fact; Trait("Task", "T009")>]
let ``signing in is ignored before the state is known and while signed in`` () =
    assertState None (snd (update SignIn initial))
    assertState None (snd (update SignIn (signedIn false)))

[<Fact; Trait("Task", "T009")>]
let ``the authorization address navigates the tab`` () =
    let starting, _ = update SignIn signedOut
    let model, effect = update (AuthorizationReady (Ok "https://auth.openai.com/authorize?x=1")) starting
    assertState Starting model.Status
    assertState (Some (Navigate "https://auth.openai.com/authorize?x=1")) effect

[<Fact; Trait("Task", "T009")>]
let ``a failed start shows a message and offers sign-in again`` () =
    let starting, _ = update SignIn signedOut
    let model, effect = update (AuthorizationReady (Error RequestFailed)) starting
    assertState SignedOut model.Status
    assertState (Some StartFailed) model.Notice
    assertState None effect

[<Fact; Trait("Task", "T009")>]
let ``already signed in on the server reloads the state`` () =
    let starting, _ = update SignIn signedOut
    let model, effect = update (AuthorizationReady (Error AlreadySignedIn)) starting
    assertState SignedOut model.Status
    Assert.True((request effect > 0))

[<Fact; Trait("Task", "T009")>]
let ``signing out sends once and clears the account`` () =
    let model, effect = update SignOut (signedIn false)
    assertState (Some SendSignOut) effect
    assertState None (snd (update SignOut model))
    assertState None (snd (update SignIn model))
    let model, effect = update (SignedOutResult (Ok true)) model
    assertState SignedOut model.Status
    assertState false model.WelcomePending
    assertState None model.Notice
    assertState None effect

[<Fact; Trait("Task", "T009")>]
let ``an unconfirmed revocation tells her she can disconnect in ChatGPT settings`` () =
    let model, _ = update SignOut (signedIn false)
    let model, _ = update (SignedOutResult (Ok false)) model
    assertState SignedOut model.Status
    assertState (Some NotRevoked) model.Notice

[<Fact; Trait("Task", "T009")>]
let ``a failed sign-out keeps the account and says so`` () =
    let model, _ = update SignOut (signedIn false)
    let model, _ = update (SignedOutResult (Error ())) model
    assertState (SignedIn account) model.Status
    assertState (Some SignOutFailed) model.Notice
    assertState (Some SendSignOut) (snd (update SignOut model))

[<Fact; Trait("Task", "T009")>]
let ``signing out is ignored when not signed in`` () =
    assertState None (snd (update SignOut signedOut))

[<Fact; Trait("Task", "T009")>]
let ``dismissing the welcome hides it and records it once`` () =
    let model, effect = update DismissWelcome (signedIn true)
    assertState false model.WelcomePending
    assertState (Some SendDismissWelcome) effect
    assertState None (snd (update DismissWelcome model))

[<Fact; Trait("Task", "T009")>]
let ``each return message is shown once`` () =
    for value, expected in [ "cancelled", Cancelled; "no_plan_access", NoPlanAccess; "failed", Failed ] do
        let model, effect = update (Returned (Some value)) initial
        assertState (Some expected) model.Notice
        assertState None effect
        let dismissed, _ = update DismissNotice model
        assertState None dismissed.Notice
        // Reloading the state does not bring the message back.
        let reloaded, fetch = update Load dismissed
        let reloaded, _ = update (Loaded (request fetch, Ok { Account = None; WelcomePending = false })) reloaded
        assertState None reloaded.Notice

[<Fact; Trait("Task", "T009")>]
let ``a successful return and unknown values show nothing`` () =
    assertState None (fst (update (Returned (Some "signed_in")) initial)).Notice
    assertState None (fst (update (Returned (Some "other")) initial)).Notice
    assertState None (fst (update (Returned None) initial)).Notice
