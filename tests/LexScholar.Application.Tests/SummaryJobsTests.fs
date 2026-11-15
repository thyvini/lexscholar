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
