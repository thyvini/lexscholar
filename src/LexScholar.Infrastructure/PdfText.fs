/// Page text of a stored original for summaries (F003, research.md section 3):
/// the original is opened with the reader's availability checks, the page count
/// is checked before any text is extracted, and each page's text is normalised.
module LexScholar.Infrastructure.PdfText

open System
open System.Text
open System.Threading
open System.Threading.Tasks
open UglyToad.PdfPig
open UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor
open LexScholar.Application
open LexScholar.Domain

/// Longest document that is summarized (FR-011).
let maximumPages = 100

/// Control characters removed, whitespace runs collapsed to one space.
let private normalise (text: string) =
    let builder = StringBuilder(text.Length)
    let mutable pendingSpace = false
    for character in text do
        if Char.IsWhiteSpace character then pendingSpace <- true
        elif not (Char.IsControl character) then
            if pendingSpace && builder.Length > 0 then builder.Append(' ') |> ignore
            pendingSpace <- false
            builder.Append(character) |> ignore
    builder.ToString()

let private read (stream: IO.Stream) (cancellationToken: CancellationToken) : Result<PageText list, Summarize.ExtractionError> =
    use document = PdfDocument.Open(stream)
    if document.NumberOfPages > maximumPages then
        Error Summarize.ExtractionError.TooManyPages
    else
        [ for number in 1 .. document.NumberOfPages do
            cancellationToken.ThrowIfCancellationRequested()
            // The number is the page's position, never read from its text.
            { Number = number; Text = normalise (ContentOrderTextExtractor.GetText(document.GetPage(number))) } ]
        |> Ok

let extractPages (dataDirectory: string) (documentId: Guid) (cancellationToken: CancellationToken)
    : Task<Result<PageText list, Summarize.ExtractionError>> = task {
    match! VerifiedDocumentContent.openRead dataDirectory documentId cancellationToken with
    | Error DocumentNotFound -> return Error Summarize.ExtractionError.DocumentNotFound
    | Error DocumentUnavailable -> return Error Summarize.ExtractionError.DocumentUnavailable
    | Ok content ->
        use stream = content.Stream
        try
            return! Task.Run((fun () -> read stream cancellationToken), cancellationToken)
        with ex when not (ex :? OperationCanceledException) ->
            return Error Summarize.ExtractionError.DocumentUnavailable }
