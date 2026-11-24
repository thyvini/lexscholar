/// Startup-only deletion recovery. Caller holds the data-directory lock, after
/// migrations and before cleanup/requests; there are no live summary jobs.
module LexScholar.Infrastructure.LibraryDeletionRecovery

open System
open LexScholar.Application.LibraryDeletion
open Microsoft.EntityFrameworkCore

/// Explicit DI-only seam, honored only in the Testing environment until T017.
/// It cannot be enabled through configuration or an HTTP request.
type StartupForTests = delegate of string -> unit

/// Snapshot identities only, not phases: each repository call opens fresh
/// transactions and resolves commit uncertainty from the durable journal.
/// A failed item retains its phase/reason and must not prevent unrelated work.
/// Repository capabilities sanitize failures and persist incomplete when possible.
let runWith (remove: Guid -> Guid -> Result<string, MetadataError>)
            (finish: Guid -> Guid -> Result<unit, MetadataError>) (directory: string) =
    let items =
        use db = WorkspaceDb.create directory
        db.Set<DeletionItemRow>().AsNoTracking()
        |> Seq.filter (fun item -> item.Status <> "deleted")
        |> Seq.map (fun item -> item.OperationId, item.DocumentId)
        |> Seq.toList
    for operation, document in items do
        match remove operation document with
        | Ok _ -> finish operation document |> ignore
        | Error _ -> ()

let runForTests coordination directory =
    runWith (LibraryDeletionRepository.removeMetadata coordination directory)
        (LibraryDeletionRepository.finishOriginal coordination directory) directory
