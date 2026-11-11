module LexScholar.Server.Tests.ProjectContractTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open Microsoft.EntityFrameworkCore
open LexScholar.Infrastructure
open LexScholar.Server.Tests.ProcessHarness
open Xunit

let private seed directory workspace projects active =
    use db = WorkspaceDb.create directory
    let time = DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero)
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    for id, name in projects do
        db.Add(ResearchProjectRow(Id = id, WorkspaceId = workspace, Name = name, NormalizedName = name.ToUpperInvariant(), CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    match active with
    | Some id ->
        let row = db.Set<WorkspaceRow>().Find(workspace)
        row.ActiveProjectId <- Nullable(id)
        db.SaveChanges() |> ignore
    | None -> ()

let private token (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    let result = json.RootElement.GetProperty("requestToken").GetString()
    Assert.False(String.IsNullOrWhiteSpace(result))
    result

let private send (client: HttpClient) (method: HttpMethod) (path: string) (csrf: string) (body: string option) =
    use request = new HttpRequestMessage(method, path)
    request.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf) |> ignore
    body |> Option.iter (fun text -> request.Content <- new StringContent(text, Encoding.UTF8, "application/json"))
    client.Send(request)

let private expectStatus status (response: HttpResponseMessage) =
    Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode)
    Assert.Equal(status, response.StatusCode)

let private expectCode code (response: HttpResponseMessage) =
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType.MediaType)
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    Assert.Equal(code, json.RootElement.GetProperty("code").GetString())

let private countProjects directory =
    use db = WorkspaceDb.create directory
    db.Set<ResearchProjectRow>().CountAsync().GetAwaiter().GetResult()

[<Fact; Trait("Task", "T013")>]
let ``GET lists persisted projects and active selection`` () =
    use harness = TestHarness.Start()
    let workspace, id = Guid.NewGuid(), Guid.NewGuid()
    seed harness.Root workspace [ id, "Family Arbitration" ] (Some id)
    use response = harness.Client.GetAsync("/api/projects").GetAwaiter().GetResult()
    expectStatus HttpStatusCode.OK response
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    Assert.Equal(id, json.RootElement.GetProperty("activeProjectId").GetGuid())
    Assert.Equal(id, json.RootElement.GetProperty("projects").[0].GetProperty("id").GetGuid())

[<Fact; Trait("Task", "T013")>]
let ``POST creates a trimmed project with a location`` () =
    use harness = TestHarness.Start()
    seed harness.Root (Guid.NewGuid()) [] None
    let csrf = token harness.Client
    let body = JsonSerializer.Serialize({| name = " Family Arbitration " |})
    use response = send harness.Client HttpMethod.Post "/api/projects" csrf (Some body)
    expectStatus HttpStatusCode.Created response
    Assert.NotNull(response.Headers.Location)
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    Assert.Equal("Family Arbitration", json.RootElement.GetProperty("name").GetString())
    Assert.NotEqual(Guid.Empty, json.RootElement.GetProperty("id").GetGuid())
    Assert.Equal(1, countProjects harness.Root)

[<Fact; Trait("Task", "T013")>]
let ``POST invalid names returns invalid_name`` () =
    use harness = TestHarness.Start()
    seed harness.Root (Guid.NewGuid()) [] None
    let csrf = token harness.Client
    let body = JsonSerializer.Serialize({| name = "   " |})
    use response = send harness.Client HttpMethod.Post "/api/projects" csrf (Some body)
    Assert.Equal(0, countProjects harness.Root)
    expectStatus HttpStatusCode.BadRequest response
    expectCode "invalid_name" response

[<Fact; Trait("Task", "T013")>]
let ``POST duplicate returns conflict and preserves existing data`` () =
    use harness = TestHarness.Start()
    let project = Guid.NewGuid()
    seed harness.Root (Guid.NewGuid()) [ project, "Family Arbitration" ] None
    let csrf = token harness.Client
    let body = JsonSerializer.Serialize({| name = " family arbitration " |})
    use response = send harness.Client HttpMethod.Post "/api/projects" csrf (Some body)
    Assert.Equal(1, countProjects harness.Root)
    do
        use db = WorkspaceDb.create harness.Root
        let existing = db.Set<ResearchProjectRow>().Find(project)
        Assert.NotNull(existing)
        Assert.Equal("Family Arbitration", existing.Name)
        Assert.Equal("FAMILY ARBITRATION", existing.NormalizedName)
    expectStatus HttpStatusCode.Conflict response
    expectCode "duplicate_project_name" response

[<Fact; Trait("Task", "T013")>]
let ``PUT selects and switches between existing projects`` () =
    use harness = TestHarness.Start()
    let first, second = Guid.NewGuid(), Guid.NewGuid()
    seed harness.Root (Guid.NewGuid()) [ first, "First"; second, "Second" ] None
    let csrf = token harness.Client
    for id in [ first; second ] do
        use response = send harness.Client HttpMethod.Put $"/api/projects/{id}/active" csrf None
        expectStatus HttpStatusCode.NoContent response
        use listing = harness.Client.GetAsync("/api/projects").GetAwaiter().GetResult()
        expectStatus HttpStatusCode.OK listing
        use json = JsonDocument.Parse(listing.Content.ReadAsStringAsync().GetAwaiter().GetResult())
        Assert.Equal(id, json.RootElement.GetProperty("activeProjectId").GetGuid())

[<Fact; Trait("Task", "T013")>]
let ``PUT unknown project returns not_found`` () =
    use harness = TestHarness.Start()
    seed harness.Root (Guid.NewGuid()) [] None
    let csrf = token harness.Client
    use response = send harness.Client HttpMethod.Put $"/api/projects/{Guid.NewGuid()}/active" csrf None
    expectStatus HttpStatusCode.NotFound response
    expectCode "not_found" response

[<Fact; Trait("Task", "T013")>]
let ``active selection is restored after host restart`` () =
    use workspace = TemporaryWorkspace.Create()
    let project = Guid.NewGuid()
    use first = HostProcess.Start(workspace.Root, [])
    Assert.Equal("ready", first.Startup.Kind)
    seed workspace.Root (Guid.NewGuid()) [ project, "First" ] None
    use firstClient = client first
    let csrf = token firstClient
    use selected = send firstClient HttpMethod.Put $"/api/projects/{project}/active" csrf None
    expectStatus HttpStatusCode.NoContent selected
    first.Stop()
    use second = HostProcess.Start(workspace.Root, [])
    Assert.Equal("ready", second.Startup.Kind)
    use secondClient = client second
    use response = secondClient.GetAsync("/api/projects").GetAwaiter().GetResult()
    expectStatus HttpStatusCode.OK response
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    Assert.Equal(project, json.RootElement.GetProperty("activeProjectId").GetGuid())

[<Fact; Trait("Task", "T013")>]
let ``a persisted active selection is visible after an actual host restart`` () =
    use workspace = TemporaryWorkspace.Create()
    let project = Guid.NewGuid()
    use first = HostProcess.Start(workspace.Root, [])
    Assert.Equal("ready", first.Startup.Kind)
    seed workspace.Root (Guid.NewGuid()) [ project, "First" ] (Some project)
    first.Stop()
    use second = HostProcess.Start(workspace.Root, [])
    Assert.Equal("ready", second.Startup.Kind)
    use secondClient = client second
    use response = secondClient.GetAsync("/api/projects").GetAwaiter().GetResult()
    expectStatus HttpStatusCode.OK response
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    Assert.Equal(project, json.RootElement.GetProperty("activeProjectId").GetGuid())

[<Fact; Trait("Task", "T013")>]
let ``independent host workspaces list only their own projects`` () =
    use first = TestHarness.Start()
    use second = TestHarness.Start()
    let firstId, secondId = Guid.NewGuid(), Guid.NewGuid()
    seed first.Root (Guid.NewGuid()) [ firstId, "First" ] None
    seed second.Root (Guid.NewGuid()) [ secondId, "Second" ] None
    for client, expected in [ first.Client, firstId; second.Client, secondId ] do
        use response = client.GetAsync("/api/projects").GetAwaiter().GetResult()
        expectStatus HttpStatusCode.OK response
        use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
        let projects = json.RootElement.GetProperty("projects")
        Assert.Equal(1, projects.GetArrayLength())
        Assert.Equal(expected, projects.[0].GetProperty("id").GetGuid())
