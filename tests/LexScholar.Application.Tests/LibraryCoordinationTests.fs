module LexScholar.Application.Tests.LibraryCoordinationTests

open System
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Domain
open Xunit

[<Fact; Trait("Task", "T003")>]
let ``summary persistence enters workspace boundary before registry`` () =
    let workspace = LibraryCoordination()
    let key = { Project = Guid.NewGuid(); Document = Guid.NewGuid() }
    let handle = TaskCompletionSource<JobHandle>(TaskCreationOptions.RunContinuationsAsynchronously)
    let finish = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable savedInsideBoundary = false
    let jobs =
        SummaryJobs(
            { Find = fun _ -> None
              Save = fun _ _ -> savedInsideBoundary <- workspace.IsMutationHeld
              Delete = fun _ -> ()
              AssociationExists = fun _ -> true }, coordination = workspace)
    let run given = task { handle.SetResult(given); do! finish.Task }
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, run))
    let id = handle.Task.GetAwaiter().GetResult().Id
    let summary =
        { DocumentId = key.Document; Points = []; PagesCovered = []; UnreadablePages = []
          OmittedPoints = 0; GeneratedAt = DateTimeOffset.UtcNow; ServiceHost = "local"; Model = "m" }
    Assert.True(jobs.Complete(key, id, summary))
    finish.SetResult()
    Assert.True(savedInsideBoundary, "The workspace boundary must be held before summary persistence")

[<Fact; Trait("Task", "T003")>]
let ``file lease serializes operations and cancelled waiters do not take a lease`` () = task {
    let coordination = LibraryCoordination()
    use! first = coordination.FileLease(CancellationToken.None)
    use cancellation = new CancellationTokenSource()
    let waiting = coordination.FileLease(cancellation.Token)
    Assert.False(waiting.IsCompleted)
    cancellation.Cancel()
    let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(fun () -> waiting :> Task)
    let next = coordination.FileLease(CancellationToken.None)
    Assert.False(next.IsCompleted)
    first.Dispose()
    use! acquired = next
    Assert.True(next.IsCompletedSuccessfully)
}

[<Fact; Trait("Task", "T003")>]
let ``summary admission and association removal run under the shared boundary`` () =
    let coordination = LibraryCoordination()
    let key = { Project = Guid.NewGuid(); Document = Guid.NewGuid() }
    let mutable checkedUnderBoundary = false
    let mutable deletedUnderBoundary = false
    let jobs =
        SummaryJobs(
            { Find = fun _ -> None
              Save = fun _ _ -> ()
              Delete = fun _ -> ()
              AssociationExists = fun _ -> checkedUnderBoundary <- coordination.IsMutationHeld; true },
            coordination = coordination)
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, fun _ -> Task.FromResult()))
    jobs.RemoveAssociation(key, fun () -> deletedUnderBoundary <- coordination.IsMutationHeld)
    Assert.True(checkedUnderBoundary)
    Assert.True(deletedUnderBoundary)

[<Fact; Trait("Task", "T003")>]
let ``summary work proceeds while simulated deletion waits for an export lease`` () = task {
    let coordination = LibraryCoordination()
    use! exportLease = coordination.FileLease(CancellationToken.None)
    let deletionLease = coordination.FileLease(CancellationToken.None)
    Assert.False(deletionLease.IsCompleted)
    let key = { Project = Guid.NewGuid(); Document = Guid.NewGuid() }
    let handle = TaskCompletionSource<JobHandle>(TaskCreationOptions.RunContinuationsAsynchronously)
    let finished = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable saved = false
    let jobs =
        SummaryJobs(
            { Find = fun _ -> None
              Save = fun _ _ -> saved <- coordination.IsMutationHeld
              Delete = fun _ -> ()
              AssociationExists = fun _ -> true }, coordination = coordination)
    let run given = task { handle.SetResult(given); do! finished.Task }
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, run))
    let! job = handle.Task
    let summary =
        { DocumentId = key.Document; Points = []; PagesCovered = []; UnreadablePages = []
          OmittedPoints = 0; GeneratedAt = DateTimeOffset.UtcNow; ServiceHost = "local"; Model = "m" }
    Assert.True(jobs.Complete(key, job.Id, summary))
    Assert.True(saved)
    Assert.False(deletionLease.IsCompleted)
    exportLease.Dispose()
    use! deletion = deletionLease
    let removed = coordination.Mutation(fun () -> jobs.Locked(fun () -> true))
    Assert.True(removed)
    finished.SetResult()
}
