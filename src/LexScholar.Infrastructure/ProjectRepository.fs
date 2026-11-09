module LexScholar.Infrastructure.ProjectRepository

open System
open LexScholar.Domain

// T013 test-first signatures only. T016 will replace these no-behavior stubs.
type SelectionError = ProjectNotFound

let list (_dataDirectory: string) (_workspaceId: Guid) : ResearchProject list = []

let activeProject (_dataDirectory: string) (_workspaceId: Guid) : Guid option = None

let create (_dataDirectory: string) (workspaceId: Guid) (id: Guid) (name: string) (createdUtc: DateTimeOffset)
    : Result<ResearchProject, ProjectCreationError> =
    Ok { Id = id; WorkspaceId = workspaceId; Name = name; NormalizedName = name; CreatedUtc = createdUtc }

let select (_dataDirectory: string) (_workspaceId: Guid) (_projectId: Guid) : Result<unit, SelectionError> = Ok ()
