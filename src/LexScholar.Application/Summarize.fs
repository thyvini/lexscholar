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

/// LexScholar's own explanation for a failed call; never the service's body, prompt, text or key.
let failureDetail (error: ServiceError) : string =
    match error with
    | ServiceError.ErrorStatus 400 ->
        "The summary service rejected the request. Check the Model setting, and the optional Temperature and MaxOutputTokens settings. Try again."
    | ServiceError.ErrorStatus _ -> "The summary service answered with an error. Try again."
    | ServiceError.Unreachable -> "The summary service could not be reached. Check that it is running and the Endpoint setting. Try again."
    | ServiceError.TimedOut -> "The summary service took too long to answer. Try again."
    | ServiceError.Redirected -> "The summary service redirected the request, which LexScholar does not follow. Check the Endpoint setting. Try again."
    | ServiceError.UnusableOutput -> "The summary service did not give a usable answer. Try again."

let private jsonForm = "{\"points\": [{\"text\": \"...\", \"pages\": [3, 4]}]}"

let private systemPrompt =
    "You summarize a document for a researcher. The text is split by page markers such as [Page 7]. "
    + "Write 5 to 10 main points, in the language of most of the text. Each point cites the page markers it comes from. "
    + $"Answer only with JSON of this form and nothing else: {jsonForm}. "
    + "Use {\"points\": []} when the text has no main points. Cite only pages that appear as markers."

let private mapPrompt =
    "You summarize part of a document for a researcher. The text is split by page markers such as [Page 7]. "
    + "Write at most 5 main points of this part, in the language of most of the text. Each point cites the page markers it comes from. "
    + $"Answer only with JSON of this form and nothing else: {jsonForm}. "
    + "Use {\"points\": []} when the text has no main points. Cite only pages that appear as markers."

let private combinePrompt (final: bool) =
    let count = if final then "5 to 10 main points" else "at most 5 main points"
    "You merge points from a document into a shorter list for a researcher. Each line is a point, with the pages it cites in parentheses. "
    + $"Write {count} that cover them, in the language of the points. Each point cites pages taken only from the lines it merges. "
    + $"Answer only with JSON of this form and nothing else: {jsonForm}."

/// Combining stops after this many rounds (research §4).
let maximumRounds = 5

let private ask (capabilities: Capabilities) system (user: string) (allowed: Set<int>) (cancellation: CancellationToken)
    : Task<Result<ValidatedPoints, SummaryFailure>> = task {
    // An abandoned request makes no further service call (FR-009), whatever the capability does with the token.
    cancellation.ThrowIfCancellationRequested()
    let messages = [ { Role = "system"; Content = system }; { Role = "user"; Content = user } ]
    match! capabilities.Complete messages cancellation with
    | Error error -> return Error (SummaryFailure.ServiceFailed error)
    | Ok output ->
        match Summaries.parsePoints allowed output with
        | Error Unparseable -> return Error (SummaryFailure.ServiceFailed ServiceError.UnusableOutput)
        | Ok validated -> return Ok validated }

let private finished (validated: ValidatedPoints) =
    if List.isEmpty validated.Points then Error (SummaryFailure.NotEnoughText validated.Omitted) else Ok validated

/// Map: each chunk in order gives at most 5 points of its own pages; progress counts a page
/// once its last part is done. Returns all points and the omitted count. `points` is
/// accumulated newest first and reversed once at the end.
let rec private mapChunks capabilities (total: int) (unreadable: int) (remaining: Chunk list) (done': Set<int>) points omitted cancellation
    : Task<Result<SummaryPoint list * int, SummaryFailure>> = task {
    match remaining with
    | [] -> return Ok (List.rev points, omitted)
    | chunk :: rest ->
        match! ask capabilities mapPrompt chunk.Content (Set.ofList chunk.PageNumbers) cancellation with
        | Error failure -> return Error failure
        | Ok validated ->
            let later = rest |> List.collect (fun c -> c.PageNumbers) |> Set.ofList
            let finishedPages = Set.union done' (Set.ofList chunk.PageNumbers) |> fun seen -> Set.difference seen later
            do! capabilities.Report (Progress.PagesProcessed (unreadable + finishedPages.Count, total))
            return! mapChunks capabilities total unreadable rest finishedPages (List.rev validated.Points @ points) (omitted + validated.Omitted) cancellation }

let private combineGroup capabilities final (group: SummaryPoint list) cancellation =
    let allowed = group |> List.collect (fun point -> point.Pages) |> Set.ofList
    ask capabilities (combinePrompt final) (group |> List.map Summaries.pointLine |> String.concat "\n") allowed cancellation

/// Combine: groups of points that fit the chunk limit are merged, round after round, until one
/// group gives the final points. A round that does not reduce the points, or a sixth round, fails.
let rec private combineRounds capabilities (maxChunkCharacters: int) round (points: SummaryPoint list) omitted cancellation
    : Task<Result<ValidatedPoints, SummaryFailure>> = task {
    if List.isEmpty points then return Error (SummaryFailure.NotEnoughText omitted)
    elif round >= maximumRounds then return Error (SummaryFailure.ServiceFailed ServiceError.UnusableOutput)
    else
        let groups = Summaries.groupPoints maxChunkCharacters points
        let final = groups.Length = 1
        do! capabilities.Report Progress.Combining
        let mutable merged = [] // newest first, reversed once below
        let mutable omittedNow = omitted
        let mutable failure = None
        for group in groups do
            if failure.IsNone then
                match! combineGroup capabilities final group cancellation with
                | Error error -> failure <- Some error
                | Ok validated ->
                    merged <- List.rev validated.Points @ merged
                    omittedNow <- omittedNow + validated.Omitted
        match failure with
        | Some error -> return Error error
        | None when final -> return finished { Points = List.rev merged; Omitted = omittedNow }
        | None when merged.Length >= points.Length -> return Error (SummaryFailure.ServiceFailed ServiceError.UnusableOutput)
        | None -> return! combineRounds capabilities maxChunkCharacters (round + 1) (List.rev merged) omittedNow cancellation }

/// Summary points for the readable pages of a document. A document that fits one chunk is one
/// call; longer ones are mapped chunk by chunk and combined in bounded rounds. Every point
/// cites only pages that were sent in the call that produced it.
let summarize (capabilities: Capabilities) (maxChunkCharacters: int) (pages: ReadablePages) (cancellation: CancellationToken)
    : Task<Result<ValidatedPoints, SummaryFailure>> = task {
    match Summaries.chunk maxChunkCharacters pages.Readable with
    | [ only ] ->
        let! result = ask capabilities systemPrompt only.Content (Set.ofList only.PageNumbers) cancellation
        return result |> Result.bind finished
    | chunks ->
        let total = pages.Readable.Length + pages.Unreadable.Length
        let unreadable = pages.Unreadable.Length
        do! capabilities.Report (Progress.PagesProcessed (unreadable, total))
        match! mapChunks capabilities total unreadable chunks Set.empty [] 0 cancellation with
        | Error failure -> return Error failure
        | Ok (points, omitted) -> return! combineRounds capabilities maxChunkCharacters 0 points omitted cancellation }
