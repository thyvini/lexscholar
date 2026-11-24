namespace LexScholar.Application

open System
open System.Threading

/// Process-local coordination shared by import, library changes and summaries.
type LibraryCoordination() =
    let mutation = obj ()
    let files = new SemaphoreSlim(1, 1)
    let fileWait = Event<unit>()

    member _.Mutation(work: unit -> 'a) = lock mutation work
    member _.IsMutationHeld = Monitor.IsEntered mutation
    member _.FileWait = fileWait.Publish

    /// Held across asynchronous file operations, never under the registry lock.
    member _.FileLease(cancellationToken: CancellationToken) = task {
        let waiting = files.WaitAsync(cancellationToken)
        if not waiting.IsCompleted then fileWait.Trigger()
        do! waiting
        let mutable released = 0
        return { new IDisposable with
                    member _.Dispose() =
                        if Interlocked.Exchange(&released, 1) = 0 then files.Release() |> ignore } }

/// The durable phases needed to resume a confirmed library deletion.
[<RequireQualifiedAccess>]
type DeletionPhase =
    | Pending
    | MetadataRemoved

/// A journal item is complete only after its managed original is absent.
[<RequireQualifiedAccess>]
type DeletionStatus =
    | Pending
    | MetadataRemoved
    | Deleted
    | Incomplete

type DeletionOperation = { Id: Guid; CreatedUtc: DateTimeOffset }

type DeletionItem =
    { OperationId: Guid
      DocumentId: Guid
      OriginalFileName: string
      StorageKey: string
      Status: DeletionStatus
      Phase: DeletionPhase
      Reason: string option }
