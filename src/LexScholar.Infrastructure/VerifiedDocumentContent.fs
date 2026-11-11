/// Opens stored originals by document UUID for delivery. The original is opened
/// read-only and served only if it exists, is readable and has its recorded
/// byte length; it is not rehashed or reparsed (see research.md, "Document
/// availability on open"). Nothing here changes a record or its associations.
module LexScholar.Infrastructure.VerifiedDocumentContent

open System
open System.IO
open Microsoft.EntityFrameworkCore
open LexScholar.Application

/// Opens the original read-only and returns it only if the opened handle's
/// length equals the recorded byte length. The handle is disposed whenever it is
/// not returned, including when reading its length fails.
let private tryOpen (path: string) (byteLength: int64) : Stream option =
    let mutable stream : FileStream = null
    try
        stream <- new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.Asynchronous)
        if stream.Length = byteLength then Some (stream :> Stream)
        else
            stream.Dispose()
            None
    with :? IOException | :? UnauthorizedAccessException ->
        if not (isNull stream) then stream.Dispose()
        None

let openRead (dataDirectory: string) : OpenVerifiedContent =
    fun documentId cancellationToken -> task {
        use db = WorkspaceDb.create dataDirectory
        let! row =
            db.Set<LibraryDocumentRow>().AsNoTracking()
                .FirstOrDefaultAsync((fun row -> row.Id = documentId), cancellationToken)
        if isNull (box row) then
            return Error DocumentNotFound
        else
            // The storage key comes only from the record, never from the request.
            match tryOpen (Path.Combine(DocumentStore.originalsDirectory dataDirectory, row.StorageKey)) row.ByteLength with
            | None -> return Error DocumentUnavailable
            | Some stream ->
                return Ok { Stream = stream; ByteLength = row.ByteLength; OriginalFileName = row.OriginalFileName } }
