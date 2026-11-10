module LexScholar.Infrastructure.PdfValidator

open System
open System.IO
open System.Security.Cryptography
open UglyToad.PdfPig
open UglyToad.PdfPig.Exceptions
open LexScholar.Application

let private signature = "%PDF-"B

/// Counts and hashes the input from the start, reading at most MaxBytes + 1
/// bytes, and captures its first bytes for the signature check.
let private measure (input: Stream) (maxBytes: int64) cancellationToken = task {
    use hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
    let buffer = Array.zeroCreate<byte> 81_920
    let header = Array.zeroCreate<byte> signature.Length
    let mutable total = 0L
    let mutable finished = false
    while not finished && total <= maxBytes do
        let wanted = int (min (int64 buffer.Length) (maxBytes + 1L - total))
        let! read = input.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken)
        if read = 0 then
            finished <- true
        else
            if total < int64 header.Length then
                let start = int total
                Array.blit buffer 0 header start (min read (header.Length - start))
            hash.AppendData(buffer, 0, read)
            total <- total + int64 read
    return total, header, Convert.ToHexStringLower(hash.GetHashAndReset()) }

/// PdfPig's Open(Stream) leaves the caller's stream open. Failures reading the
/// input are I/O errors, not evidence of a corrupt PDF, so they propagate; none
/// of PdfPig's own exceptions derive from IOException.
let private pageCount (input: Stream) =
    try
        use document = PdfDocument.Open(input)
        Ok document.NumberOfPages
    with
    | :? PdfDocumentEncryptedException -> Error Encrypted
    | :? OperationCanceledException
    | :? IOException
    | :? UnauthorizedAccessException -> reraise ()
    | _ -> Error Corrupt

let validate : ValidatePdf =
    fun input limits cancellationToken -> task {
        if not input.CanSeek then
            invalidArg (nameof input) "PDF validation requires a seekable stream."
        cancellationToken.ThrowIfCancellationRequested()
        input.Seek(0L, SeekOrigin.Begin) |> ignore
        let! byteLength, header, sha256 = measure input limits.MaxBytes cancellationToken
        if byteLength > limits.MaxBytes then
            return Error (TooLarge limits.MaxBytes)
        elif byteLength < int64 signature.Length || header <> signature then
            return Error NotPdf
        else
            cancellationToken.ThrowIfCancellationRequested()
            input.Seek(0L, SeekOrigin.Begin) |> ignore
            return
                pageCount input
                |> Result.bind (fun pages ->
                    if pages > limits.MaxPages then Error (TooManyPages limits.MaxPages)
                    else Ok { Sha256 = sha256; ByteLength = byteLength; PageCount = pages }) }
