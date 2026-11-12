namespace LexScholar.Application

open System.IO
open System.Threading
open System.Threading.Tasks

/// Validation borrows the caller-owned, seekable input until its task completes
/// and reads it from the start. The implementation must not dispose or retain
/// it, including on cancellation.
type ValidatePdf = Stream -> ImportLimits -> CancellationToken -> Task<Result<ValidatedPdf, PdfRejection>>
