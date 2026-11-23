/// The registry of background summary jobs (F004, research §3). One lock guards every state change.
/// T001 gives the shape only; the members arrive with T002 onwards.
namespace LexScholar.Application

open System
open System.Threading
open System.Threading.Tasks
open LexScholar.Domain

/// What `Start` decided for a key.
type StartOutcome =
    | Started
    | AlreadyRunning
    | AlreadyFinished
    | NotAssociated
    | NoSavedSummary
    | Cancelled

/// The capabilities the registry needs; all are called inside its lock.
type SummaryJobCapabilities =
    { Find: SummaryKey -> StoredSummary option
      Save: SummaryKey -> StoredSummary -> unit
      Delete: SummaryKey -> unit
      AssociationExists: SummaryKey -> bool }

/// What a running job is given: its id and the token every service call it makes must receive (research §3).
type JobHandle = { Id: Guid; Token: CancellationToken }

/// One job in the registry: its id, its token source, its state and the task that runs it.
type private Job =
    { Id: Guid
      Regeneration: bool
      Source: CancellationTokenSource
      /// The token the job's service calls receive and `Complete` checks: the source's own, or a plan run's (cancelled by sign-out).
      Effective: CancellationToken
      mutable State: JobState
      mutable Run: Task }

type SummaryJobs(capabilities: SummaryJobCapabilities, ?coordination: LibraryCoordination) =
    let gate = obj ()
    let coordination = defaultArg coordination (LibraryCoordination())
    let locked work = coordination.Mutation(fun () -> lock gate work)
    let jobs = System.Collections.Generic.Dictionary<SummaryKey, Job>()
    // Only in-flight HTTP preparations live here. Cancellation fences them before they can register a job.
    let preparing = System.Collections.Generic.Dictionary<SummaryKey, System.Collections.Generic.Dictionary<Guid, CancellationTokenSource>>()

    let cancelPreparing key =
        match preparing.TryGetValue key with
        | true, requests ->
            for source in requests.Values do source.Cancel()
        | _ -> ()

    /// The job when the registry still holds exactly this id for the key. Call inside the lock.
    let current key id =
        match jobs.TryGetValue key with
        | true, job when job.Id = id -> Some job
        | _ -> None

    /// Ends a job: its token source is no longer needed. Call inside the lock.
    let end' (job: Job) state =
        job.State <- state
        job.Source.Dispose()

    /// Removes the job for `key` and cancels a running one. Call inside the lock. The job is removed first, so a late
    /// `Complete`, `Fail` or `Progress` of it finds nothing.
    let dropJob key =
        match jobs.TryGetValue key with
        | true, job ->
            jobs.Remove key |> ignore
            match job.State with
            | JobState.Running _ ->
                job.Source.Cancel()
                job.Source.Dispose()
            | _ -> ()
        | _ -> ()

    member _.Capabilities = capabilities

    /// Runs `work` under the registry lock.
    member _.Locked(work: unit -> 'a) : 'a = locked work

    /// Register before the first async preparation. Dispose at request end; Cancel/Stop/removal invalidate all
    /// preparations for the pair. Registration checks this token under the same lock as cancellation.
    member _.PrepareReplacement(key: SummaryKey) : CancellationToken * IDisposable =
        locked (fun () ->
            let id = Guid.NewGuid()
            let source = new CancellationTokenSource()
            let requests =
                match preparing.TryGetValue key with
                | true, requests -> requests
                | _ ->
                    let requests = System.Collections.Generic.Dictionary<Guid, CancellationTokenSource>()
                    preparing[key] <- requests
                    requests
            requests[id] <- source
            source.Token,
            { new IDisposable with
                member _.Dispose() =
                    locked (fun () ->
                        requests.Remove id |> ignore
                        if requests.Count = 0 then preparing.Remove key |> ignore
                        source.Dispose()) })

    /// Starts `run` as the job for `key` (research §1, §3) with a token source the caller made (a plan job needs it before
    /// `prepareSend`) and the token its calls receive (`effective`: the source's own, or the plan run's, which sign-out
    /// cancels). Under the lock, in order: the pair is no longer in the project -> `NotAssociated`; a job is running ->
    /// `AlreadyRunning`; a failed job is replaced; a finished or stored summary -> `AlreadyFinished`; otherwise the job
    /// starts and owns `source`. Explicit regeneration instead requires a stored summary and leaves it untouched until
    /// successful completion; a running job still wins. For any other outcome the caller still owns the source.
    member _.StartWith(key: SummaryKey, source: CancellationTokenSource, effective: CancellationToken, run: JobHandle -> Task<unit>, ?regenerate: bool, ?admission: CancellationToken) : StartOutcome =
        locked (fun () ->
            if (defaultArg admission CancellationToken.None).IsCancellationRequested then Cancelled
            elif not (capabilities.AssociationExists key) then NotAssociated
            else
                match jobs.TryGetValue key with
                | true, { State = JobState.Running _ } -> AlreadyRunning
                | _ when defaultArg regenerate false && (capabilities.Find key).IsNone -> NoSavedSummary
                | true, { State = JobState.Finished _ } when not (defaultArg regenerate false) -> AlreadyFinished
                // A failed job is replaced; a stored summary is as good as a finished job.
                | _ when not (defaultArg regenerate false) && (capabilities.Find key).IsSome -> AlreadyFinished
                | _ ->
                    let job = { Id = Guid.NewGuid(); Regeneration = defaultArg regenerate false; Source = source; Effective = effective; State = JobState.Running None; Run = Task.CompletedTask }
                    jobs[key] <- job
                    let handle = { Id = job.Id; Token = effective }
                    // The task runs on the pool; whatever it needs from the registry waits for this lock.
                    job.Run <- Task.Run(fun () -> run handle :> Task)
                    Started)

    /// `StartWith` for a job with its own token, linked to `stopping`, never to a request.
    member this.Start(key: SummaryKey, stopping: CancellationToken, run: JobHandle -> Task<unit>, ?regenerate: bool, ?admission: CancellationToken) : StartOutcome =
        let source = CancellationTokenSource.CreateLinkedTokenSource stopping
        match this.StartWith(key, source, source.Token, run, ?regenerate = regenerate, ?admission = admission) with
        | Started -> Started
        | refused ->
            source.Dispose()
            refused

    /// Creates the job for `key` already failed (a plan outcome decided before anything was sent). The same refusals as
    /// `StartWith`, including explicit regeneration's durable-row requirement.
    member _.StartFailed(key: SummaryKey, code: string, detail: string, ?regenerate: bool, ?admission: CancellationToken) : StartOutcome =
        locked (fun () ->
            if (defaultArg admission CancellationToken.None).IsCancellationRequested then Cancelled
            elif not (capabilities.AssociationExists key) then NotAssociated
            else
                match jobs.TryGetValue key with
                | true, { State = JobState.Running _ } -> AlreadyRunning
                | _ when defaultArg regenerate false && (capabilities.Find key).IsNone -> NoSavedSummary
                | true, { State = JobState.Finished _ } when not (defaultArg regenerate false) -> AlreadyFinished
                | _ when not (defaultArg regenerate false) && (capabilities.Find key).IsSome -> AlreadyFinished
                | _ ->
                    let source = new CancellationTokenSource()
                    source.Dispose()
                    jobs[key] <- { Id = Guid.NewGuid(); Regeneration = defaultArg regenerate false; Source = source; Effective = CancellationToken.None
                                   State = JobState.Failed (code, detail, None); Run = Task.CompletedTask }
                    Started)

    /// Finishes the job `id` of `key` with its summary; false when the registry no longer holds that job, or when its
    /// effective token is already cancelled (a plan job after sign-out): then the job ends `Failed signed_out` and
    /// nothing new is kept (an earlier durable version is untouched). No sign-in lock is taken: a sign-out landing after this check is allowed to lose.
    member _.Complete(key: SummaryKey, id: Guid, summary: StoredSummary) : bool =
        locked (fun () ->
            match current key id with
            | Some job when (match job.State with JobState.Running _ -> true | _ -> false) ->
                if job.Effective.IsCancellationRequested then
                    end' job (JobState.Failed ("signed_out", Summarize.failureDetail Summarize.ServiceError.SignedOut, None))
                    false
                else
                    // Saved inside the lock, before the job is dropped: a `Dismiss` or a restart sees the row or the job, never neither.
                    // A failed save leaves the job running for the caller to `Fail`.
                    capabilities.Save key summary
                    end' job (JobState.Finished summary)
                    jobs.Remove key |> ignore
                    true
            | _ -> false)

    /// Ends the job `id` of `key` as failed; false when the registry no longer holds that job.
    member _.Fail(key: SummaryKey, id: Guid, code: string, detail: string, omitted: int option) : bool =
        locked (fun () ->
            match current key id with
            | Some job when (match job.State with JobState.Running _ -> true | _ -> false) ->
                end' job (JobState.Failed (code, detail, omitted))
                true
            | _ -> false)

    /// Records the progress of the running job `id` of `key`; false when the registry no longer holds that job running.
    member _.Progress(key: SummaryKey, id: Guid, progress: Progress) : bool =
        locked (fun () ->
            match current key id with
            | Some job when (match job.State with JobState.Running _ -> true | _ -> false) ->
                job.State <- JobState.Running (Some progress)
                true
            | _ -> false)

    /// The job's state, else the stored summary (a finished job is dropped once saved). Legacy callers set
    /// includeReplacement=false so replacement progress/failure cannot mask the durable version.
    member _.State(key: SummaryKey, ?includeReplacement: bool) : JobState option =
        locked (fun () ->
            match jobs.TryGetValue key with
            | true, job when not job.Regeneration || defaultArg includeReplacement true -> Some job.State
            | _ -> capabilities.Find key |> Option.map JobState.Finished)

    /// The status of every pair with a job (running, or failed: both live in memory) and of every stored summary, read
    /// under the lock so a job that completes is seen once, as stored (`Complete` saves inside the lock, then drops the job).
    /// Legacy list markers see replacement jobs as finished, matching their durable version.
    member _.Snapshot(listStored: unit -> SummaryKey list) : (SummaryKey * SummaryStatus) list =
        locked (fun () ->
            let live = [ for pair in jobs -> pair.Key, (if pair.Value.Regeneration then SummaryStatus.Finished else Summaries.statusOf pair.Value.State) ]
            let stored = [ for key in listStored () do if not (jobs.ContainsKey key) then key, SummaryStatus.Finished ]
            live @ stored)

    /// Stops a running summary, or dismisses a finished or failed one; idempotent (research §3). Under the lock: the job is
    /// removed first, so a late `Complete`, `Fail` or `Progress` of it finds nothing, then its token is cancelled (no new call
    /// starts, one in flight is aborted) and the stored row is deleted (finishing may have won the race: nothing stopped is shown).
    member _.Stop(key: SummaryKey) : unit =
        locked (fun () ->
            cancelPreparing key
            dropJob key
            capabilities.Delete key)

    /// Cancels/dismisses only the transient operation. Never deletes a saved summary, even if completion won.
    member _.Cancel(key: SummaryKey) : unit =
        locked (fun () -> cancelPreparing key; dropJob key)

    /// Removes the pair from the project (research §3 "Removal"): under the lock, drops and cancels its job, then runs
    /// `delete` (the association delete) and returns its result. The stored summary is NOT deleted here: its row goes with
    /// the association (ON DELETE CASCADE), in the same database operation, so a failed or interrupted removal never loses
    /// a summary of a document that stays in the project. `Start` checks the association inside the same lock, so it cannot
    /// start a job for a pair removed here.
    member _.RemoveAssociation(key: SummaryKey, delete: unit -> 'a) : 'a =
        locked (fun () ->
            cancelPreparing key
            dropJob key
            delete ())

    /// Completes when the key's current job has ended (tests wait on this instead of polling).
    member _.Settled(key: SummaryKey) : Task =
        locked (fun () ->
            match jobs.TryGetValue key with
            | true, job -> job.Run
            | _ -> Task.CompletedTask)
