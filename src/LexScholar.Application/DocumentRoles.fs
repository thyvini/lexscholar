/// Project-specific document roles (FR-009): a role belongs to one project's
/// association, so changing it never affects another project.
module LexScholar.Application.DocumentRoles

open System
open LexScholar.Domain

[<RequireQualifiedAccess>]
type RoleChangeError =
    /// The project is unknown or the document is not associated with it.
    | AssociationNotFound

/// Sets the role of only the (project, document) association and returns it
/// with its document; AssociationNotFound when no such association exists.
type UpdateRole = Guid -> Guid -> DocumentRole -> Result<ProjectDocument * LibraryDocument, RoleChangeError>

let changeRole (update: UpdateRole) projectId documentId role : Result<ProjectDocumentView, RoleChangeError> =
    update projectId documentId role
    |> Result.map (fun (association, document) -> Library.associationView association document)
