module LexScholar.Infrastructure.LibraryRepository

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

// RED stubs (T030): no behavior yet.
let list (dataDirectory: string) : LibraryDocument list = ignore dataDirectory; []

let listForProject (dataDirectory: string) (projectId: System.Guid) : Result<(ProjectDocument * LibraryDocument) list, Library.AssociationError> =
    ignore (dataDirectory, projectId); Ok []

let associate (dataDirectory: string) (association: ProjectDocument) : Result<LibraryDocument, Library.AssociationError> =
    ignore (dataDirectory, association); Error Library.AssociationError.DocumentNotFound

let removeAssociation (dataDirectory: string) (projectId: System.Guid) (documentId: System.Guid) : Result<unit, Library.RemovalError> =
    ignore (dataDirectory, projectId, documentId); Error Library.RemovalError.AssociationNotFound
