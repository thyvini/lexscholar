module LexScholar.Infrastructure.LibraryRepository

open System
open System.Linq
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open LexScholar.Application
open LexScholar.Domain

let private domain (row: LibraryDocumentRow) : LibraryDocument =
    { Id = row.Id
      Sha256 = row.Sha256
      StorageKey = row.StorageKey
      OriginalFileName = row.OriginalFileName
      ByteLength = row.ByteLength
      PageCount = row.PageCount
      ImportedUtc = row.ImportedUtc }

let findBySha256 (dataDirectory: string) : FindDocumentsBySha256 =
    fun sha256 cancellationToken -> task {
        use db = WorkspaceDb.create dataDirectory
        let candidates = query { for row in db.Set<LibraryDocumentRow>().AsNoTracking() do
                                 where (row.Sha256 = sha256)
                                 select row }
        let! rows = candidates.ToListAsync(cancellationToken)
        return rows |> Seq.map domain |> Seq.toList }

let insert (dataDirectory: string) : InsertDocument =
    fun document cancellationToken -> task {
        use db = WorkspaceDb.create dataDirectory
        db.Add(LibraryDocumentRow(Id = document.Id, Sha256 = document.Sha256, StorageKey = document.StorageKey,
                                  OriginalFileName = document.OriginalFileName, ByteLength = document.ByteLength,
                                  PageCount = document.PageCount, ImportedUtc = document.ImportedUtc)) |> ignore
        let! _ = db.SaveChangesAsync(cancellationToken)
        return () }

/// Every storage key that a document row references.
let storageKeys (dataDirectory: string) : Set<string> =
    use db = WorkspaceDb.create dataDirectory
    query { for row in db.Set<LibraryDocumentRow>().AsNoTracking() do select row.StorageKey } |> Set.ofSeq

let private roleText role =
    match role with
    | FocusedSource -> "FocusedSource"
    | ReferenceSource -> "ReferenceSource"

let private role (text: string) =
    match text with
    | "FocusedSource" -> FocusedSource
    | "ReferenceSource" -> ReferenceSource
    | other -> invalidOp $"Unknown document role '{other}'."

let private association (row: ProjectDocumentRow) : ProjectDocument =
    { ProjectId = row.ProjectId; DocumentId = row.DocumentId; Role = role row.Role; AddedUtc = row.AddedUtc }

let private exists (row: 'row) = not (obj.ReferenceEquals(row, null))

/// Every library document, oldest import first, with or without associations.
let list (dataDirectory: string) : LibraryDocument list =
    use db = WorkspaceDb.create dataDirectory
    db.Set<LibraryDocumentRow>().AsNoTracking()
    |> Seq.map domain
    |> Seq.sortBy (fun document -> document.ImportedUtc, document.Id)
    |> Seq.toList

/// A project's associations with their documents, oldest association first.
let listForProject (dataDirectory: string) (projectId: Guid) : Result<(ProjectDocument * LibraryDocument) list, Library.AssociationError> =
    use db = WorkspaceDb.create dataDirectory
    if not (exists (db.Set<ResearchProjectRow>().Find(projectId))) then
        Error Library.AssociationError.ProjectNotFound
    else
        let associations =
            query { for row in db.Set<ProjectDocumentRow>().AsNoTracking() do
                    where (row.ProjectId = projectId)
                    select row } |> Seq.map association |> Seq.toList
        let ids = associations |> List.map (fun item -> item.DocumentId) |> List.toArray
        let documents =
            query { for row in db.Set<LibraryDocumentRow>().AsNoTracking() do
                    where (ids.Contains(row.Id))
                    select row } |> Seq.map (fun row -> row.Id, domain row) |> Map.ofSeq
        associations
        |> List.map (fun item -> item, documents[item.DocumentId])
        |> List.sortBy (fun (item, _) -> item.AddedUtc, item.DocumentId)
        |> Ok

/// Inserts the association and returns its document. A repeated
/// (project, document) pair is reported as a duplicate, never replaced.
let associate (dataDirectory: string) (item: ProjectDocument) : Result<LibraryDocument, Library.AssociationError> =
    use db = WorkspaceDb.create dataDirectory
    let document = db.Set<LibraryDocumentRow>().Find(item.DocumentId)
    if not (exists (db.Set<ResearchProjectRow>().Find(item.ProjectId))) then Error Library.AssociationError.ProjectNotFound
    elif not (exists document) then Error Library.AssociationError.DocumentNotFound
    else
        db.Add(ProjectDocumentRow(ProjectId = item.ProjectId, DocumentId = item.DocumentId,
                                  Role = roleText item.Role, AddedUtc = item.AddedUtc)) |> ignore
        try
            db.SaveChanges() |> ignore
            Ok (domain document)
        with
        // SQLITE_CONSTRAINT_PRIMARYKEY: the association already exists.
        | :? DbUpdateException as error when
            (match error.InnerException with :? SqliteException as sqlite -> sqlite.SqliteExtendedErrorCode = 1555 | _ -> false) ->
            Error Library.AssociationError.DuplicateAssociation

/// Deletes only this association; the document row and its original remain.
let removeAssociation (dataDirectory: string) (projectId: Guid) (documentId: Guid) : Result<unit, Library.RemovalError> =
    use db = WorkspaceDb.create dataDirectory
    let deleted =
        query { for row in db.Set<ProjectDocumentRow>() do
                where (row.ProjectId = projectId && row.DocumentId = documentId)
                select row }
        |> fun rows -> rows.ExecuteDelete()
    if deleted = 0 then Error Library.RemovalError.AssociationNotFound else Ok ()
