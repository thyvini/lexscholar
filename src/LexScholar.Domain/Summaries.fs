/// Types for summaries of a document (F003). Nothing here is stored; rules
/// (readability, chunking, parsing points) arrive with the slices that need them.
namespace LexScholar.Domain

/// Whether the configured service runs on this machine or elsewhere (FR-014).
type ServiceLocation =
    | Local
    | Elsewhere

/// Text of one page; `Number` is the page position, first page = 1.
type PageText = { Number: int; Text: string }

/// Why a document has too little text to summarize.
type NoTextReason =
    | NoReadableText
    | NotEnoughText

/// Pages split by whether they have readable text.
type ReadablePages =
    { Readable: PageText list
      Unreadable: int list }

/// Consecutive text from whole pages (or part of one long page), sent in one request.
type Chunk = { Content: string; PageNumbers: int list }

/// One generated point and the readable pages it cites.
type SummaryPoint = { Text: string; Pages: int list }

/// Points that passed citation validation, and how many were dropped.
type ValidatedPoints =
    { Points: SummaryPoint list
      Omitted: int }

/// A finished summary with the context shown next to it.
type DocumentSummary =
    { DocumentId: System.Guid
      Points: SummaryPoint list
      PagesCovered: int list
      UnreadablePages: int list
      OmittedPoints: int
      GeneratedAt: System.DateTimeOffset
      ServiceHost: string
      Model: string }

/// The service's answer could not be read as the requested JSON.
type Unparseable = Unparseable

module Summaries =
    /// Fewer readable characters than this in total is not enough to summarize.
    let minimumReadableCharacters = 200

    let private hasLetterOrDigit (text: string) = text |> Seq.exists System.Char.IsLetterOrDigit

    /// Splits pages into readable (some letter or digit) and unreadable, and
    /// refuses documents with no readable page or fewer than 200 readable characters.
    let readability (pages: PageText list) : Result<ReadablePages, NoTextReason> =
        let readable, unreadable = pages |> List.partition (fun page -> hasLetterOrDigit page.Text)
        if List.isEmpty readable then Error NoReadableText
        elif readable |> List.sumBy (fun page -> page.Text.Length) < minimumReadableCharacters then Error NotEnoughText
        else Ok { Readable = readable; Unreadable = unreadable |> List.map (fun page -> page.Number) }


    /// Reads the model's answer into validated points (research §5). RED stub.
    let parsePoints (allowedPages: Set<int>) (output: string) : Result<ValidatedPoints, Unparseable> = Error Unparseable
