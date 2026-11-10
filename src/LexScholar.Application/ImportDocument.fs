module LexScholar.Application.ImportDocument

open System
open System.IO
open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Tasks
open LexScholar.Domain

/// Only what the import workflow uses; passed explicitly by the composition root.
type ImportDependencies =
    { Limits: ImportLimits
      Validate: ValidatePdf
      OpenStagedWrite: OpenStagedWrite
      OpenStagedRead: OpenStagedRead
      Promote: PromoteStagedContent
      Discard: DiscardStagedContent
      OpenOriginal: OpenStoredOriginal
      FindBySha256: FindDocumentsBySha256
      Insert: InsertDocument
      Now: unit -> DateTimeOffset }

/// The document an import produced, and whether it already existed.
type ImportOutcome = { Document: LibraryDocument; Reused: bool }

type Import = Stream -> string -> CancellationToken -> Task<Result<ImportOutcome, PdfRejection>>

let private bufferSize = 81_920

/// Reads until the buffer is full or the stream ends; returns the bytes read.
let private fill (stream: Stream) (buffer: byte array) (cancellationToken: CancellationToken) = task {
    let mutable total = 0
    let mutable finished = false
    while not finished && total < buffer.Length do
        let! read = stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)
        if read = 0 then finished <- true else total <- total + read
    return total }

let private sameBytes (left: byte array) (right: byte array) count =
    ReadOnlySpan(left, 0, count).SequenceEqual(ReadOnlySpan(right, 0, count))

/// Compares two streams byte for byte from their current positions.
let contentEquals (left: Stream) (right: Stream) (cancellationToken: CancellationToken) : Task<bool> = task {
    let leftBuffer = Array.zeroCreate<byte> bufferSize
    let rightBuffer = Array.zeroCreate<byte> bufferSize
    let mutable result = None
    while result.IsNone do
        let! leftCount = fill left leftBuffer cancellationToken
        let! rightCount = fill right rightBuffer cancellationToken
        if leftCount <> rightCount || not (sameBytes leftBuffer rightBuffer leftCount) then result <- Some false
        elif leftCount = 0 then result <- Some true
    return result.Value }

/// Display-only basename for an uploaded file; never used as a storage path.
let sanitizeFileName (fileName: string) : string =
    let raw = if isNull fileName then "" else fileName
    let basename = raw.Split([| '/'; '\\' |]) |> Array.last
    let cleaned = String(basename |> Seq.filter (Char.IsControl >> not) |> Seq.toArray).Trim()
    if cleaned.Length = 0 then "document.pdf" else cleaned

/// Copies at most `limit` bytes, so an oversized upload cannot fill the disk.
let private copyAtMost (source: Stream) (target: Stream) (limit: int64) (cancellationToken: CancellationToken) = task {
    let buffer = Array.zeroCreate<byte> bufferSize
    let mutable total = 0L
    let mutable finished = false
    while not finished && total < limit do
        let wanted = int (min (int64 buffer.Length) (limit - total))
        let! read = source.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken)
        if read = 0 then
            finished <- true
        else
            do! target.WriteAsync(ReadOnlyMemory(buffer, 0, read), cancellationToken)
            total <- total + int64 read }

/// Creates an import function with its own serialized commit section. Call it
/// once per host: streaming and validation run concurrently, but only one
/// import at a time finds duplicates, promotes and inserts. The data-directory
/// lock guarantees a single process, so no contention retries are needed.
let create (dependencies: ImportDependencies) : Import =
    let commitSection = new SemaphoreSlim(1, 1)
    // Reading one byte past the limit is enough to prove an upload is too large.
    let stagingLimit =
        if dependencies.Limits.MaxBytes < Int64.MaxValue then dependencies.Limits.MaxBytes + 1L
        else dependencies.Limits.MaxBytes

    let stage key (source: Stream) cancellationToken = task {
        let! opened = dependencies.OpenStagedWrite key cancellationToken
        use target = opened
        do! copyAtMost source target stagingLimit cancellationToken }

    let validate key cancellationToken = task {
        let! opened = dependencies.OpenStagedRead key cancellationToken
        use staged = opened
        return! dependencies.Validate staged dependencies.Limits cancellationToken }

    let matches key (candidate: LibraryDocument) = task {
        let! stagedStream = dependencies.OpenStagedRead key CancellationToken.None
        use staged = stagedStream
        let! originalStream = dependencies.OpenOriginal candidate.StorageKey CancellationToken.None
        use original = originalStream
        return! contentEquals staged original CancellationToken.None }

    let rec firstMatch key (candidates: LibraryDocument list) = task {
        match candidates with
        | [] -> return None
        | candidate :: rest ->
            let! same = matches key candidate
            if same then return Some candidate else return! firstMatch key rest }

    // Runs inside the commit section without a cancellation token, so a commit
    // is never abandoned between promotion and the row insert.
    let commit key (validated: ValidatedPdf) fileName = task {
        let! candidates = dependencies.FindBySha256 validated.Sha256 CancellationToken.None
        let sameLength = candidates |> List.filter (fun candidate -> candidate.ByteLength = validated.ByteLength)
        match! firstMatch key sameLength with
        | Some existing -> return { Document = existing; Reused = true }
        | None ->
            let! storageKey = dependencies.Promote key CancellationToken.None
            let document =
                LibraryDocument.create
                    { Id = Guid.NewGuid()
                      Sha256 = validated.Sha256
                      StorageKey = storageKey
                      OriginalFileName = sanitizeFileName fileName
                      ByteLength = validated.ByteLength
                      PageCount = validated.PageCount
                      ImportedUtc = dependencies.Now () }
            match document with
            | Error error -> return invalidOp $"The validated PDF produced an invalid document: {error}."
            | Ok document ->
                do! dependencies.Insert document CancellationToken.None
                return { Document = document; Reused = false } }

    let run key source fileName cancellationToken = task {
        do! stage key source cancellationToken
        match! validate key cancellationToken with
        | Error rejection -> return Error rejection
        | Ok validated ->
            do! commitSection.WaitAsync(cancellationToken)
            try
                let! outcome = commit key validated fileName
                return Ok outcome
            finally
                commitSection.Release() |> ignore }

    fun source fileName cancellationToken -> task {
        let key = StagingKey (Guid.NewGuid())
        let! attempt = task {
            try
                let! result = run key source fileName cancellationToken
                return Ok result
            with error ->
                return Error (ExceptionDispatchInfo.Capture error) }
        // The staged file is gone after promotion; on every other path,
        // successful reuse, rejection or failure, it is deleted here.
        do! dependencies.Discard key CancellationToken.None
        match attempt with
        | Ok result -> return result
        | Error captured ->
            captured.Throw()
            return Unchecked.defaultof<_> }
