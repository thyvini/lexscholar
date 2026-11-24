/// Snapshot impact and atomic journal admission. No destructive processing.
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
