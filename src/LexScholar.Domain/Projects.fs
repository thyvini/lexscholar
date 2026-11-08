namespace LexScholar.Domain

open System

type Workspace =
    { Id: Guid
      ActiveProjectId: Guid option
      CreatedUtc: DateTimeOffset }

type ResearchProject =
    { Id: Guid
      WorkspaceId: Guid
      Name: string
      NormalizedName: string
      CreatedUtc: DateTimeOffset }

type ProjectName =
    { Display: string
      Normalized: string }

type ProjectNameError = InvalidProjectName

type ProjectCreationError =
    | InvalidName of ProjectNameError
    | DuplicateProjectName

module ProjectName =
    // T014 scaffold only: validation, trimming, and normalization are not yet
    // implemented. Returning the input lets behavior tests fail by assertion.
    let create (name: string) : Result<ProjectName, ProjectNameError> =
        Ok { Display = name; Normalized = name }

module ResearchProject =
    // T014 scaffold only: no duplicate detection is performed. This is a pure
    // decision over the supplied snapshot; persistence must separately enforce
    // uniqueness atomically when project storage is implemented.
    let create (id: Guid) (workspaceId: Guid) (name: string) (createdUtc: DateTimeOffset)
               (_existing: ResearchProject list) : Result<ResearchProject, ProjectCreationError> =
        ProjectName.create name
        |> Result.mapError InvalidName
        |> Result.map (fun projectName ->
            { Id = id
              WorkspaceId = workspaceId
              Name = projectName.Display
              NormalizedName = projectName.Normalized
              CreatedUtc = createdUtc })
