/// Capabilities and failures of summarizing a document (F003). The workflow
/// itself arrives with T003; it takes only the functions below.
module LexScholar.Application.Summarize

open System
open System.Threading
open System.Threading.Tasks
open LexScholar.Domain

/// Why a document's pages could not be read.
[<RequireQualifiedAccess>]
type ExtractionError =
    | DocumentNotFound
    | DocumentUnavailable
    | TooManyPages

/// Why one call to the configured service failed. Never carries the service's body.
[<RequireQualifiedAccess>]
type ServiceError =
    | Unreachable
    | ErrorStatus of status: int
    | Redirected
    | TimedOut
    | UnusableOutput

/// Messages are (role, content) pairs for one chat completion.
type ChatMessage = { Role: string; Content: string }

[<RequireQualifiedAccess>]
type Progress =
    | PagesProcessed of processed: int * total: int
    | Combining

type Capabilities =
    { ExtractPages: Guid -> Task<Result<PageText list, ExtractionError>>
      Complete: ChatMessage list -> CancellationToken -> Task<Result<string, ServiceError>>
      Report: Progress -> Task<unit> }

[<RequireQualifiedAccess>]
type SummaryFailure =
    | NotEnoughText of omitted: int
    | ServiceFailed of ServiceError

/// Summary points for the readable pages of a document that fits one request. RED stub.
let summarize (capabilities: Capabilities) (pages: ReadablePages) (cancellation: CancellationToken)
    : Task<Result<ValidatedPoints, SummaryFailure>> =
    Task.FromResult(Error (SummaryFailure.ServiceFailed ServiceError.UnusableOutput))
