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
