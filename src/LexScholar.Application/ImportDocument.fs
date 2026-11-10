module LexScholar.Application.ImportDocument

open System
open System.IO
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

// RED stubs (T021): behavior is implemented in T029.

/// Compares two streams byte for byte from their current positions.
let contentEquals (left: Stream) (right: Stream) (cancellationToken: CancellationToken) : Task<bool> =
    Task.FromResult false

/// Display-only basename for an uploaded file; never used as a storage path.
let sanitizeFileName (fileName: string) : string = fileName

/// Creates an import function with its own serialized commit section.
let create (dependencies: ImportDependencies) : Import =
    fun _ _ _ -> Task.FromResult(Error Corrupt)
