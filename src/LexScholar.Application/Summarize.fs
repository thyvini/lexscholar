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

let private systemPrompt =
    "You summarize a document for a researcher. The text is split by page markers such as [Page 7]. "
    + "Write 5 to 10 main points, in the language of most of the text. Each point cites the page markers it comes from. "
    + "Answer only with JSON of this form and nothing else: {\"points\": [{\"text\": \"...\", \"pages\": [3, 4]}]}. "
    + "Use {\"points\": []} when the text has no main points. Cite only pages that appear as markers."

let private userPrompt (pages: PageText list) =
    pages |> List.map (fun page -> $"[Page {page.Number}]\n{page.Text}") |> String.concat "\n\n"

/// Summary points for the readable pages of a document that fits one request
/// (longer documents are chunked in T006). Every point cites only pages that were sent.
let summarize (capabilities: Capabilities) (pages: ReadablePages) (cancellation: CancellationToken)
    : Task<Result<ValidatedPoints, SummaryFailure>> = task {
    let messages =
        [ { Role = "system"; Content = systemPrompt }
          { Role = "user"; Content = userPrompt pages.Readable } ]
    match! capabilities.Complete messages cancellation with
    | Error error -> return Error (SummaryFailure.ServiceFailed error)
    | Ok output ->
        let allowed = pages.Readable |> List.map (fun page -> page.Number) |> Set.ofList
        match Summaries.parsePoints allowed output with
        | Error Unparseable -> return Error (SummaryFailure.ServiceFailed ServiceError.UnusableOutput)
        | Ok validated when List.isEmpty validated.Points -> return Error (SummaryFailure.NotEnoughText validated.Omitted)
        | Ok validated -> return Ok validated }
