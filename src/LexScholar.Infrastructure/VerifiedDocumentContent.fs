/// Opens stored originals by document UUID for delivery. The original is opened
/// read-only and served only if it exists, is readable and has its recorded
/// byte length; it is not rehashed or reparsed (see research.md, "Document
/// availability on open"). Nothing here changes a record or its associations.
module LexScholar.Infrastructure.VerifiedDocumentContent

open System
open System.IO
open Microsoft.EntityFrameworkCore
open LexScholar.Application

let private tryOpen (path: string) : Stream option =
    try Some (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.Asynchronous) :> Stream)
    with :? IOException | :? UnauthorizedAccessException -> None

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
            match tryOpen (Path.Combine(DocumentStore.originalsDirectory dataDirectory, row.StorageKey)) with
            | None -> return Error DocumentUnavailable
            | Some stream when stream.Length <> row.ByteLength ->
                stream.Dispose()
                return Error DocumentUnavailable
            | Some stream ->
                return Ok { Stream = stream; ByteLength = row.ByteLength; OriginalFileName = row.OriginalFileName } }
