/// Page text of a stored original for summaries (F003, research.md section 3).
module LexScholar.Infrastructure.PdfText

open System
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Domain

let extractPages (dataDirectory: string) (documentId: Guid) (cancellationToken: CancellationToken)
    : Task<Result<PageText list, Summarize.ExtractionError>> =
    Task.FromResult(Ok [])
