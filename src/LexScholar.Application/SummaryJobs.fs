/// The registry of background summary jobs (F004, research §3). One lock guards every state change.
/// T001 gives the shape only; the members arrive with T002 onwards.
namespace LexScholar.Application

open System
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

type SummaryJobs(capabilities: SummaryJobCapabilities) =
    let gate = obj ()

    member _.Capabilities = capabilities

    /// Runs `work` under the registry lock.
    member _.Locked(work: unit -> 'a) : 'a = lock gate work

    member _.State(_key: SummaryKey) : JobState option = failwith "not implemented (T002)"
