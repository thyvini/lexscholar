namespace LexScholar.Application

open System
open System.IO
open LexScholar.Domain

// These types are scaffolding for the approved API; storage implementations
// belong to later tasks. DocumentRole is defined in LexScholar.Domain.

type Project = { Id: Guid; Name: string; CreatedUtc: DateTimeOffset }
type ProjectList = { ActiveProjectId: Guid option; Projects: Project list }
type Document =
    { Id: Guid
      OriginalFileName: string
      ByteLength: int64
      PageCount: int
      ImportedUtc: DateTimeOffset
      ContentUrl: string }
type ImportResult = Imported of Document | Reused of Document
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
// reads fail or are cancelled. Verification and delivery use the same read-only
// handle, protected from changes; no physical path or browser-specific
// representation is exposed.
type VerifiedContent = { Stream: Stream; ByteLength: int64; OriginalFileName: string }
