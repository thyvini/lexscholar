/// Read-only deletion impact repository. No deletion admission or processing.
module LexScholar.Infrastructure.LibraryDeletionRepository

open System
open System.Linq
open Microsoft.EntityFrameworkCore
open LexScholar.Application.LibraryDeletion

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
