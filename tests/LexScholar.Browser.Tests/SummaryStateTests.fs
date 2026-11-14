module LexScholar.Browser.Tests.SummaryStateTests

open System
open Xunit
open LexScholar.Client.Summary

let private project = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
let private other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
let private document = Guid.Parse("11111111-1111-1111-1111-111111111111")

let private assertState<'a when 'a: equality> (expected: 'a) (actual: 'a) =
    Assert.True((expected = actual), sprintf "Expected: %A\nActual: %A" expected actual)

let private generated =
    { DocumentId = document
      Points = [ { Text = "Main idea"; Pages = [ 1; 2 ] } ]
      PagesCovered = [ 1; 2 ]
      UnreadablePages = []
      OmittedPoints = 0
      GeneratedAt = DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero)
      ServiceHost = "127.0.0.1:9"
      Model = "m" }

let private reading = update (ReaderChanged (Some project, Some document)) initial |> fst

[<Fact; Trait("Task", "T004")>]
let ``requesting a summary starts generating and sends one request`` () =
    let model, effect = update Summarize reading
    assertState (Generating 1) model.State
    assertState (Some (Send (1, project, document, None, None))) effect

[<Fact; Trait("Task", "T004")>]
let ``requesting without an open document does nothing`` () =
    let model, effect = update Summarize initial
    assertState Idle model.State
    assertState None effect

[<Fact; Trait("Task", "T004")>]
let ``a second request while generating is ignored`` () =
    let generating, _ = update Summarize reading
    let model, effect = update Summarize generating
    assertState generating model
    assertState None effect

[<Fact; Trait("Task", "T004")>]
let ``a result for the current request is shown and an older one is dropped`` () =
    let generating, _ = update Summarize reading
    let stale, _ = update (Responded (0, Ok generated)) generating
    assertState generating stale
    let shown, _ = update (Responded (1, Ok generated)) generating
    assertState (Shown generated) shown.State

[<Fact; Trait("Task", "T004")>]
let ``a failure is kept as the state, with not configured distinct`` () =
    let generating, _ = update Summarize reading
    let failed, _ = update (Responded (1, Error NotConfigured)) generating
    assertState (Failed NotConfigured) failed.State
    // After a failure the researcher can ask again.
    let again, effect = update Summarize failed
    assertState (Generating 2) again.State
    assertState (Some (Send (2, project, document, None, None))) effect

[<Fact; Trait("Task", "T004")>]
let ``dismissing returns to idle and drops a late result`` () =
    let shown, _ = update (Responded (1, Ok generated)) (update Summarize reading |> fst)
    let dismissed, _ = update Dismiss shown
    assertState Idle dismissed.State
    let generating, _ = update Summarize dismissed
    let dismissedWhileGenerating, _ = update Dismiss generating
    assertState Idle dismissedWhileGenerating.State
    let late, _ = update (Responded (2, Ok generated)) dismissedWhileGenerating
    assertState Idle late.State

[<Fact; Trait("Task", "T004")>]
let ``a document or project change resets the summary and drops late results`` () =
    let generating, _ = update Summarize reading
    let changed, _ = update (ReaderChanged (Some project, None)) generating
    assertState Idle changed.State
    let late, _ = update (Responded (1, Ok generated)) changed
    assertState Idle late.State
    let shown, _ = update (Responded (1, Ok generated)) generating
    let otherProject, _ = update (ReaderChanged (Some other, Some document)) shown
    assertState Idle otherProject.State
    // The same project and document reported again keeps the summary.
    let same, _ = update (ReaderChanged (Some project, Some document)) shown
    assertState (Shown generated) same.State

[<Fact; Trait("Task", "T006")>]
let ``progress of the current request is kept and cleared when it ends`` () =
    let generating, _ = update Summarize reading
    let counted, _ = update (Progressed (1, PagesProcessed (12, 40))) generating
    assertState (Some (PagesProcessed (12, 40))) counted.Progress
    let combining, _ = update (Progressed (1, Combining)) counted
    assertState (Some Combining) combining.Progress
    assertState (Generating 1) combining.State
    let shown, _ = update (Responded (1, Ok generated)) combining
    assertState None shown.Progress
    let dismissed, _ = update Dismiss combining
    assertState None dismissed.Progress

[<Fact; Trait("Task", "T006")>]
let ``progress of an older or ended request is dropped`` () =
    let generating, _ = update Summarize reading
    let failed, _ = update (Responded (1, Error (ServiceFailed None))) generating
    let late, _ = update (Progressed (1, PagesProcessed (5, 40))) failed
    assertState None late.Progress
    let again, _ = update Summarize failed
    let old, _ = update (Progressed (1, PagesProcessed (5, 40))) again
    assertState None old.Progress

let private confirming =
    let generating, _ = update Summarize reading
    update (Responded (1, Error (ConfirmationRequired (Service ("summary.example.test:8080", "gpt"))))) generating |> fst

[<Fact; Trait("Task", "T007")>]
let ``a confirmation required answer asks the researcher and sends nothing more`` () =
    assertState (Confirming (Service ("summary.example.test:8080", "gpt"))) confirming.State
    let again, effect = update Summarize confirming
    assertState confirming again
    assertState None effect

[<Fact; Trait("Task", "T007")>]
let ``confirming repeats the request with the confirmed host and model under a new request number`` () =
    let model, effect = update ConfirmSend confirming
    assertState (Generating 2) model.State
    assertState (Some (Send (2, project, document, None, Some (ConfirmedService ("summary.example.test:8080", "gpt"))))) effect

[<Fact; Trait("Task", "T007")>]
let ``cancelling returns to idle and emits no effect`` () =
    let model, effect = update CancelSend confirming
    assertState Idle model.State
    assertState None effect
    // A late answer to the first request changes nothing, and asking again starts unconfirmed.
    let late, lateEffect = update (Responded (1, Ok generated)) model
    assertState Idle late.State
    assertState None lateEffect
    let _, again = update Summarize model
    assertState (Some (Send (2, project, document, None, None))) again

[<Fact; Trait("Task", "T007")>]
let ``confirm and cancel do nothing unless a confirmation is open, and a reader change closes it`` () =
    let ignored, effect = update ConfirmSend reading
    assertState reading ignored
    assertState None effect
    let ignoredCancel, cancelEffect = update CancelSend reading
    assertState reading ignoredCancel
    assertState None cancelEffect
    let moved, movedEffect = update (ReaderChanged (Some project, Some (Guid.NewGuid()))) confirming
    assertState Idle moved.State
    assertState None movedEffect

let private generating = update Summarize reading |> fst

[<Fact; Trait("Task", "T008")>]
let ``dismissing a running request cancels it and drops its late events`` () =
    let model, effect = update Dismiss generating
    assertState Idle model.State
    assertState (Some (Cancel 1)) effect
    let late, lateEffect = update (Responded (1, Ok generated)) model
    assertState Idle late.State
    assertState None lateEffect
    let progressed, _ = update (Progressed (1, Combining)) model
    assertState None progressed.Progress

[<Fact; Trait("Task", "T008")>]
let ``changing document or project cancels the running request`` () =
    let otherDocument, effect = update (ReaderChanged (Some project, Some (Guid.NewGuid()))) generating
    assertState Idle otherDocument.State
    assertState (Some (Cancel 1)) effect
    let otherProject, effect2 = update (ReaderChanged (Some (Guid.NewGuid()), None)) generating
    assertState Idle otherProject.State
    assertState (Some (Cancel 1)) effect2
    let late, _ = update (Responded (1, Ok generated)) otherDocument
    assertState Idle late.State

[<Fact; Trait("Task", "T008")>]
let ``a second request for the same document is ignored while one runs, and dismissing outside a request cancels nothing`` () =
    let again, effect = update Summarize generating
    assertState generating again
    assertState None effect
    let _, idleDismiss = update Dismiss reading
    assertState None idleDismiss
    let _, confirmingDismiss = update Dismiss confirming
    assertState None confirmingDismiss

[<Fact; Trait("Task", "T010")>]
let ``a service failure shows the server's explanation and offers another try that asks again`` () =
    let detail = "The service rejected the request. Check Model."
    let failed, _ = update (Responded (1, Error (ServiceFailed (Some detail)))) generating
    assertState (Failed (ServiceFailed (Some detail))) failed.State
    Assert.Contains(detail, failureText (ServiceFailed (Some detail)))
    Assert.Contains("try again", (failureText (ServiceFailed None)).ToLowerInvariant())
    let again, effect = update Summarize failed
    assertState (Generating 2) again.State
    assertState (Some (Send (2, project, document, None, None))) effect
    let late, _ = update (Responded (1, Error (ServiceFailed None))) again
    assertState (Generating 2) late.State

[<Fact; Trait("Task", "T010")>]
let ``abandoning a request is never shown as a failure, even when its fetch ends with an error`` () =
    let dismissed, _ = update Dismiss generating
    let late, _ = update (Responded (1, Error RequestFailed)) dismissed
    assertState Idle late.State
    let moved, _ = update (ReaderChanged (Some project, None)) generating
    let lateMoved, _ = update (Responded (1, Error (ServiceFailed None))) moved
    assertState Idle lateMoved.State

// ---- F005 T010: destinations, plan confirmation and the new failures ----

let private planOnly = { Plan = true; Configured = false }
let private configuredOnly = { Plan = false; Configured = true }
let private both = { Plan = true; Configured = true }
let private withAvailable available = update (DestinationsChanged available) reading |> fst

[<Fact; Trait("Task", "T010")>]
let ``one available destination is used without a choice`` () =
    let _, plan = update Summarize (withAvailable planOnly)
    assertState (Some (Send (1, project, document, Some UsePlan, None))) plan
    let _, configured = update Summarize (withAvailable configuredOnly)
    assertState (Some (Send (1, project, document, Some UseConfigured, None))) configured

[<Fact; Trait("Task", "T010")>]
let ``with two destinations nothing is sent until she chooses, and the choice is sent`` () =
    let model = withAvailable both
    let unchosen, effect = update Summarize model
    assertState Idle unchosen.State
    assertState None effect
    let chosen = update (Choose UseConfigured) model |> fst
    assertState (Some UseConfigured) chosen.Choice
    let _, send = update Summarize chosen
    assertState (Some (Send (1, project, document, Some UseConfigured, None))) send

[<Fact; Trait("Task", "T010")>]
let ``a choice is dropped when a destination stops being available`` () =
    let chosen = update (Choose UsePlan) (withAvailable both) |> fst
    let signedOut = update (DestinationsChanged configuredOnly) chosen |> fst
    assertState None signedOut.Choice
    let _, send = update Summarize signedOut
    assertState (Some (Send (1, project, document, Some UseConfigured, None))) send

[<Fact; Trait("Task", "T010")>]
let ``confirming the plan names it, sends the confirmation and keeps the destination`` () =
    let model = withAvailable planOnly
    let generating, _ = update Summarize model
    let asked = update (Responded (1, Error (ConfirmationRequired (Plan "Ada")))) generating |> fst
    assertState (Confirming (Plan "Ada")) asked.State
    let confirmed, effect = update ConfirmSend asked
    assertState (Generating 2) confirmed.State
    assertState (Some (Send (2, project, document, Some UsePlan, Some ConfirmedPlan))) effect

[<Fact; Trait("Task", "T010")>]
let ``cancelling the plan confirmation sends nothing`` () =
    let generating, _ = update Summarize (withAvailable planOnly)
    let asked = update (Responded (1, Error (ConfirmationRequired (Plan "Ada")))) generating |> fst
    let cancelled, effect = update CancelSend asked
    assertState Idle cancelled.State
    assertState None effect

[<Fact; Trait("Task", "T010")>]
let ``each new failure is kept as the state and the researcher can ask again`` () =
    let generating, _ = update Summarize (withAvailable planOnly)
    for failure in [ UsageLimitReached; SignedOut; NoPlanAccess; DestinationRequired ] do
        let failed, _ = update (Responded (1, Error failure)) generating
        assertState (Failed failure) failed.State

[<Fact; Trait("Task", "T010")>]
let ``failure texts use the wording of the feature`` () =
    Assert.Equal("Usage limit reached. Review your plan or this app's limit in ChatGPT settings.", failureText UsageLimitReached)
    Assert.Equal("You are signed out of ChatGPT. Sign in again to summarize with your plan.", failureText SignedOut)
    Assert.Equal("This ChatGPT account's plan can't be used by apps, so you are signed out.", failureText NoPlanAccess)
    Assert.Contains("settings file", failureText NotConfigured)
    Assert.Contains("LexScholar:Summary:Endpoint", failureText NotConfigured)

[<Fact; Trait("Task", "T010")>]
let ``a signed-out failure reloads the destinations and offers sign-in`` () =
    for failure in [ SignedOut; NoPlanAccess ] do
        Assert.True(reloadsDestinations failure)
        Assert.True(offersSignIn failure)
    Assert.True(offersSignIn NotConfigured)
    Assert.False(reloadsDestinations UsageLimitReached)
    Assert.False(offersSignIn UsageLimitReached)

[<Fact; Trait("Task", "T010")>]
let ``the stream and the problem map the new failures alike`` () =
    let event code = (use json = System.Text.Json.JsonDocument.Parse($"""{{"type":"failed","code":"{code}"}}""") in LexScholar.Client.SummaryStream.outcome json.RootElement)
    assertState (Some (Error SignedOut)) (event "signed_out")
    assertState (Some (Error UsageLimitReached)) (event "usage_limit_reached")
    assertState (Some (Error NoPlanAccess)) (event "no_plan_access")
    let body code = LexScholar.Client.SummaryStream.failureOfBody $"""{{"code":"{code}"}}"""
    assertState SignedOut (body "chatgpt_signed_out")
    assertState DestinationRequired (body "destination_required")
    assertState NotConfigured (body "summary_service_not_configured")
    assertState (ConfirmationRequired (Plan "Ada"))
        (LexScholar.Client.SummaryStream.failureOfBody """{"code":"confirmation_required","destination":"chatgpt","account":"Ada"}""")

[<Fact; Trait("Task", "T010")>]
let ``the request body carries the destination and the confirmation`` () =
    let body destination confirmed =
        use json = System.Text.Json.JsonDocument.Parse(LexScholar.Client.SummaryStream.requestBody destination confirmed)
        json.RootElement.GetRawText()
    assertState """{"destination":"chatgpt","confirmSend":false}""" (body (Some UsePlan) None)
    assertState """{"destination":"configured","confirmSend":false}""" (body (Some UseConfigured) None)
    assertState """{"destination":"chatgpt","confirmSend":true}""" (body (Some UsePlan) (Some ConfirmedPlan))
    assertState """{"confirmSend":false}""" (body None None)
