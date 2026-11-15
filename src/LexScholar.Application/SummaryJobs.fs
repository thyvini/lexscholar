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

type SummaryJobs(capabilities: SummaryJobCapabilities) =
    let gate = obj ()

    member _.Capabilities = capabilities

    /// Runs `work` under the registry lock.
    member _.Locked(work: unit -> 'a) : 'a = lock gate work

    /// Starts `run` as the job for `key` (research §1, §3), in the order of the plan's design.
    member _.Start(_key: SummaryKey, _stopping: CancellationToken, _run: JobHandle -> Task<unit>) : StartOutcome =
        failwith "not implemented (T002)"

    /// Finishes the job `id` of `key` with its summary; false when the registry no longer holds that job.
    member _.Complete(_key: SummaryKey, _id: Guid, _summary: StoredSummary) : bool = failwith "not implemented (T002)"

    /// Ends the job `id` of `key` as failed; false when the registry no longer holds that job.
    member _.Fail(_key: SummaryKey, _id: Guid, _code: string, _detail: string, _omitted: int option) : bool =
        failwith "not implemented (T002)"

    member _.State(_key: SummaryKey) : JobState option = failwith "not implemented (T002)"

    /// Completes when the key's current job has ended (tests wait on this instead of polling).
    member _.Settled(_key: SummaryKey) : Task = failwith "not implemented (T002)"
