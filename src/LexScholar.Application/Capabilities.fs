namespace LexScholar.Application

open System
open System.IO
open System.Threading
open System.Threading.Tasks

// Pass only the functions needed by a caller; these are values, not services
// resolved from a container. Domain rules remain for later tasks.
type ProjectOperations =
    { List: CancellationToken -> Task<ProjectList>
      Create: string -> CancellationToken -> Task<Project>
      Select: Guid -> CancellationToken -> Task<unit> }

type LibraryOperations =
    { List: CancellationToken -> Task<Document list>
      Import: Stream -> string -> CancellationToken -> Task<ImportResult>
      ListForProject: Guid -> CancellationToken -> Task<ProjectDocument list>
      Associate: Guid -> Guid -> DocumentRole -> CancellationToken -> Task<ProjectDocument>
      ChangeRole: Guid -> Guid -> DocumentRole -> CancellationToken -> Task<ProjectDocument>
      RemoveAssociation: Guid -> Guid -> CancellationToken -> Task<unit> }

type ValidatePdf = Stream -> ImportLimits -> CancellationToken -> Task<ValidatedPdf>

type ImportJournalOperations =
    { Create: PendingImport -> CancellationToken -> Task<unit>
      Update: PendingImport -> CancellationToken -> Task<unit>
      Remove: Guid -> CancellationToken -> Task<unit>
      Recover: CancellationToken -> Task<unit> }

type OpenVerifiedContent = Guid -> CancellationToken -> Task<VerifiedContent>
