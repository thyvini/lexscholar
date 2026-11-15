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
    let dismissing, _ = update Dismiss shown
    // The server's answer to the delete (F004 T006) ends the dismissal.
    let dismissed, _ = update (Dismissed (project, document, true)) dismissing
    assertState Idle dismissed.State
    let generating, _ = update Summarize dismissed
    let dismissedWhileGenerating, _ = update Dismiss generating
    assertState (Dismissing (Generating 2)) dismissedWhileGenerating.State
    let late, _ = update (Responded (2, Ok generated)) dismissedWhileGenerating
    assertState (Dismissing (Generating 2)) late.State

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
    assertState (Some (Send (2, project, document, Some UseConfigured, Some (ConfirmedService ("summary.example.test:8080", "gpt"))))) effect

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
let ``changing document or project aborts nothing and leaves the request to the server`` () =
    let otherDocument, effect = update (ReaderChanged (Some project, Some (Guid.NewGuid()))) generating
    assertState Idle otherDocument.State
    assertState None effect
    let otherProject, effect2 = update (ReaderChanged (Some (Guid.NewGuid()), None)) generating
    assertState Idle otherProject.State
    assertState None effect2
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
    assertState (Dismissing (Generating 1)) late.State
    let settled, _ = update (Dismissed (project, document, true)) late
    let settled, _ = update (Dismissed (project, document, true)) settled
    assertState Idle settled.State
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
    // With a stale signed-in label the server answers not configured: reload so the sign-in offer becomes usable.
    Assert.True(reloadsDestinations NotConfigured)
    Assert.False(reloadsDestinations UsageLimitReached)
    Assert.False(offersSignIn UsageLimitReached)

[<Fact; Trait("Task", "T010")>]
let ``a job state and a problem map the new failures alike`` () =
    let state code = LexScholar.Client.SummaryStatus.parseRemote $"""{{"status":"failed","code":"{code}"}}"""
    assertState (Some (RemoteFailed SignedOut)) (state "signed_out")
    assertState (Some (RemoteFailed UsageLimitReached)) (state "usage_limit_reached")
    assertState (Some (RemoteFailed NoPlanAccess)) (state "no_plan_access")
    let body code = LexScholar.Client.SummaryStatus.failureOfBody $"""{{"code":"{code}"}}"""
    assertState SignedOut (body "chatgpt_signed_out")
    assertState DestinationRequired (body "destination_required")
    assertState NotConfigured (body "summary_service_not_configured")
    assertState (ConfirmationRequired (Plan "Ada"))
        (LexScholar.Client.SummaryStatus.failureOfBody """{"code":"confirmation_required","destination":"chatgpt","account":"Ada"}""")

[<Fact; Trait("Task", "T010")>]
let ``the request body carries the destination and the confirmation`` () =
    let body destination confirmed =
        use json = System.Text.Json.JsonDocument.Parse(LexScholar.Client.SummaryStatus.requestBody destination confirmed)
        json.RootElement.GetRawText()
    assertState """{"destination":"chatgpt","confirmSend":false}""" (body (Some UsePlan) None)
    assertState """{"destination":"configured","confirmSend":false}""" (body (Some UseConfigured) None)
    assertState """{"destination":"chatgpt","confirmSend":true}""" (body (Some UsePlan) (Some ConfirmedPlan))
    assertState """{"confirmSend":false}""" (body None None)

[<Fact; Trait("Task", "T010")>]
let ``confirming sends to the destination the dialog named, even if the destinations changed`` () =
    let generating, _ = update Summarize (withAvailable both |> update (Choose UsePlan) |> fst)
    let asked = update (Responded (1, Error (ConfirmationRequired (Plan "Ada")))) generating |> fst
    // She signs out while the dialog is open: only the configured service remains.
    let changed = update (DestinationsChanged configuredOnly) asked |> fst
    let _, effect = update ConfirmSend changed
    assertState (Some (Send (2, project, document, Some UsePlan, Some ConfirmedPlan))) effect

[<Fact; Trait("Task", "T010")>]
let ``choosing another destination clears a failure so she can ask again`` () =
    let generating, _ = update Summarize (withAvailable both |> update (Choose UsePlan) |> fst)
    let failed, _ = update (Responded (1, Error UsageLimitReached)) generating
    let chosen, _ = update (Choose UseConfigured) failed
    assertState Idle chosen.State
    let _, effect = update Summarize chosen
    assertState (Some (Send (2, project, document, Some UseConfigured, None))) effect

// F004 T002: the server owns the summary job; the client follows it by polling.

let private asked = update Summarize reading |> fst

[<Fact; Trait("Task", "T002")>]
let ``a 202 with a running job keeps waiting and polls after the interval`` () =
    let model, effect = update (Accepted (1, project, document, (RemoteRunning None))) asked
    assertState Running model.State
    match effect with
    | Some (Fetch (watch, p, d, true)) ->
        assertState project p
        assertState document d
        let again, next = update (Polled (watch, Some (RemoteRunning None))) model
        assertState Running again.State
        assertState (Some (Fetch (watch, project, document, true))) next
    | other -> failwithf "Expected a delayed Fetch, got %A" other

[<Fact; Trait("Task", "T002")>]
let ``a job answered while the POST was open is shown, or its failure`` () =
    let shown, effect = update (Accepted (1, project, document, RemoteFinished generated)) asked
    assertState (Shown generated) shown.State
    assertState None effect
    let failed, _ = update (Accepted (1, project, document, RemoteFailed UsageLimitReached)) asked
    assertState (Failed UsageLimitReached) failed.State
    let stale, _ = update (Accepted (9, project, document, RemoteFinished generated)) asked
    assertState asked.State stale.State

[<Fact; Trait("Task", "T002")>]
let ``leaving a document with a running job cancels nothing and returning asks the server`` () =
    let running, _ = update (Accepted (1, project, document, (RemoteRunning None))) asked
    let away, cancel = update (ReaderChanged (Some project, Some (Guid.NewGuid()))) running
    assertState Idle away.State
    assertState None cancel
    let back, _ = update (ReaderChanged (Some project, Some document)) away
    let refreshed, effect = update Refresh back
    match effect with
    | Some (Fetch (watch, p, d, false)) ->
        assertState (project, document) (p, d)
        let shown, _ = update (Polled (watch, Some (RemoteFinished generated))) refreshed
        assertState (Shown generated) shown.State
        let none, noEffect = update (Polled (watch, None)) refreshed
        assertState Idle none.State
        assertState None noEffect
    | other -> failwithf "Expected an immediate Fetch, got %A" other

[<Fact; Trait("Task", "T002")>]
let ``a fetched state for a document she has left is dropped`` () =
    let refreshed, effect = update Refresh reading
    let watch = match effect with Some (Fetch (w, _, _, _)) -> w | other -> failwithf "Expected Fetch, got %A" other
    let moved, _ = update (ReaderChanged (Some project, Some (Guid.NewGuid()))) refreshed
    let late, lateEffect = update (Polled (watch, Some (RemoteFinished generated))) moved
    assertState Idle late.State
    assertState None lateEffect
    let dismissed, _ = update (Accepted (1, project, document, (RemoteRunning None))) asked
    let hidden, _ = update Dismiss dismissed
    let stale, staleEffect = update (Polled (dismissed.Watch, Some (RemoteRunning None))) hidden
    assertState Idle stale.State
    assertState None staleEffect

[<Fact; Trait("Task", "T002")>]
let ``leaving while the POST of a configured-service summary is in flight does not abort it`` () =
    let configured, _ = update (DestinationsChanged { Plan = false; Configured = true }) reading
    let asking, sent = update Summarize configured
    assertState (Some (Send (1, project, document, Some UseConfigured, None))) sent
    let away, cancel = update (ReaderChanged (Some project, Some (Guid.NewGuid()))) asking
    assertState Idle away.State
    assertState None cancel
    // Dismissing it deletes the job on the server (T013), still without aborting the POST.
    let dismissed, dismissEffect = update Dismiss asking
    assertState (Dismissing (Generating 1)) dismissed.State
    assertState (Some (DeleteSummary (project, document))) dismissEffect

[<Fact; Trait("Task", "T002")>]
let ``a POST answered after she left and came back still shows the job`` () =
    // Asked, left (the fetch is not aborted), came back and asked the server: nothing there yet (404).
    let away, _ = update (ReaderChanged (Some project, Some (Guid.NewGuid()))) asked
    let back, _ = update (ReaderChanged (Some project, Some document)) away
    let refreshed, effect = update Refresh back
    let watch = match effect with Some (Fetch (w, _, _, _)) -> w | other -> failwithf "Expected Fetch, got %A" other
    let none, _ = update (Polled (watch, None)) refreshed
    assertState Idle none.State
    let running, next = update (Accepted (1, project, document, (RemoteRunning None))) none
    assertState Running running.State
    assertState true (match next with Some (Fetch (_, _, _, true)) -> true | _ -> false)
    // The answer for another document is not shown here.
    let other, _ = update (Accepted (1, project, Guid.NewGuid(), RemoteFinished generated)) none
    assertState Idle other.State

[<Fact; Trait("Task", "T002")>]
let ``a POST answered after she dismissed is not shown`` () =
    let dismissed, _ = update Dismiss asked
    let late, effect = update (Accepted (1, project, document, RemoteFinished generated)) dismissed
    assertState (Dismissing (Generating 1)) late.State
    // Not shown; the job it found is deleted again (T013).
    assertState (Some (DeleteSummary (project, document))) effect
    let settled, _ = update (Dismissed (project, document, true)) late
    let settled, _ = update (Dismissed (project, document, true)) settled
    assertState Idle settled.State
    // A later request is not claimed by an older answer.
    let again, _ = update Summarize settled
    let old, _ = update (Accepted (1, project, document, RemoteFinished generated)) again
    assertState again.State old.State

// F004 T004: progress comes from the job's state; polling stops when it ends.

[<Fact; Trait("Task", "T004")>]
let ``a running job's progress is shown, updates on each poll and polling continues`` () =
    let running, effect = update (Accepted (1, project, document, RemoteRunning (Some (PagesProcessed (5, 40))))) asked
    assertState (Some (PagesProcessed (5, 40))) running.Progress
    let watch = match effect with Some (Fetch (w, _, _, true)) -> w | other -> failwithf "Expected a delayed Fetch, got %A" other
    let later, next = update (Polled (watch, Some (RemoteRunning (Some Combining)))) running
    assertState (Some Combining) later.Progress
    assertState (Some (Fetch (watch, project, document, true))) next

[<Fact; Trait("Task", "T004")>]
let ``returning to a running document shows its progress and keeps polling until it finishes`` () =
    let away, _ = update (ReaderChanged (Some project, Some (Guid.NewGuid()))) asked
    let back, _ = update (ReaderChanged (Some project, Some document)) away
    let refreshed, effect = update Refresh back
    let watch = match effect with Some (Fetch (w, _, _, false)) -> w | other -> failwithf "Expected an immediate Fetch, got %A" other
    let running, next = update (Polled (watch, Some (RemoteRunning (Some (PagesProcessed (12, 40)))))) refreshed
    assertState Running running.State
    assertState (Some (PagesProcessed (12, 40))) running.Progress
    assertState (Some (Fetch (watch, project, document, true))) next
    let finished, stop = update (Polled (watch, Some (RemoteFinished generated))) running
    assertState (Shown generated) finished.State
    assertState None finished.Progress
    assertState None stop

[<Fact; Trait("Task", "T004")>]
let ``a failed job clears progress and stops polling, and a response for another document is dropped`` () =
    let running, _ = update (Accepted (1, project, document, RemoteRunning (Some (PagesProcessed (5, 40))))) asked
    let failed, stop = update (Polled (running.Watch, Some (RemoteFailed UsageLimitReached))) running
    assertState (Failed UsageLimitReached) failed.State
    assertState None failed.Progress
    assertState None stop
    let moved, _ = update (ReaderChanged (Some project, Some (Guid.NewGuid()))) running
    assertState None moved.Progress
    let late, lateEffect = update (Polled (running.Watch, Some (RemoteRunning (Some (PagesProcessed (30, 40)))))) moved
    assertState Idle late.State
    assertState None late.Progress
    assertState None lateEffect

[<Fact; Trait("Task", "T004")>]
let ``a running state is read with its pages processed or combining`` () =
    let read body = LexScholar.Client.SummaryStatus.parseRemote body
    assertState (Some (RemoteRunning None)) (read """{"status":"running"}""")
    assertState (Some (RemoteRunning (Some (PagesProcessed (5, 40))))) (read """{"status":"running","pagesProcessed":5,"totalPages":40}""")
    assertState (Some (RemoteRunning (Some Combining))) (read """{"status":"running","combining":true}""")

// F004 T005: every failure a GET reports renders as the one a waited-for POST gave.

[<Fact; Trait("Task", "T005")>]
let ``each failed state read from a GET is the failure F003 and F005 showed`` () =
    let read code = LexScholar.Client.SummaryStatus.parseRemote $"""{{"status":"failed","code":"{code}"}}"""
    for code, failure in
        [ "usage_limit_reached", UsageLimitReached
          "signed_out", SignedOut
          "no_plan_access", NoPlanAccess
          "not_enough_text", NotEnoughText
          "no_text", NoText
          "too_many_pages", TooManyPages ] do
        assertState (Some (RemoteFailed failure)) (read code)
    assertState
        (Some (RemoteFailed (ServiceFailed (Some "The service failed."))))
        (LexScholar.Client.SummaryStatus.parseRemote """{"status":"failed","code":"service_failed","detail":"The service failed."}""")

[<Fact; Trait("Task", "T005")>]
let ``a failure found on return is shown with its actions, and Summarize asks again`` () =
    let away, _ = update (ReaderChanged (Some project, Some (Guid.NewGuid()))) reading
    let back, _ = update (ReaderChanged (Some project, Some document)) away
    let refreshed, effect = update Refresh back
    let watch = match effect with Some (Fetch (w, _, _, false)) -> w | other -> failwithf "Expected an immediate Fetch, got %A" other
    let failed, stop = update (Polled (watch, Some (RemoteFailed UsageLimitReached))) refreshed
    assertState (Failed UsageLimitReached) failed.State
    assertState None stop
    assertState (failureText UsageLimitReached) "Usage limit reached. Review your plan or this app's limit in ChatGPT settings."
    let signedOut, _ = update (Polled (watch, Some (RemoteFailed SignedOut))) refreshed
    assertState true (offersSignIn SignedOut && reloadsDestinations SignedOut)
    assertState (Failed SignedOut) signedOut.State
    let again, send = update Summarize failed
    assertState true (match again.State, send with Generating _, Some (Send _) -> true | _ -> false)

// F004 T006: dismissing a finished or failed summary deletes it on the server.

[<Fact; Trait("Task", "T006")>]
let ``dismissing a shown or failed summary sends the delete, anything else sends nothing`` () =
    let asked, _ = update Summarize reading
    let shown, _ = update (Responded (1, Ok generated)) asked
    let dismissedShown, shownEffect = update Dismiss shown
    assertState (Dismissing (Shown generated)) dismissedShown.State
    assertState (Some (DeleteSummary (project, document))) shownEffect
    let failed, _ = update (Responded (1, Error (ServiceFailed None))) asked
    assertState (Some (DeleteSummary (project, document))) (snd (update Dismiss failed))
    // Nothing asked, finished or failed: nothing to delete.
    assertState None (snd (update Dismiss reading))
    // An ask in flight is deleted too (T013).
    assertState (Some (DeleteSummary (project, document))) (snd (update Dismiss asked))
    let confirming, _ = update (Responded (1, Error (ConfirmationRequired (Plan "me")))) asked
    assertState None (snd (update Dismiss confirming))

[<Fact; Trait("Task", "T006")>]
let ``while the delete is in flight nothing can be asked; its answer ends or restores the summary`` () =
    let asked, _ = update Summarize reading
    let shown, _ = update (Responded (1, Ok generated)) asked
    let dismissing, _ = update Dismiss shown
    // Asking again, or dismissing again, does nothing until the server answers.
    assertState (dismissing, None) (update Summarize dismissing)
    assertState (dismissing, None) (update Dismiss dismissing)
    let done', effect = update (Dismissed (project, document, true)) dismissing
    assertState Idle done'.State
    assertState false done'.DismissFailed
    assertState None effect
    // A refused delete brings the summary back and says so; the next dismissal clears the message.
    let refused, _ = update (Dismissed (project, document, false)) dismissing
    assertState (Shown generated) refused.State
    assertState true refused.DismissFailed
    assertState false (fst (update Dismiss refused)).DismissFailed
    // An answer for another document, or when not dismissing, is ignored.
    assertState (dismissing, None) (update (Dismissed (project, Guid.NewGuid(), true)) dismissing)
    assertState (shown, None) (update (Dismissed (project, document, true)) shown)

[<Fact; Trait("Task", "T006")>]
let ``coming back while the delete is in flight waits for its answer`` () =
    let asked, _ = update Summarize reading
    let shown, _ = update (Responded (1, Ok generated)) asked
    let dismissing, _ = update Dismiss shown
    let away, _ = update (ReaderChanged (Some project, Some other)) dismissing
    let returned, _ = update (ReaderChanged (Some project, Some document)) away
    let back, fetch = update Refresh returned
    // No fetch of the summary being deleted.
    assertState (Dismissing Idle) back.State
    assertState None fetch
    let ended, endEffect = update (Dismissed (project, document, true)) back
    assertState Idle ended.State
    assertState None endEffect
    // A refused delete after coming back fetches the summary that is still stored.
    let refused, refusedEffect = update (Dismissed (project, document, false)) back
    assertState true refused.DismissFailed
    assertState Idle refused.State
    assertState true (match refusedEffect with Some (Fetch (_, p, d, false)) -> (p, d) = (project, document) | _ -> false)

// F004 T008: Stop.

[<Fact; Trait("Task", "T008")>]
let ``stopping a running summary sends the delete and waits for it, then she can ask again`` () =
    let asked, _ = update Summarize reading
    let running, _ = update (Accepted (1, project, document, RemoteRunning None)) asked
    assertState Running running.State
    let stopping, effect = update Stop running
    assertState (Dismissing Running) stopping.State
    assertState (Some (DeleteSummary (project, document))) effect
    // Nothing can be asked, or stopped again, until the server answers.
    assertState (stopping, None) (update Summarize stopping)
    assertState (stopping, None) (update Stop stopping)
    let stopped, stoppedEffect = update (Dismissed (project, document, true)) stopping
    assertState Idle stopped.State
    assertState None stoppedEffect
    // A poll answered after the stop is dropped.
    assertState Idle (fst (update (Polled (running.Watch, Some (RemoteRunning None))) stopped)).State
    let again, send = update Summarize stopped
    assertState true (match again.State, send with Generating _, Some (Send _) -> true | _ -> false)

[<Fact; Trait("Task", "T008")>]
let ``a failed stop asks the server what is there so polling resumes, and Stop does nothing unless running`` () =
    let asked, _ = update Summarize reading
    let running, _ = update (Accepted (1, project, document, RemoteRunning None)) asked
    let stopping, _ = update Stop running
    let refused, effect = update (Dismissed (project, document, false)) stopping
    assertState true refused.DismissFailed
    assertState Idle refused.State
    assertState true (match effect with Some (Fetch (_, p, d, false)) -> (p, d) = (project, document) | _ -> false)
    assertState None (snd (update Stop reading))
    assertState None (snd (update Stop asked))
    let shown, _ = update (Responded (1, Ok generated)) asked
    assertState None (snd (update Stop shown))

// F004 T013: dismissing while the POST is in flight stops the server job (FR-012).

let private dismissedAsking = update Dismiss generating

[<Fact; Trait("Task", "T013")>]
let ``dismissing while the ask is in flight sends the delete and waits`` () =
    let model, effect = dismissedAsking
    assertState (Dismissing (Generating 1)) model.State
    assertState (Some (DeleteSummary (project, document))) effect
    assertState (model, None) (update Summarize model)
    assertState (model, None) (update Dismiss model)

[<Fact; Trait("Task", "T013")>]
let ``the answer of a dismissed ask deletes the job again and shows nothing`` () =
    let waiting, _ = dismissedAsking
    // The delete answered first: still waiting for the POST.
    let deleted, deletedEffect = update (Dismissed (project, document, true)) waiting
    assertState (Dismissing (Generating 1)) deleted.State
    assertState None deletedEffect
    for remote in [ RemoteRunning None; RemoteFinished generated; RemoteFailed NoText ] do
        let answered, effect = update (Accepted (1, project, document, remote)) deleted
        assertState (Some (DeleteSummary (project, document))) effect
        assertState (Dismissing (Generating 1)) answered.State
        let done', doneEffect = update (Dismissed (project, document, true)) answered
        assertState Idle done'.State
        assertState None doneEffect
        // No poll is started and a poll of the earlier watch is dropped.
        assertState Idle (fst (update (Polled (generating.Watch, Some (RemoteRunning None))) done')).State

[<Fact; Trait("Task", "T013")>]
let ``the POST answering before the delete still deletes again and waits for both`` () =
    let waiting, _ = dismissedAsking
    let answered, effect = update (Accepted (1, project, document, RemoteRunning None)) waiting
    assertState (Some (DeleteSummary (project, document))) effect
    let first, _ = update (Dismissed (project, document, true)) answered
    assertState (Dismissing (Generating 1)) first.State
    let second, _ = update (Dismissed (project, document, true)) first
    assertState Idle second.State

[<Fact; Trait("Task", "T013")>]
let ``a failed POST of a dismissed ask deletes again, since the job may exist, and ends when both deletes answered`` () =
    let waiting, _ = dismissedAsking
    let failed, effect = update (Responded (1, Error RequestFailed)) waiting
    assertState (Some (DeleteSummary (project, document))) effect
    assertState (Dismissing (Generating 1)) failed.State
    let first, _ = update (Dismissed (project, document, true)) failed
    assertState (Dismissing (Generating 1)) first.State
    let second, _ = update (Dismissed (project, document, true)) first
    assertState Idle second.State

[<Fact; Trait("Task", "T013")>]
let ``a failed delete is not forgotten when the other answer arrives last`` () =
    let waiting, _ = dismissedAsking
    let answered, _ = update (Accepted (1, project, document, RemoteRunning None)) waiting
    // The second delete fails first, then the first delete succeeds: the job may still run.
    let secondFailed, _ = update (Dismissed (project, document, false)) answered
    assertState (Dismissing (Generating 1)) secondFailed.State
    let ended, effect = update (Dismissed (project, document, true)) secondFailed
    assertState true ended.DismissFailed
    assertState Idle ended.State
    assertState true (match effect with Some (Fetch (_, p, d, false)) -> (p, d) = (project, document) | _ -> false)

[<Fact; Trait("Task", "T013")>]
let ``leaving after dismissing an ask still deletes the job when the POST answers, and coming back waits`` () =
    let waiting, _ = dismissedAsking
    let away, _ = update (ReaderChanged (Some project, None)) waiting
    let answered, effect = update (Accepted (1, project, document, RemoteRunning None)) away
    assertState (Some (DeleteSummary (project, document))) effect
    let back, backEffect = update (ReaderChanged (Some project, Some document)) answered
    let back, _ = update Refresh back
    assertState (Dismissing Idle) back.State
    ignore backEffect

[<Fact; Trait("Task", "T013")>]
let ``a delete that fails while she is away is reported when she is back`` () =
    let waiting, _ = dismissedAsking
    let answered, _ = update (Accepted (1, project, document, RemoteRunning None)) waiting
    let away, _ = update (ReaderChanged (Some project, None)) answered
    let first, _ = update (Dismissed (project, document, true)) away
    let second, _ = update (Dismissed (project, document, false)) first
    let back, _ = update (ReaderChanged (Some project, Some document)) second
    let refreshed, effect = update Refresh back
    assertState true refreshed.DismissFailed
    assertState true (match effect with Some (Fetch (_, p, d, false)) -> (p, d) = (project, document) | _ -> false)
    // Failure first, success last, while away: the same.
    let away2, _ = update (ReaderChanged (Some project, None)) answered
    let failedFirst, _ = update (Dismissed (project, document, false)) away2
    let last, _ = update (Dismissed (project, document, true)) failedFirst
    let back2, _ = update (ReaderChanged (Some project, Some document)) last
    assertState true (fst (update Refresh back2)).DismissFailed
