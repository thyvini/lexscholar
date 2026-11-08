namespace LexScholar.Application

open System
open System.IO

// These types are scaffolding for the approved API; domain entities and storage
// implementations belong to later tasks.
type DocumentRole =
    | FocusedSource
    | ReferenceSource

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
type ProjectDocument =
    { ProjectId: Guid
      Document: Document
      Role: DocumentRole
      AddedUtc: DateTimeOffset }

type ImportLimits = { MaxBytes: int64; MaxPages: int }
type ValidatedPdf = { Sha256: string; ByteLength: int64; PageCount: int }
type PendingImportStatus = Uploading | Validated | Promoted
type PendingImport =
    { Id: Guid
      Status: PendingImportStatus
      StagingKey: string
      DestinationKey: string
      Sha256: string option
      ByteLength: int64 option
      OriginalFileName: string
      PageCount: int option
      CreatedUtc: DateTimeOffset }

// The stream is owned by the caller; an implementation must bind verification
// and delivery to the same protected handle.
type VerifiedContent = { Stream: Stream; ByteLength: int64; OriginalFileName: string }
