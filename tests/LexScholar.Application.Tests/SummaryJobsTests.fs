/// The background summary registry (F004, research §3), with in-memory capabilities.
module LexScholar.Application.Tests.SummaryJobsTests

open System
open System.Threading
open System.Threading.Tasks
open Xunit
open LexScholar.Application
open LexScholar.Domain

let private key = { Project = Guid.NewGuid(); Document = Guid.NewGuid() }

let private summary =
    { DocumentId = key.Document
      Points = [ { Text = "A point"; Pages = [ 1 ] } ]
      PagesCovered = [ 1 ]
      UnreadablePages = []
      OmittedPoints = 0
      GeneratedAt = DateTimeOffset.UtcNow
      ServiceHost = "host"
      Model = "m" }

let private registry (associated: bool ref) (stored: StoredSummary option ref) =
    SummaryJobs(
        { Find = fun _ -> stored.Value
          Save = fun _ saved -> stored.Value <- Some saved
          Delete = fun _ -> stored.Value <- None
          AssociationExists = fun _ -> associated.Value })

/// A job body that hands out its handle and then waits for `finish`.
let private held () =
    let handle = TaskCompletionSource<JobHandle>()
    let finish = TaskCompletionSource()
    let run (given: JobHandle) : Task<unit> = task {
        handle.SetResult given
        do! finish.Task }
    run, handle.Task, finish

let private noJob (_: JobHandle) : Task<unit> = Task.FromResult()

[<Fact; Trait("Task", "T002")>]
let ``Start answers NotAssociated when the pair is no longer in the project`` () =
    let jobs = registry (ref false) (ref None)
    Assert.Equal(NotAssociated, jobs.Start(key, CancellationToken.None, noJob))
    Assert.Equal(None, jobs.State key)

[<Fact; Trait("Task", "T002")>]
let ``Start starts one job, a second ask while it runs is AlreadyRunning and runs nothing`` () =
    let jobs = registry (ref true) (ref None)
    let run, handle, finish = held ()
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, run))
    handle.Wait()
    Assert.Equal(Some (JobState.Running None), jobs.State key)
    let mutable second = false
    Assert.Equal(AlreadyRunning, jobs.Start(key, CancellationToken.None, fun _ -> second <- true; Task.FromResult()))
    finish.SetResult()
    jobs.Settled(key).Wait()
    Assert.False second

[<Fact; Trait("Task", "T002")>]
let ``a finished job is AlreadyFinished and a stored summary is AlreadyFinished`` () =
    let stored = ref None
    let jobs = registry (ref true) stored
    let run, handle, finish = held ()
    jobs.Start(key, CancellationToken.None, run) |> ignore
    let given = handle.Result
    Assert.True(jobs.Complete(key, given.Id, summary))
    finish.SetResult()
    jobs.Settled(key).Wait()
    Assert.Equal(Some (JobState.Finished summary), jobs.State key)
    Assert.Equal(AlreadyFinished, jobs.Start(key, CancellationToken.None, noJob))
    // Another pair that only has a stored summary.
    let other = { Project = key.Project; Document = Guid.NewGuid() }
    let fresh = registry (ref true) (ref (Some summary))
    Assert.Equal(AlreadyFinished, fresh.Start(other, CancellationToken.None, noJob))

[<Fact; Trait("Task", "T002")>]
let ``a failed job is replaced by a new one`` () =
    let jobs = registry (ref true) (ref None)
    let run, handle, finish = held ()
    jobs.Start(key, CancellationToken.None, run) |> ignore
    Assert.True(jobs.Fail(key, handle.Result.Id, "service_failed", "detail", Some 2))
    finish.SetResult()
    Assert.Equal(Some (JobState.Failed ("service_failed", "detail", Some 2)), jobs.State key)
    let run2, handle2, finish2 = held ()
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, run2))
    handle2.Wait()
    Assert.Equal(Some (JobState.Running None), jobs.State key)
    finish2.SetResult()

[<Fact; Trait("Task", "T002")>]
let ``a stale job id cannot change state`` () =
    let jobs = registry (ref true) (ref None)
    let run, handle, finish = held ()
    jobs.Start(key, CancellationToken.None, run) |> ignore
    let old = handle.Result
    jobs.Fail(key, old.Id, "service_failed", "d", None) |> ignore
    let run2, handle2, finish2 = held ()
    jobs.Start(key, CancellationToken.None, run2) |> ignore
    handle2.Wait()
    Assert.NotEqual(old.Id, handle2.Result.Id)
    Assert.False(jobs.Complete(key, old.Id, summary))
    Assert.False(jobs.Fail(key, old.Id, "service_failed", "d", None))
    Assert.Equal(Some (JobState.Running None), jobs.State key)
    Assert.True(jobs.Complete(key, handle2.Result.Id, summary))
    finish.SetResult()
    finish2.SetResult()

[<Fact; Trait("Task", "T024")>]
let ``replacement registration rejects a removed association and early plan failure preserves the saved version`` () =
    let associated = ref true
    let stored = ref (Some summary)
    let jobs = registry associated stored
    Assert.Equal(Started, jobs.StartFailed(key, "signed_out", "expired", regenerate = true))
    Assert.Equal(Some summary, stored.Value)
    jobs.Cancel key
    Assert.Equal(Some (JobState.Finished summary), jobs.State key)
    jobs.RemoveAssociation(key, fun () -> associated.Value <- false; stored.Value <- None)
    Assert.Equal(NotAssociated, jobs.Start(key, CancellationToken.None, noJob, regenerate = true))
    Assert.Equal(NotAssociated, jobs.StartFailed(key, "signed_out", "expired", regenerate = true))

[<Fact; Trait("Task", "T002")>]
let ``the job's token is linked to the application stopping, not to a request`` () =
    let jobs = registry (ref true) (ref None)
    use stopping = new CancellationTokenSource()
    let run, handle, finish = held ()
    jobs.Start(key, stopping.Token, run) |> ignore
    let given = handle.Result
    Assert.False given.Token.IsCancellationRequested
    stopping.Cancel()
    Assert.True given.Token.IsCancellationRequested
    finish.SetResult()

[<Fact; Trait("Task", "T003")>]
let ``a plan job's own token is the one handed to its work, and Stop's source reaches it`` () =
    let jobs = registry (ref true) (ref None)
    use source = new CancellationTokenSource()
    use run = new CancellationTokenSource()
    let work, handle, finish = held ()
    Assert.Equal(Started, jobs.StartWith(key, source, run.Token, work))
    let given = handle.Result
    // The work is given the effective token (the plan's run token), not the source's.
    run.Cancel()
    Assert.True given.Token.IsCancellationRequested
    Assert.False source.IsCancellationRequested
    finish.SetResult()

[<Fact; Trait("Task", "T003")>]
let ``Complete with a cancelled effective token ends the job as signed_out and keeps nothing`` () =
    let stored = ref None
    let jobs = registry (ref true) stored
    use source = new CancellationTokenSource()
    use run = new CancellationTokenSource()
    let work, handle, finish = held ()
    jobs.StartWith(key, source, run.Token, work) |> ignore
    let given = handle.Result
    run.Cancel()
    Assert.False(jobs.Complete(key, given.Id, summary))
    match jobs.State key with
    | Some (JobState.Failed ("signed_out", _, None)) -> ()
    | other -> failwith $"expected failed signed_out, got {other}"
    Assert.Equal(None, stored.Value)
    finish.SetResult()

[<Fact; Trait("Task", "T003")>]
let ``Complete with a live effective token finishes the job`` () =
    let jobs = registry (ref true) (ref None)
    use source = new CancellationTokenSource()
    let work, handle, finish = held ()
    jobs.StartWith(key, source, source.Token, work) |> ignore
    Assert.True(jobs.Complete(key, handle.Result.Id, summary))
    Assert.Equal(Some (JobState.Finished summary), jobs.State key)
    finish.SetResult()

[<Fact; Trait("Task", "T003")>]
let ``StartFailed creates a failed job, replaces a failed one, and refuses a running or finished pair`` () =
    let jobs = registry (ref true) (ref None)
    Assert.Equal(Started, jobs.StartFailed(key, "signed_out", "detail"))
    Assert.Equal(Some (JobState.Failed ("signed_out", "detail", None)), jobs.State key)
    Assert.Equal(Started, jobs.StartFailed(key, "service_failed", "again"))
    Assert.Equal(Some (JobState.Failed ("service_failed", "again", None)), jobs.State key)
    let other = { key with Document = Guid.NewGuid() }
    Assert.Equal(NotAssociated, (registry (ref false) (ref None)).StartFailed(other, "signed_out", "d"))
    let run, handle, finish = held ()
    jobs.Start(other, CancellationToken.None, run) |> ignore
    handle.Wait()
    Assert.Equal(AlreadyRunning, jobs.StartFailed(other, "signed_out", "d"))
    finish.SetResult()

// F004 T008: Stop.

[<Fact; Trait("Task", "T008")>]
let ``after Stop the job's token is cancelled so a call made with it never reaches the service`` () =
    let jobs = registry (ref true) (ref None)
    let run, handle, finish = held ()
    jobs.Start(key, CancellationToken.None, run) |> ignore
    let given = handle.Result
    jobs.Stop key
    Assert.True given.Token.IsCancellationRequested
    let mutable reached = false
    let call (token: CancellationToken) = token.ThrowIfCancellationRequested(); reached <- true
    Assert.Throws<OperationCanceledException>(fun () -> call given.Token) |> ignore
    Assert.False reached
    Assert.Equal(None, jobs.State key)
    finish.SetResult()

[<Fact; Trait("Task", "T008")>]
let ``Complete, Fail and Progress after Stop change and save nothing`` () =
    let stored = ref None
    let jobs = registry (ref true) stored
    let run, handle, finish = held ()
    jobs.Start(key, CancellationToken.None, run) |> ignore
    let given = handle.Result
    jobs.Stop key
    Assert.False(jobs.Complete(key, given.Id, summary))
    Assert.False(jobs.Fail(key, given.Id, "service_failed", "detail", None))
    Assert.False(jobs.Progress(key, given.Id, Progress.Combining))
    Assert.Equal(None, stored.Value)
    Assert.Equal(None, jobs.State key)
    finish.SetResult()

[<Fact; Trait("Task", "T008")>]
let ``Stop removes a stored summary and a stopped key can be started again`` () =
    let stored = ref (Some summary)
    let jobs = registry (ref true) stored
    Assert.Equal(Some (JobState.Finished summary), jobs.State key)
    jobs.Stop key
    Assert.Equal(None, stored.Value)
    Assert.Equal(None, jobs.State key)
    let run, handle, finish = held ()
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, run))
    handle.Wait()
    finish.SetResult()

// F004 T009: removal from the project.

[<Fact; Trait("Task", "T009")>]
let ``RemoveAssociation stops a running job and deletes the association, whose cascade takes the stored row`` () =
    let associated = ref true
    let stored = ref None
    let jobs = registry associated stored
    let run, handle, finish = held ()
    jobs.Start(key, CancellationToken.None, run) |> ignore
    let given = handle.Result
    // A row is stored as well (finishing may have won the race): the association delete takes it with it (the cascade).
    stored.Value <- Some summary
    let result = jobs.RemoveAssociation(key, fun () -> associated.Value <- false; stored.Value <- None; "removed")
    Assert.Equal("removed", result)
    Assert.True given.Token.IsCancellationRequested
    Assert.Equal(None, stored.Value)
    Assert.Equal(None, jobs.State key)
    Assert.Equal(NotAssociated, jobs.Start(key, CancellationToken.None, noJob))
    finish.SetResult()

[<Fact; Trait("Task", "T009")>]
let ``a Start launched while the association is being deleted gets NotAssociated`` () =
    let associated = ref true
    let jobs = registry associated (ref None)
    let mutable racing : Task<StartOutcome> = Unchecked.defaultof<_>
    let answered = new ManualResetEventSlim()
    jobs.RemoveAssociation(key, fun () ->
        // Another request's Start begins now; it must wait for the lock, so it sees the association gone.
        racing <- Task.Run(fun () ->
            let outcome = jobs.Start(key, CancellationToken.None, noJob)
            answered.Set()
            outcome)
        // Bounded wait for an early answer: only a Start that did NOT wait for the lock can give one here, so with the lock this
        // times out and the test goes on; without it the early `Started` is seen and the assertion below fails.
        answered.Wait(TimeSpan.FromMilliseconds 200.) |> ignore
        associated.Value <- false)
    Assert.Equal(NotAssociated, racing.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult())
    Assert.Equal(None, jobs.State key)

[<Fact; Trait("Task", "T009")>]
let ``a failed association delete leaves the stored summary alone`` () =
    let stored = ref (Some summary)
    let jobs = registry (ref true) stored
    let failed = jobs.RemoveAssociation(key, fun () -> Error "database error")
    Assert.Equal(Error "database error", failed)
    Assert.Equal(Some summary, stored.Value)

[<Fact; Trait("Task", "T024")>]
let ``replacement failure cancellation stale reports and restart preserve the durable version`` () =
    let stored = ref (Some summary)
    let jobs = registry (ref true) stored
    let run, handle, finish = held ()
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, run, regenerate = true))
    let old = handle.Result
    Assert.Equal(AlreadyRunning, jobs.Start(key, CancellationToken.None, noJob, regenerate = true))
    Assert.Equal(AlreadyRunning, jobs.Start(key, CancellationToken.None, noJob))
    Assert.True(jobs.Fail(key, old.Id, "service_failed", "failed", None))
    Assert.Equal(Some summary, stored.Value)
    let run2, handle2, finish2 = held ()
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, run2, regenerate = true))
    let newer = handle2.Result
    Assert.False(jobs.Complete(key, old.Id, { summary with Model = "stale" }))
    Assert.False(jobs.Fail(key, old.Id, "bad", "bad", None))
    Assert.False(jobs.Progress(key, old.Id, Progress.Combining))
    jobs.Cancel key
    Assert.True newer.Token.IsCancellationRequested
    Assert.False(jobs.Complete(key, newer.Id, { summary with Model = "late" }))
    Assert.Equal(Some summary, stored.Value)
    Assert.Equal(Some (JobState.Finished summary), (registry (ref true) stored).State key)
    finish.SetResult()
    finish2.SetResult()

[<Fact; Trait("Task", "T024")>]
let ``replacement commits atomically and cancel after commit keeps the new version but DELETE removes it`` () =
    let stored = ref (Some summary)
    let jobs = registry (ref true) stored
    let run, handle, finish = held ()
    jobs.Start(key, CancellationToken.None, run, regenerate = true) |> ignore
    let replacement = { summary with Model = "replacement" }
    Assert.Equal(Some summary, stored.Value)
    Assert.True(jobs.Complete(key, handle.Result.Id, replacement))
    jobs.Cancel key
    Assert.Equal(Some replacement, stored.Value)
    jobs.Stop key
    Assert.Equal(None, stored.Value)
    Assert.Equal(NoSavedSummary, jobs.Start(key, CancellationToken.None, noJob, regenerate = true))
    finish.SetResult()

[<Fact; Trait("Task", "T024")>]
let ``save failure or cancelled effective token cannot replace the durable version`` () =
    let stored = ref (Some summary)
    let jobs =
        SummaryJobs { Find = fun _ -> stored.Value
                      Save = fun _ _ -> failwith "storage failure"
                      Delete = fun _ -> stored.Value <- None
                      AssociationExists = fun _ -> true }
    use stopping = new CancellationTokenSource()
    let run, handle, finish = held ()
    jobs.Start(key, stopping.Token, run, regenerate = true) |> ignore
    let given = handle.Result
    Assert.Throws<Exception>(fun () -> jobs.Complete(key, given.Id, { summary with Model = "new" }) |> ignore) |> ignore
    Assert.True(jobs.Fail(key, given.Id, "service_failed", "failed save", None))
    Assert.Equal(Some summary, stored.Value)
    let run2, handle2, finish2 = held ()
    jobs.Start(key, stopping.Token, run2, regenerate = true) |> ignore
    let next = handle2.Result
    stopping.Cancel()
    Assert.False(jobs.Complete(key, next.Id, { summary with Model = "cancelled" }))
    Assert.Equal(Some summary, stored.Value)
    finish.SetResult()
    finish2.SetResult()
