module LexScholar.Application.Library

open System
open LexScholar.Application

let unimplemented : LibraryOperations =
    { List = fun _ -> task { return raise (NotImplementedException("Library listing is not implemented.")) }
      Import = fun _ _ _ -> task { return raise (NotImplementedException("PDF import is not implemented.")) }
      ListForProject = fun _ _ -> task { return raise (NotImplementedException("Project document listing is not implemented.")) }
      Associate = fun _ _ _ _ -> task { return raise (NotImplementedException("Document association is not implemented.")) }
      ChangeRole = fun _ _ _ _ -> task { return raise (NotImplementedException("Role changes are not implemented.")) }
      RemoveAssociation = fun _ _ _ -> task { return raise (NotImplementedException("Association removal is not implemented.")) } }

// Shared library listing and per-project associations. Explicit storage
// functions keep these workflows independent of EF, as in Projects.

[<RequireQualifiedAccess>]
type AssociationError =
    | ProjectNotFound
    | DocumentNotFound
    | DuplicateAssociation

[<RequireQualifiedAccess>]
type RemovalError = AssociationNotFound

type Store =
    { ListDocuments: unit -> LexScholar.Domain.LibraryDocument list
      /// A project's associations with their documents; ProjectNotFound for an unknown project.
      ListForProject: Guid -> Result<(LexScholar.Domain.ProjectDocument * LexScholar.Domain.LibraryDocument) list, AssociationError>
      /// Inserts the association and returns its document.
      Associate: LexScholar.Domain.ProjectDocument -> Result<LexScholar.Domain.LibraryDocument, AssociationError>
      /// Deletes only this association, never the document or its original.
      Remove: Guid -> Guid -> Result<unit, RemovalError> }

/// The content route for a document; derived from its UUID, never a storage path.
let contentUrl (documentId: Guid) = $"/api/documents/{documentId:D}/content"

/// The contract view of a library document.
let documentView (document: LexScholar.Domain.LibraryDocument) : Document =
    { Id = document.Id
      OriginalFileName = document.OriginalFileName
      ByteLength = document.ByteLength
      PageCount = document.PageCount
      ImportedUtc = document.ImportedUtc
      ContentUrl = contentUrl document.Id }

let private associationView (association: LexScholar.Domain.ProjectDocument) document : ProjectDocumentView =
    { ProjectId = association.ProjectId
      Document = documentView document
      Role = association.Role
      AddedUtc = association.AddedUtc }

/// Every shared library document, including those with no associations.
let list (store: Store) = store.ListDocuments () |> List.map documentView

let listForProject (store: Store) projectId =
    store.ListForProject projectId |> Result.map (List.map (fun (association, document) -> associationView association document))

let associate (store: Store) projectId documentId role =
    let association : LexScholar.Domain.ProjectDocument =
        { ProjectId = projectId; DocumentId = documentId; Role = role; AddedUtc = DateTimeOffset.UtcNow }
    store.Associate association |> Result.map (associationView association)

let remove (store: Store) projectId documentId = store.Remove projectId documentId
