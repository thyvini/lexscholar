module LexScholar.Infrastructure.ProjectRepository

open System
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open LexScholar.Domain

type SelectionError = ProjectNotFound

let private domain (row: ResearchProjectRow) : ResearchProject =
    { Id = row.Id; WorkspaceId = row.WorkspaceId; Name = row.Name
      NormalizedName = row.NormalizedName; CreatedUtc = row.CreatedUtc }

// Resolve on first project request, not host startup. An immediate SQLite
// transaction serializes concurrent first requests, including across processes.
let resolveWorkspace directory =
    use db = WorkspaceDb.create directory
    db.Database.OpenConnection()
    let connection = db.Database.GetDbConnection() :?> SqliteConnection
    use transaction = connection.BeginTransaction(deferred = false)
    db.Database.UseTransaction(transaction) |> ignore
    let rows = db.Set<WorkspaceRow>() |> Seq.truncate 2 |> Seq.toArray
    let id =
        match rows with
        | [| row |] -> row.Id
        | [||] ->
            let row = WorkspaceRow(Id = Guid.NewGuid(), CreatedUtc = DateTimeOffset.UtcNow)
            db.Add(row) |> ignore
            db.SaveChanges() |> ignore
            row.Id
        | _ -> invalidOp "Multiple workspaces exist in a single-workspace database."
    transaction.Commit()
    id

let list directory workspaceId : ResearchProject list =
    use db = WorkspaceDb.create directory
    query { for row in db.Set<ResearchProjectRow>().AsNoTracking() do
            where (row.WorkspaceId = workspaceId)
            select row } |> Seq.map domain |> Seq.toList

let activeProject directory (workspaceId: Guid) =
    use db = WorkspaceDb.create directory
    let row = db.Set<WorkspaceRow>().Find(workspaceId)
    if obj.ReferenceEquals(row, null) || not row.ActiveProjectId.HasValue then None else Some row.ActiveProjectId.Value

let create directory workspaceId id name createdUtc : Result<ResearchProject, ProjectCreationError> =
    match ResearchProject.create id workspaceId name createdUtc [] with
    | Error error -> Error error
    | Ok project ->
        use db = WorkspaceDb.create directory
        db.Add(ResearchProjectRow(Id = project.Id, WorkspaceId = project.WorkspaceId, Name = project.Name,
                                  NormalizedName = project.NormalizedName, CreatedUtc = project.CreatedUtc)) |> ignore
        try
            db.SaveChanges() |> ignore
            Ok { project with CreatedUtc = project.CreatedUtc.ToUniversalTime() }
        with
        | :? DbUpdateException as error when
            (match error.InnerException with :? SqliteException as sqlite -> sqlite.SqliteExtendedErrorCode = 2067 | _ -> false) ->
            // Only translate a conflict on the normalized name; other unique
            // failures (such as a repeated project UUID) remain errors.
            use check = WorkspaceDb.create directory
            let exists = query { for row in check.Set<ResearchProjectRow>().AsNoTracking() do
                                 where (row.WorkspaceId = workspaceId && row.NormalizedName = project.NormalizedName)
                                 select row } |> Seq.isEmpty |> not
            if exists then Error DuplicateProjectName else reraise()

let select directory (workspaceId: Guid) (projectId: Guid) : Result<unit, SelectionError> =
    use db = WorkspaceDb.create directory
    let workspace = db.Set<WorkspaceRow>().Find(workspaceId)
    let project = db.Set<ResearchProjectRow>().Find(projectId)
    if obj.ReferenceEquals(workspace, null) || obj.ReferenceEquals(project, null) || project.WorkspaceId <> workspaceId then Error ProjectNotFound
    else
        workspace.ActiveProjectId <- Nullable(projectId)
        db.SaveChanges() |> ignore
        Ok ()
