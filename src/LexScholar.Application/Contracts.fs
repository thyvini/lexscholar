namespace LexScholar.Application

open System
open System.IO
open LexScholar.Domain

// Views and boundary types shared by the application workflows and the host.
// DocumentRole is defined in LexScholar.Domain.

type Document =
    { Id: Guid
      OriginalFileName: string
      ByteLength: int64
      PageCount: int
      ImportedUtc: DateTimeOffset
      ContentUrl: string }
/// Serialized as the `ProjectDocument` schema in contracts/http-api.yaml.
type ProjectDocumentView =
    { ProjectId: Guid
      Document: Document
      Role: DocumentRole
      AddedUtc: DateTimeOffset }

type ImportLimits = { MaxBytes: int64; MaxPages: int }
type ValidatedPdf = { Sha256: string; ByteLength: int64; PageCount: int }
/// Why an upload is not an acceptable PDF under the limits current at import.
type PdfRejection =
    | NotPdf
    | Corrupt
    | Encrypted
    | TooLarge of maxBytes: int64
    | TooManyPages of maxPages: int
/// Identifies one import's private staging file. The workflow generates it;
/// the document store derives the file name from the UUID alone, never from
/// client input.
type StagingKey = StagingKey of Guid

// On a successful open, the caller owns and must dispose Stream, even if later
// reads fail or are cancelled. Stream is a read-only handle on the stored
// original, which existed, was readable and had its recorded byte length when
// opened; it is not rehashed, so same-length changes made outside LexScholar are
// not detected (see research.md, "Document availability on open"). No physical
// path or browser-specific representation is exposed.
type VerifiedContent = { Stream: Stream; ByteLength: int64; OriginalFileName: string }
/// Why document content cannot be opened: no record has the UUID, or the
/// record's original is missing, unreadable or of the wrong length. Neither
/// case changes the record or its associations.
type ContentFailure =
    | DocumentNotFound
    | DocumentUnavailable
