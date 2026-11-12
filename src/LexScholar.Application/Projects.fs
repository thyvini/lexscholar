module LexScholar.Application.Projects

open System
open LexScholar.Application

// Explicit storage functions keep application workflows independent of EF.
type SelectionError = ProjectNotFound

type Store =
    { ResolveWorkspace: unit -> Guid
      List: Guid -> LexScholar.Domain.ResearchProject list
      ActiveProject: Guid -> Guid option
      Create: Guid -> Guid -> string -> DateTimeOffset -> Result<LexScholar.Domain.ResearchProject, LexScholar.Domain.ProjectCreationError>
      Select: Guid -> Guid -> Result<unit, SelectionError> }

let list (store: Store) =
    let workspace = store.ResolveWorkspace()
    store.ActiveProject workspace, store.List workspace

let create (store: Store) name =
    // Validate before resolving a workspace, so invalid requests cause no writes.
    match LexScholar.Domain.ProjectName.create name with
    | Error error -> Error (LexScholar.Domain.InvalidName error)
    | Ok _ -> store.Create (store.ResolveWorkspace()) (Guid.NewGuid()) name DateTimeOffset.UtcNow

let select (store: Store) id = store.Select (store.ResolveWorkspace()) id
