module LexScholar.Domain.Tests.ProjectTests

open System
open System.Globalization
open LexScholar.Domain
open Xunit

let private createdUtc = DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero)

let private normalized name =
    ProjectName.create name |> Result.map (fun value -> value.Normalized)

let private assertResult<'a when 'a: equality> (expected: 'a) (actual: 'a) =
    Assert.True((expected = actual), sprintf "Expected: %A\nActual: %A" expected actual)

[<Fact; Trait("Task", "T012")>]
let ``a valid project name is accepted`` () =
    let actual = ProjectName.create "Family Arbitration"
    assertResult (Ok "Family Arbitration") (actual |> Result.map (fun name -> name.Display))

[<Fact; Trait("Task", "T012")>]
let ``surrounding whitespace is removed from the display name`` () =
    let actual = ProjectName.create " \tFamily Arbitration \n"
    assertResult (Ok "Family Arbitration") (actual |> Result.map (fun name -> name.Display))

[<Theory; Trait("Task", "T012")>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("\t\r\n")>]
[<InlineData(null)>]
let ``empty whitespace-only or missing names are rejected`` (name: string) =
    assertResult (Error InvalidProjectName) (ProjectName.create name)

[<Fact; Trait("Task", "T012")>]
let ``project creation rejects an invalid name`` () =
    let actual = ResearchProject.create (Guid.NewGuid()) (Guid.NewGuid()) "  " createdUtc []
    assertResult (Error (InvalidName InvalidProjectName)) actual

[<Fact; Trait("Task", "T012")>]
let ``project creation stores the trimmed display name and normalized name`` () =
    let actual = ResearchProject.create (Guid.NewGuid()) (Guid.NewGuid()) " family arbitration " createdUtc []
    let expectedKey =
        ProjectName.create "Family Arbitration"
        |> Result.mapError InvalidName
        |> Result.map (fun name -> name.Normalized)
    assertResult (Ok "family arbitration") (actual |> Result.map (fun project -> project.Name))
    assertResult expectedKey (actual |> Result.map (fun project -> project.NormalizedName))

[<Fact; Trait("Task", "T012")>]
let ``names differing only in capitalization normalize identically`` () =
    let first = normalized "Family Arbitration"
    let second = normalized "FAMILY ARBITRATION"
    Assert.True(Result.isOk first)
    Assert.True(Result.isOk second)
    assertResult first second

[<Fact; Trait("Task", "T012")>]
let ``names differing only in surrounding whitespace normalize identically`` () =
    let first = normalized "Family Arbitration"
    let second = normalized "  Family Arbitration\t"
    Assert.True(Result.isOk first)
    Assert.True(Result.isOk second)
    assertResult first second

[<Fact; Trait("Task", "T012")>]
let ``case-insensitive normalization is independent of current culture`` () =
    let previous = CultureInfo.CurrentCulture
    try
        CultureInfo.CurrentCulture <- CultureInfo.GetCultureInfo("en-US")
        let english = normalized "isolation"
        CultureInfo.CurrentCulture <- CultureInfo.GetCultureInfo("tr-TR")
        let turkish = normalized "isolation"
        let uppercase = normalized "ISOLATION"
        Assert.True(Result.isOk english)
        Assert.True(Result.isOk turkish)
        Assert.True(Result.isOk uppercase)
        assertResult english turkish
        assertResult turkish uppercase
    finally
        CultureInfo.CurrentCulture <- previous

[<Fact; Trait("Task", "T012")>]
let ``case and whitespace variants are duplicates within a workspace`` () =
    let workspaceId = Guid.NewGuid()
    let first = ResearchProject.create (Guid.NewGuid()) workspaceId "Family Arbitration" createdUtc []
    let attempted =
        first |> Result.bind (fun existing ->
            ResearchProject.create (Guid.NewGuid()) workspaceId " family arbitration " createdUtc [ existing ])
    assertResult (Error DuplicateProjectName) attempted

[<Fact; Trait("Task", "T012")>]
let ``an exact duplicate is rejected within a workspace`` () =
    let workspaceId = Guid.NewGuid()
    let first = ResearchProject.create (Guid.NewGuid()) workspaceId "Family Arbitration" createdUtc []
    let attempted =
        first |> Result.bind (fun existing ->
            ResearchProject.create (Guid.NewGuid()) workspaceId "Family Arbitration" createdUtc [ existing ])
    assertResult (Error DuplicateProjectName) attempted

[<Fact; Trait("Task", "T012")>]
let ``a duplicate attempt leaves existing project data unchanged`` () =
    let workspaceId = Guid.NewGuid()
    let projectId = Guid.NewGuid()
    let otherId = Guid.NewGuid()
    let first = ResearchProject.create projectId workspaceId "Family Arbitration" createdUtc []
    let existingProjects =
        first |> Result.bind (fun existing ->
            ResearchProject.create otherId workspaceId "Comparative Law" createdUtc [ existing ]
            |> Result.map (fun other -> [ existing; other ]))
    let observation =
        existingProjects |> Result.map (fun projects ->
            // Snapshot every field before the attempted creation.
            let before = projects |> List.map (fun project -> { project with Name = project.Name })
            let attempted = ResearchProject.create (Guid.NewGuid()) workspaceId " family arbitration " createdUtc projects
            attempted, projects, before)
    let preserved = observation |> Result.map (fun (_, after, before) -> after = before)
    let attempted = observation |> Result.bind (fun (result, _, _) -> result)
    assertResult (Ok true) preserved
    assertResult (Error DuplicateProjectName) attempted

[<Fact; Trait("Task", "T012")>]
let ``independent workspaces can use the same project name`` () =
    let firstWorkspace = Guid.NewGuid()
    let secondWorkspace = Guid.NewGuid()
    let secondId = Guid.NewGuid()
    let first = ResearchProject.create (Guid.NewGuid()) firstWorkspace "Family Arbitration" createdUtc []
    let second =
        first |> Result.bind (fun existing ->
            ResearchProject.create secondId secondWorkspace "Family Arbitration" createdUtc [ existing ])
    let actual = second |> Result.map (fun project -> project.Id, project.WorkspaceId, project.Name)
    assertResult (Ok (secondId, secondWorkspace, "Family Arbitration")) actual

[<Fact; Trait("Task", "T012")>]
let ``different names can coexist in the same workspace`` () =
    let workspaceId = Guid.NewGuid()
    let first = ResearchProject.create (Guid.NewGuid()) workspaceId "Family Arbitration" createdUtc []
    let second =
        first |> Result.bind (fun existing ->
            ResearchProject.create (Guid.NewGuid()) workspaceId "Comparative Law" createdUtc [ existing ])
    assertResult (Ok "Comparative Law") (second |> Result.map (fun project -> project.Name))

[<Fact; Trait("Task", "T012")>]
let ``workspace and project retain UUID identities and UTC creation time`` () =
    let workspaceId = Guid.NewGuid()
    let projectId = Guid.NewGuid()
    let workspace : Workspace = { Id = workspaceId; ActiveProjectId = None; CreatedUtc = createdUtc }
    let project = ResearchProject.create projectId workspaceId "Family Arbitration" createdUtc []
    let actual = project |> Result.map (fun value -> value.Id, value.WorkspaceId, value.CreatedUtc)
    Assert.Equal<Guid>(workspaceId, workspace.Id)
    Assert.Equal(TimeSpan.Zero, workspace.CreatedUtc.Offset)
    assertResult (Ok (projectId, workspaceId, createdUtc)) actual

[<Fact; Trait("Task", "T012")>]
let ``a workspace can have no active project`` () =
    let workspace : Workspace = { Id = Guid.NewGuid(); ActiveProjectId = None; CreatedUtc = createdUtc }
    Assert.Equal<Guid option>(None, workspace.ActiveProjectId)
