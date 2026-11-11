module LexScholar.Infrastructure.Tests.ProjectPersistenceTests

open System
open System.IO
open Microsoft.EntityFrameworkCore
open LexScholar.Domain
open LexScholar.Infrastructure
open Xunit

type private WorkspaceDirectory() =
    let path = Path.Combine(Path.GetTempPath(), "lexscholar-project-tests", Guid.NewGuid().ToString("N"))
    do Directory.CreateDirectory(path) |> ignore
    do WorkspaceDb.initialize path
    member _.Path = path
    interface IDisposable with member _.Dispose() = Directory.Delete(path, true)

let private now = DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero)
let private assertResult<'a when 'a: equality> (expected: 'a) (actual: 'a) =
    Assert.True((expected = actual), sprintf "Expected: %A\nActual: %A" expected actual)

let private seedWorkspace directory id =
    use db = WorkspaceDb.create directory
    db.Add(WorkspaceRow(Id = id, CreatedUtc = now)) |> ignore
    db.SaveChanges() |> ignore

let private seedProject directory workspace id name =
    use db = WorkspaceDb.create directory
    db.Add(ResearchProjectRow(Id = id, WorkspaceId = workspace, Name = name, NormalizedName = name.ToUpperInvariant(), CreatedUtc = now)) |> ignore
    db.SaveChanges() |> ignore

let private stored directory workspace =
    use db = WorkspaceDb.create directory
    db.Set<ResearchProjectRow>().AsNoTracking()
    |> Seq.filter (fun row -> row.WorkspaceId = workspace)
    |> Seq.map (fun row -> row.Id, row.Name, row.NormalizedName)
    |> Seq.sortBy (fun (id, _, _) -> id)
    |> Seq.toList

[<Fact; Trait("Task", "T013")>]
let ``creating a project persists it and listing returns it`` () =
    use directory = new WorkspaceDirectory()
    let workspace, id = Guid.NewGuid(), Guid.NewGuid()
    seedWorkspace directory.Path workspace
    let created = ProjectRepository.create directory.Path workspace id " Family Arbitration " now
    Assert.True(Result.isOk created)
    let saved = stored directory.Path workspace
    assertResult [ (id, "Family Arbitration", "FAMILY ARBITRATION") ] saved
    let listed = ProjectRepository.list directory.Path workspace |> List.map (fun project -> project.Id, project.Name)
    assertResult [ (id, "Family Arbitration") ] listed

[<Fact; Trait("Task", "T013")>]
let ``listing reads existing projects from the requested workspace`` () =
    use directory = new WorkspaceDirectory()
    let first, second = Guid.NewGuid(), Guid.NewGuid()
    let idA, idB = Guid.NewGuid(), Guid.NewGuid()
    seedWorkspace directory.Path first
    seedWorkspace directory.Path second
    seedProject directory.Path first idA "First"
    seedProject directory.Path second idB "Second"
    let actual = ProjectRepository.list directory.Path first |> List.map (fun project -> project.Id, project.Name)
    assertResult [ (idA, "First") ] actual

[<Fact; Trait("Task", "T013")>]
let ``invalid project names are rejected without changing the database`` () =
    use directory = new WorkspaceDirectory()
    let workspace = Guid.NewGuid()
    seedWorkspace directory.Path workspace
    let result = ProjectRepository.create directory.Path workspace (Guid.NewGuid()) "  " now
    Assert.Empty(stored directory.Path workspace)
    assertResult (Error (InvalidName InvalidProjectName)) result

[<Fact; Trait("Task", "T013")>]
let ``normalized duplicates are rejected without changing existing projects`` () =
    use directory = new WorkspaceDirectory()
    let workspace, id = Guid.NewGuid(), Guid.NewGuid()
    seedWorkspace directory.Path workspace
    seedProject directory.Path workspace id "Family Arbitration"
    let before = stored directory.Path workspace
    let result = ProjectRepository.create directory.Path workspace (Guid.NewGuid()) " family arbitration " now
    assertResult before (stored directory.Path workspace)
    assertResult (Error DuplicateProjectName) result

[<Fact; Trait("Task", "T013")>]
let ``identical names can be created in independent workspaces`` () =
    use directory = new WorkspaceDirectory()
    let first, second, secondId = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    seedWorkspace directory.Path first
    seedWorkspace directory.Path second
    seedProject directory.Path first (Guid.NewGuid()) "Family Arbitration"
    let result = ProjectRepository.create directory.Path second secondId " family arbitration " now
    Assert.True(Result.isOk result)
    assertResult [ (secondId, "family arbitration", "FAMILY ARBITRATION") ] (stored directory.Path second)

[<Fact; Trait("Task", "T013")>]
let ``selecting switching and reopening preserves the active project`` () =
    use directory = new WorkspaceDirectory()
    let workspace, first, second = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    seedWorkspace directory.Path workspace
    seedProject directory.Path workspace first "First"
    seedProject directory.Path workspace second "Second"
    assertResult (Ok ()) (ProjectRepository.select directory.Path workspace first)
    assertResult (Some first) (ProjectRepository.activeProject directory.Path workspace)
    assertResult (Ok ()) (ProjectRepository.select directory.Path workspace second)
    WorkspaceDb.initialize directory.Path
    assertResult (Some second) (ProjectRepository.activeProject directory.Path workspace)

[<Fact; Trait("Task", "T013")>]
let ``selection rejects unknown and cross-workspace projects`` () =
    use directory = new WorkspaceDirectory()
    let first, second, project = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    seedWorkspace directory.Path first
    seedWorkspace directory.Path second
    seedProject directory.Path first project "First"
    assertResult (Error ProjectRepository.ProjectNotFound) (ProjectRepository.select directory.Path second project)
    assertResult (Error ProjectRepository.ProjectNotFound) (ProjectRepository.select directory.Path second (Guid.NewGuid()))

[<Fact; Trait("Task", "T016")>]
let ``concurrent workspace resolution creates one stable workspace`` () =
    use directory = new WorkspaceDirectory()
    let tasks = [| for _ in 1..12 -> System.Threading.Tasks.Task.Run(fun () -> ProjectRepository.resolveWorkspace directory.Path) |]
    System.Threading.Tasks.Task.WaitAll(tasks |> Array.map (fun task -> task :> System.Threading.Tasks.Task))
    let ids = tasks |> Array.map (fun task -> task.Result) |> Array.distinct
    Assert.Single(ids) |> ignore
    WorkspaceDb.initialize directory.Path
    Assert.Equal(ids.[0], ProjectRepository.resolveWorkspace directory.Path)
    use db = WorkspaceDb.create directory.Path
    Assert.Equal(1, db.Set<WorkspaceRow>() |> Seq.length)

[<Fact; Trait("Task", "T016")>]
let ``concurrent duplicate creation commits exactly one project`` () =
    use directory = new WorkspaceDirectory()
    let workspace = ProjectRepository.resolveWorkspace directory.Path
    let tasks = [| for _ in 1..8 -> System.Threading.Tasks.Task.Run(fun () -> ProjectRepository.create directory.Path workspace (Guid.NewGuid()) " Same Name " now) |]
    System.Threading.Tasks.Task.WaitAll(tasks |> Array.map (fun task -> task :> System.Threading.Tasks.Task))
    let results = tasks |> Array.map (fun task -> task.Result)
    Assert.Equal(1, results |> Array.filter Result.isOk |> Array.length)
    Assert.Equal(7, results |> Array.filter ((=) (Error DuplicateProjectName)) |> Array.length)
    Assert.Single(stored directory.Path workspace) |> ignore
