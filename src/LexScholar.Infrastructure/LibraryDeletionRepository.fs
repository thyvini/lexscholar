/// Snapshot impact, journal admission and internal transactional metadata removal.
module LexScholar.Infrastructure.LibraryDeletionRepository

open System
open System.Linq
open Microsoft.EntityFrameworkCore
open LexScholar.Application.LibraryDeletion
open LexScholar.Application

/// The caller owns the snapshot transaction. Exposed separately for deterministic
/// interleaving tests and later atomic confirmation under mutation coordination.
let read (db: WorkspaceDb) (ids: Guid list) : DocumentSource list =
    let selected = Set.ofList ids
    let documents = db.Set<LibraryDocumentRow>().AsNoTracking().ToList() |> Seq.filter (fun row -> selected.Contains row.Id) |> Seq.toList
    let associations = db.Set<ProjectDocumentRow>().AsNoTracking().ToList() |> Seq.filter (fun row -> selected.Contains row.DocumentId) |> Seq.toList
    let projects = db.Set<ResearchProjectRow>().AsNoTracking().ToList() |> Seq.map (fun row -> row.Id, row.Name) |> Map.ofSeq
    let summaries =
        db.Set<DocumentSummaryRow>().AsNoTracking().ToList()
        |> Seq.filter (fun row -> selected.Contains row.DocumentId)
        |> Seq.map (fun row -> (row.ProjectId, row.DocumentId), (row.Content, row.GeneratedAt))
        |> Map.ofSeq
    documents |> List.map (fun document ->
        { Id = document.Id; Name = document.OriginalFileName
          Projects =
            associations |> List.filter (fun row -> row.DocumentId = document.Id) |> List.map (fun row ->
                { Id = row.ProjectId; Name = projects[row.ProjectId]; Role = row.Role
                  Summary = summaries |> Map.tryFind (row.ProjectId, row.DocumentId) }) })

let capture directory ids =
    LibraryExportSnapshot.withSnapshot directory (fun db -> read db ids)

let private pending (db: WorkspaceDb) ids =
    let selected = Set.ofList ids
    db.Set<DeletionItemRow>().AsNoTracking()
    |> Seq.exists (fun item -> item.Status <> "deleted" && selected.Contains item.DocumentId)

let isPending directory documentId = LibraryRepository.isPending directory documentId

/// Wrap the existing summary capabilities rather than editing their repository.
/// Used under the registry's workspace mutation boundary; T011 adds job fencing.
let unlessPending directory documentId work =
    if isPending directory documentId then invalidOp "Library deletion is pending."
    work ()

let admit (coordination: LibraryCoordination) directory operation ids revision =
    coordination.Mutation(fun () ->
        use db = WorkspaceDb.create directory
        // SQLite's immediate write transaction makes compare + intent one commit.
        use transaction = db.Database.BeginTransaction()
        let store : AdmissionStore =
            { Existing = fun id ->
                if db.Set<DeletionOperationRow>().AsNoTracking().Any(fun row -> row.Id = id) then
                    Some (db.Set<DeletionItemRow>().AsNoTracking() |> Seq.filter (fun row -> row.OperationId = id) |> Seq.map _.DocumentId |> Seq.toList)
                else None
              Read = read db
              Pending = pending db
              Persist = fun id selected ->
                  db.Add(DeletionOperationRow(Id = id, CreatedUtc = DateTimeOffset.UtcNow)) |> ignore
                  for documentId in selected do
                      let document = db.Set<LibraryDocumentRow>().Find(documentId)
                      db.Add(DeletionItemRow(OperationId = id, DocumentId = documentId,
                                             OriginalFileName = document.OriginalFileName, StorageKey = document.StorageKey,
                                             Status = "pending", Phase = "pending")) |> ignore
                  db.SaveChanges() |> ignore }
        let result = LibraryDeletion.admit store operation ids revision
        transaction.Commit()
        result)

/// Fault boundaries for deterministic transaction/uncertain-commit tests.
[<RequireQualifiedAccess>]
type MetadataStep =
    | AssociationsRemoved
    | BeforeCommit
    | AfterCommit

let hasItem directory operation document =
    use db = WorkspaceDb.create directory
    db.Set<DeletionItemRow>().Any(fun row -> row.OperationId = operation && row.DocumentId = document)

let private metadataAbsent (db: WorkspaceDb) document =
    not (db.Set<LibraryDocumentRow>().Any(fun row -> row.Id = document))
    && not (db.Set<ProjectDocumentRow>().Any(fun row -> row.DocumentId = document))
    && not (db.Set<DocumentSummaryRow>().Any(fun row -> row.DocumentId = document))

/// Internal only: never removes files or marks an item deleted. The caller uses
/// LibraryDeletion.removeMetadata to fence jobs before entering this transaction.
let removeMetadataWith (checkpoint: MetadataStep -> unit) (coordination: LibraryCoordination) directory operation document : Result<string, MetadataError> =
    coordination.Mutation(fun () ->
        let attempt () =
            use db = WorkspaceDb.create directory
            use transaction = db.Database.BeginTransaction()
            let item = db.Set<DeletionItemRow>().Find(operation, document)
            if isNull (box item) then Error MetadataError.ItemNotFound
            elif item.Phase = "metadata_removed" || item.Status = "deleted" then
                if metadataAbsent db document then Ok item.StorageKey
                else invalidOp "Inconsistent deletion metadata."
            else
                let row = db.Set<LibraryDocumentRow>().Find(document)
                if isNull (box row) || row.StorageKey <> item.StorageKey then
                    invalidOp "Inconsistent deletion intent."
                LibraryRepository.removeMetadata db document (fun () -> checkpoint MetadataStep.AssociationsRemoved)
                item.Phase <- "metadata_removed"
                item.Status <- "metadata_removed"
                item.Reason <- null
                db.SaveChanges() |> ignore
                checkpoint MetadataStep.BeforeCommit
                transaction.Commit()
                checkpoint MetadataStep.AfterCommit
                Ok item.StorageKey
        try attempt ()
        with _ ->
            // The transaction/context above have been disposed. Never infer
            // rollback from an exception: commit acknowledgement may be lost.
            try
                use db = WorkspaceDb.create directory
                use transaction = db.Database.BeginTransaction()
                let item = db.Set<DeletionItemRow>().Find(operation, document)
                let result =
                    if isNull (box item) then Error MetadataError.ItemNotFound
                    elif item.Phase = "metadata_removed" && metadataAbsent db document then Ok item.StorageKey
                    else
                        if item.Status <> "deleted" then
                            item.Status <- "incomplete"
                            item.Reason <- "storage_error"
                            db.SaveChanges() |> ignore
                        Error MetadataError.StorageError
                transaction.Commit()
                result
            with _ ->
                // An inaccessible/full database may also prevent the failure
                // marker. The durable intent is still retained for recovery.
                Error MetadataError.StorageError)

let removeMetadata coordination directory operation document =
    removeMetadataWith ignore coordination directory operation document

[<RequireQualifiedAccess>]
type CompletionStep = BeforeCommit | AfterCommit

/// Internal only. Caller must own the host file lease for the whole lifecycle.
/// No file work occurs under the summary registry lock. The completion marker is
/// committed only after metadata absence, unlink (or proven ENOENT), and fsync.
let finishOriginalWith (checkpoint: CompletionStep -> unit) (remove: string -> unit) (coordination: LibraryCoordination) directory operation document : Result<unit, MetadataError> =
    coordination.Mutation(fun () ->
        let attempt () =
            use db = WorkspaceDb.create directory
            use transaction = db.Database.BeginTransaction()
            let item = db.Set<DeletionItemRow>().Find(operation, document)
            if isNull (box item) then Error MetadataError.ItemNotFound
            // The durable completion already proves the required unlink/sync.
            // A retry has no authority to touch a subsequently reused name.
            elif item.Status = "deleted" then Ok ()
            elif item.Phase <> "metadata_removed" || not (metadataAbsent db document) then
                Error MetadataError.StorageError
            elif db.Set<LibraryDocumentRow>().Any(fun row -> row.StorageKey = item.StorageKey) then
                Error MetadataError.StorageError
            else
                remove item.StorageKey
                item.Status <- "deleted"
                item.Reason <- null
                db.SaveChanges() |> ignore
                checkpoint CompletionStep.BeforeCommit
                transaction.Commit()
                checkpoint CompletionStep.AfterCommit
                Ok ()
        try attempt ()
        with _ ->
            try
                use db = WorkspaceDb.create directory
                use transaction = db.Database.BeginTransaction()
                let item = db.Set<DeletionItemRow>().Find(operation, document)
                let result =
                    if isNull (box item) then Error MetadataError.ItemNotFound
                    // This fresh transaction also protects against regressing a
                    // completion committed by another processor after our error.
                    elif item.Status = "deleted" then Ok ()
                    else
                        item.Status <- "incomplete"
                        item.Reason <- "storage_error"
                        db.SaveChanges() |> ignore
                        Error MetadataError.StorageError
                transaction.Commit()
                result
            with _ -> Error MetadataError.StorageError)

let finishOriginal coordination directory operation document =
    finishOriginalWith ignore (DocumentStore.removeOriginal directory) coordination directory operation document

/// One SQLite snapshot for operation/item projection; storage keys and internal
/// phases never cross this boundary. A read failure is an error, never an empty list.
let results directory =
    LibraryExportSnapshot.withSnapshot directory (fun db ->
        let items = db.Set<DeletionItemRow>().AsNoTracking().ToList() |> Seq.toList
        db.Set<DeletionOperationRow>().AsNoTracking().ToList()
        |> Seq.sortBy (fun row -> row.CreatedUtc, row.Id)
        |> Seq.map (fun operation ->
            let outcomes =
                items |> List.filter (fun item -> item.OperationId = operation.Id)
                |> List.sortBy _.DocumentId
                |> List.map (fun item ->
                    { DocumentId = item.DocumentId; Name = item.OriginalFileName
                      Outcome = if item.Status = "deleted" then "deleted" else "not_completed"
                      Reason = if item.Status = "deleted" then null
                               elif item.Reason = "storage_error" then "storage_error" else "pending" })
            { OperationId = operation.Id
              Status = if not outcomes.IsEmpty && outcomes |> List.forall (fun item -> item.Outcome = "deleted") then "complete" else "partial"
              Items = outcomes })
        |> Seq.toList)

let acknowledge directory (operation: Guid) =
    use db = WorkspaceDb.create directory
    use transaction = db.Database.BeginTransaction()
    let row = db.Set<DeletionOperationRow>().Find(operation)
    if isNull (box row) then Error "operation_not_found"
    else
        let items = db.Set<DeletionItemRow>().Where(fun item -> item.OperationId = operation).ToList()
        if items.Count = 0 || items |> Seq.exists (fun item -> item.Status <> "deleted") then
            Error "deletion_incomplete"
        else
            for item in items do db.Remove(item) |> ignore
            db.Remove(row) |> ignore
            db.SaveChanges() |> ignore
            transaction.Commit()
            Ok ()
