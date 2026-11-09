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
    let create (name: string) : Result<ProjectName, ProjectNameError> =
        if String.IsNullOrWhiteSpace(name) then
            Error InvalidProjectName
        else
            let display = name.Trim()
            Ok { Display = display; Normalized = display.ToUpperInvariant() }

module ResearchProject =
    // This pure snapshot check does not replace atomic storage uniqueness.
    let create (id: Guid) (workspaceId: Guid) (name: string) (createdUtc: DateTimeOffset)
               (existing: ResearchProject list) : Result<ResearchProject, ProjectCreationError> =
        ProjectName.create name
        |> Result.mapError InvalidName
        |> Result.bind (fun projectName ->
            if existing |> List.exists (fun project -> project.WorkspaceId = workspaceId && project.NormalizedName = projectName.Normalized) then
                Error DuplicateProjectName
            else
                Ok { Id = id
                     WorkspaceId = workspaceId
                     Name = projectName.Display
                     NormalizedName = projectName.Normalized
                     CreatedUtc = createdUtc })
