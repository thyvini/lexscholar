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
      Source: CancellationTokenSource
      /// The token the job's service calls receive and `Complete` checks: the source's own, or a plan run's (cancelled by sign-out).
      Effective: CancellationToken
      mutable State: JobState
      mutable Run: Task }

type SummaryJobs(capabilities: SummaryJobCapabilities) =
    let gate = obj ()
    let jobs = System.Collections.Generic.Dictionary<SummaryKey, Job>()

    /// The job when the registry still holds exactly this id for the key. Call inside the lock.
    let current key id =
        match jobs.TryGetValue key with
        | true, job when job.Id = id -> Some job
        | _ -> None

    /// Ends a job: its token source is no longer needed. Call inside the lock.
    let end' (job: Job) state =
        job.State <- state
        job.Source.Dispose()

    member _.Capabilities = capabilities

    /// Runs `work` under the registry lock.
    member _.Locked(work: unit -> 'a) : 'a = lock gate work

    /// Starts `run` as the job for `key` (research §1, §3) with a token source the caller made (a plan job needs it before
    /// `prepareSend`) and the token its calls receive (`effective`: the source's own, or the plan run's, which sign-out
    /// cancels). Under the lock, in order: the pair is no longer in the project -> `NotAssociated`; a job is running ->
    /// `AlreadyRunning`; a failed job is replaced; a finished or stored summary -> `AlreadyFinished`; otherwise the job
    /// starts and owns `source`. For any other outcome the caller still owns it.
    member _.StartWith(key: SummaryKey, source: CancellationTokenSource, effective: CancellationToken, run: JobHandle -> Task<unit>) : StartOutcome =
        lock gate (fun () ->
            if not (capabilities.AssociationExists key) then NotAssociated
            else
                match jobs.TryGetValue key with
                | true, { State = JobState.Running _ } -> AlreadyRunning
                | true, { State = JobState.Finished _ } -> AlreadyFinished
                // A failed job is replaced; a stored summary is as good as a finished job.
                | _ when (capabilities.Find key).IsSome -> AlreadyFinished
                | _ ->
                    let job = { Id = Guid.NewGuid(); Source = source; Effective = effective; State = JobState.Running None; Run = Task.CompletedTask }
                    jobs[key] <- job
                    let handle = { Id = job.Id; Token = effective }
                    // The task runs on the pool; whatever it needs from the registry waits for this lock.
                    job.Run <- Task.Run(fun () -> run handle :> Task)
                    Started)

    /// `StartWith` for a job with its own token, linked to `stopping`, never to a request.
    member this.Start(key: SummaryKey, stopping: CancellationToken, run: JobHandle -> Task<unit>) : StartOutcome =
        let source = CancellationTokenSource.CreateLinkedTokenSource stopping
        match this.StartWith(key, source, source.Token, run) with
        | Started -> Started
        | refused ->
            source.Dispose()
            refused

    /// Creates the job for `key` already failed (a plan outcome decided before anything was sent). The same refusals as
    /// `StartWith`: only a failed job is replaced.
    member _.StartFailed(key: SummaryKey, code: string, detail: string) : StartOutcome =
        lock gate (fun () ->
            if not (capabilities.AssociationExists key) then NotAssociated
            else
                match jobs.TryGetValue key with
                | true, { State = JobState.Running _ } -> AlreadyRunning
                | true, { State = JobState.Finished _ } -> AlreadyFinished
                | _ when (capabilities.Find key).IsSome -> AlreadyFinished
                | _ ->
                    let source = new CancellationTokenSource()
                    source.Dispose()
                    jobs[key] <- { Id = Guid.NewGuid(); Source = source; Effective = CancellationToken.None
                                   State = JobState.Failed (code, detail, None); Run = Task.CompletedTask }
                    Started)

    /// Finishes the job `id` of `key` with its summary; false when the registry no longer holds that job, or when its
    /// effective token is already cancelled (a plan job after sign-out): then the job ends `Failed signed_out` and
    /// nothing is kept (research §3, AR 9). No sign-in lock is taken: a sign-out landing after this check is allowed to lose.
    member _.Complete(key: SummaryKey, id: Guid, summary: StoredSummary) : bool =
        lock gate (fun () ->
            match current key id with
            | Some job when (match job.State with JobState.Running _ -> true | _ -> false) ->
                if job.Effective.IsCancellationRequested then
                    end' job (JobState.Failed ("signed_out", Summarize.failureDetail Summarize.ServiceError.SignedOut, None))
                    false
                else
                    end' job (JobState.Finished summary)
                    true
            | _ -> false)

    /// Ends the job `id` of `key` as failed; false when the registry no longer holds that job.
    member _.Fail(key: SummaryKey, id: Guid, code: string, detail: string, omitted: int option) : bool =
        lock gate (fun () ->
            match current key id with
            | Some job when (match job.State with JobState.Running _ -> true | _ -> false) ->
                end' job (JobState.Failed (code, detail, omitted))
                true
            | _ -> false)

    /// Records the progress of the running job `id` of `key`; false when the registry no longer holds that job running.
    member _.Progress(key: SummaryKey, id: Guid, progress: Progress) : bool =
        false

    member _.State(key: SummaryKey) : JobState option =
        lock gate (fun () ->
            match jobs.TryGetValue key with
            | true, job -> Some job.State
            | _ -> None)

    /// Completes when the key's current job has ended (tests wait on this instead of polling).
    member _.Settled(key: SummaryKey) : Task =
        lock gate (fun () ->
            match jobs.TryGetValue key with
            | true, job -> job.Run
            | _ -> Task.CompletedTask)
