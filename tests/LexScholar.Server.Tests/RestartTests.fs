/// SC-005 restart cycles (T041): 100 consecutive in-process host restarts over
/// one workspace keep every project, library document, association, role and
/// the last active project. The active project alternates each cycle, so its
/// restoration is checked on every restart.
module LexScholar.Server.Tests.RestartTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit

let private fixture name = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))

let private send (client: HttpClient) (method: HttpMethod) (path: string) (body: string option) =
    use tokenResponse = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    use tokenJson = JsonDocument.Parse(tokenResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    use message = new HttpRequestMessage(method, path)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString()) |> ignore
    body |> Option.iter (fun text -> message.Content <- new StringContent(text, Encoding.UTF8, "application/json"))
    client.Send(message)

let private expect (status: HttpStatusCode) (response: HttpResponseMessage) =
    let body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert.True((status = response.StatusCode), $"Expected {status}, got {response.StatusCode}: {body}")
    body

let private created (response: HttpResponseMessage) =
    use document = JsonDocument.Parse(expect HttpStatusCode.Created response)
    document.RootElement.GetProperty("id").GetGuid()

let private upload (client: HttpClient) name =
    let content = new MultipartFormDataContent()
    content.Add(new ByteArrayContent(fixture name), "file", name)
    use tokenResponse = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    use tokenJson = JsonDocument.Parse(tokenResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    use message = new HttpRequestMessage(HttpMethod.Post, "/api/documents", Content = content)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString()) |> ignore
    use response = client.Send(message)
    created response

let private get (client: HttpClient) (path: string) = client.GetStringAsync(path).GetAwaiter().GetResult()

/// Everything SC-005 requires to survive a restart, read through the API:
/// active project, projects, library documents, each project's associations
/// and roles, and the SHA-256 of each document's served content.
let private observe (client: HttpClient) =
    use projects = JsonDocument.Parse(get client "/api/projects")
    let active =
        match projects.RootElement.GetProperty("activeProjectId") with
        | value when value.ValueKind = JsonValueKind.Null -> None
        | value -> Some (value.GetGuid())
    let projectList =
        [ for item in projects.RootElement.GetProperty("projects").EnumerateArray() ->
            item.GetProperty("id").GetGuid(), item.GetProperty("name").GetString(), item.GetProperty("createdUtc").GetDateTimeOffset() ]
    let documents = get client "/api/documents"
    let associations = [ for id, _, _ in projectList -> id, get client $"/api/projects/{id}/documents" ]
    use library = JsonDocument.Parse(documents)
    let contentHashes =
        [ for item in library.RootElement.EnumerateArray() ->
            let id = item.GetProperty("id").GetGuid()
            id, Convert.ToHexStringLower(SHA256.HashData(client.GetByteArrayAsync($"/api/documents/{id}/content").GetAwaiter().GetResult())) ]
    active, projectList, documents, associations, contentHashes

[<Fact; Trait("Task", "T041")>]
let ``100 consecutive restarts keep every project, document, association and the active project`` () =
    use workspace = TemporaryWorkspace.Create()
    let projectA, projectB, expected =
        use harness = TestHarness.Start(workspace = workspace)
        let client = harness.Client
        let createProject (name: string) =
            use response = send client HttpMethod.Post "/api/projects" (Some (JsonSerializer.Serialize({| name = name |})))
            created response
        let projectA = createProject "Restart A"
        let projectB = createProject "Restart B"
        let valid = upload client "valid.pdf"
        let distinct = upload client "distinct.pdf"
        for project, document, role in [ projectA, valid, "FocusedSource"; projectA, distinct, "ReferenceSource"
                                         projectB, valid, "ReferenceSource"; projectB, distinct, "FocusedSource" ] do
            use response = send client HttpMethod.Post $"/api/projects/{project}/documents" (Some $"""{{"documentId":"{document}","role":"{role}"}}""")
            expect HttpStatusCode.Created response |> ignore
        use changed = send client HttpMethod.Patch $"/api/projects/{projectA}/documents/{valid}" (Some """{"role":"ReferenceSource"}""")
        expect HttpStatusCode.OK changed |> ignore
        use removed = send client HttpMethod.Delete $"/api/projects/{projectB}/documents/{distinct}" None
        expect HttpStatusCode.NoContent removed |> ignore
        use selected = send client HttpMethod.Put $"/api/projects/{projectA}/active" None
        expect HttpStatusCode.NoContent selected |> ignore
        projectA, projectB, observe client
    let _, projects, documents, associations, hashes = expected
    Assert.Equal(2, projects.Length)
    Assert.Equal(2, hashes.Length)
    let mutable active = projectA
    for cycle in 1 .. 100 do
        use harness = TestHarness.Start(workspace = workspace)
        let observedActive, observedProjects, observedDocuments, observedAssociations, observedHashes = observe harness.Client
        let failure what = $"Restart cycle {cycle}: {what} changed."
        Assert.True((Some active = observedActive), failure "the active project")
        Assert.True((projects = observedProjects), failure "the projects")
        Assert.True((documents = observedDocuments), failure "the library documents")
        Assert.True((associations = observedAssociations), failure "the associations or roles")
        Assert.True((hashes = observedHashes), failure "the served document content")
        // Select the other project, so the next restart must restore a new value.
        active <- if active = projectA then projectB else projectA
        use selected = send harness.Client HttpMethod.Put $"/api/projects/{active}/active" None
        expect HttpStatusCode.NoContent selected |> ignore
