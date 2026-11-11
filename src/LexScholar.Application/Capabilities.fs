namespace LexScholar.Application

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open LexScholar.Domain

// Pass only the functions needed by a caller; these are values, not services
// resolved from a container. Domain rules remain for later tasks.
type ProjectOperations =
    { List: CancellationToken -> Task<ProjectList>
      Create: string -> CancellationToken -> Task<Project>
      Select: Guid -> CancellationToken -> Task<unit> }

type LibraryOperations =
    { List: CancellationToken -> Task<Document list>
      /// Import borrows the caller-owned input until its task completes. The
      /// implementation must not dispose or retain it, including on cancellation.
      Import: Stream -> string -> CancellationToken -> Task<ImportResult>
      ListForProject: Guid -> CancellationToken -> Task<ProjectDocumentView list>
      Associate: Guid -> Guid -> DocumentRole -> CancellationToken -> Task<ProjectDocumentView>
      ChangeRole: Guid -> Guid -> DocumentRole -> CancellationToken -> Task<ProjectDocumentView>
      RemoveAssociation: Guid -> Guid -> CancellationToken -> Task<unit> }

/// Validation borrows the caller-owned, seekable input until its task completes
/// and reads it from the start. The implementation must not dispose or retain
/// it, including on cancellation.
type ValidatePdf = Stream -> ImportLimits -> CancellationToken -> Task<Result<ValidatedPdf, PdfRejection>>
