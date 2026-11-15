/// Pure state of the list markers (F004 T010): when the status list is fetched, when polling runs and stops, the roll-up.
module LexScholar.Browser.Tests.SummaryStatusStateTests

open System
open Xunit
open LexScholar.Domain
open LexScholar.Client.SummaryStatus

let private project = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
let private other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
let private first = Guid.Parse("11111111-1111-1111-1111-111111111111")
let private second = Guid.Parse("22222222-2222-2222-2222-222222222222")

let private assertState<'a when 'a: equality> (expected: 'a) (actual: 'a) =
    Assert.True((expected = actual), sprintf "Expected: %A\nActual: %A" expected actual)

let private entry projectId documentId status = { ProjectId = projectId; DocumentId = documentId; Status = status }

[<Fact; Trait("Task", "T010")>]
let ``the status list is read from the endpoint's JSON`` () =
    let body = $"""[{{"projectId":"{project}","documentId":"{first}","status":"running"}},{{"projectId":"{project}","documentId":"{second}","status":"failed"}},{{"projectId":"{other}","documentId":"{first}","status":"finished"}}]"""
    assertState
        (Some [ entry project first SummaryStatus.Running; entry project second SummaryStatus.Failed; entry other first SummaryStatus.Finished ])
        (parseStatuses body)
    assertState None (parseStatuses "not json")

[<Fact; Trait("Task", "T010")>]
let ``a refresh fetches the statuses at once`` () =
    let model, effect = updateStatuses Refresh initialStatuses
    assertState (Some (FetchStatuses (model.Watch, false))) effect

[<Fact; Trait("Task", "T010")>]
let ``a document or project view change fetches the statuses, an unchanged view does not`` () =
    let before = (Some project, Some first)
    let _, same = followView before before initialStatuses
    assertState None same
    let _, otherDocument = followView before (Some project, Some second) initialStatuses
    Assert.True(otherDocument.IsSome)
    let _, otherProject = followView before (Some other, None) initialStatuses
    Assert.True(otherProject.IsSome)

[<Fact; Trait("Task", "T010")>]
let ``polling continues every tick while one summary runs and stops when none does`` () =
    let fetching, effect = updateStatuses Refresh initialStatuses
    assertState (Some (FetchStatuses (fetching.Watch, false))) effect
    let running, effect = updateStatuses (StatusesLoaded (fetching.Watch, Ok [ entry project first SummaryStatus.Running ])) fetching
    assertState (Some (FetchStatuses (fetching.Watch, true))) effect
    let finished, effect = updateStatuses (StatusesLoaded (fetching.Watch, Ok [ entry project first SummaryStatus.Finished ])) running
    assertState None effect
    assertState [ entry project first SummaryStatus.Finished ] finished.Entries

[<Fact; Trait("Task", "T010")>]
let ``a failed fetch keeps the markers and keeps polling while one was running`` () =
    let fetching, _ = updateStatuses Refresh initialStatuses
    let running, _ = updateStatuses (StatusesLoaded (fetching.Watch, Ok [ entry project first SummaryStatus.Running ])) fetching
    // However many times it fails: a running summary is waited for.
    let mutable model = running
    for _ in 1 .. 5 do
        let next, effect = updateStatuses (StatusesLoaded (fetching.Watch, Error ())) model
        assertState running.Entries next.Entries
        assertState (Some (FetchStatuses (fetching.Watch, true))) effect
        model <- next

[<Fact; Trait("Task", "T010")>]
let ``a fetch that fails right after a refresh is tried again a few times, then given up`` () =
    let fetching, _ = updateStatuses Refresh initialStatuses
    let mutable model = fetching
    for _ in 1 .. 3 do
        let next, effect = updateStatuses (StatusesLoaded (fetching.Watch, Error ())) model
        assertState (Some (FetchStatuses (fetching.Watch, true))) effect
        model <- next
    let _, effect = updateStatuses (StatusesLoaded (fetching.Watch, Error ())) model
    assertState None effect
    // The retry's answer is read like any other.
    let recovered, effect = updateStatuses (StatusesLoaded (fetching.Watch, Ok [ entry project first SummaryStatus.Running ])) model
    assertState (Some (FetchStatuses (fetching.Watch, true))) effect
    assertState 1 recovered.Entries.Length

[<Fact; Trait("Task", "T010")>]
let ``a newer refresh supersedes the older polling loop`` () =
    let older, _ = updateStatuses Refresh initialStatuses
    let newer, _ = updateStatuses Refresh older
    let after, effect = updateStatuses (StatusesLoaded (older.Watch, Ok [ entry project first SummaryStatus.Running ])) newer
    assertState newer after
    assertState None effect

[<Fact; Trait("Task", "T010")>]
let ``a document's marker and a project's roll-up come from the list`` () =
    let entries =
        [ entry project first SummaryStatus.Finished
          entry project second SummaryStatus.Failed
          entry other first SummaryStatus.Finished ]
    assertState (Some SummaryStatus.Finished) (documentStatus entries project first)
    assertState None (documentStatus entries other second)
    assertState (Some SummaryStatus.Failed) (projectStatus entries project)
    assertState (Some SummaryStatus.Finished) (projectStatus entries other)
    assertState None (projectStatus entries (Guid.NewGuid()))
    let running = entry project first SummaryStatus.Running :: entries
    assertState (Some SummaryStatus.Running) (projectStatus running project)
