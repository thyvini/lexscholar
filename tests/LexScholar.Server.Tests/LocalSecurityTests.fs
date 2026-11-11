/// Local-only storage, path traversal, original-byte preservation and error
/// non-disclosure checks over HTTP (T042; FR-012, FR-014, FR-017, SC-007).
/// Loopback binding, static exposure of the data directory and safe upload
/// rejection are covered by HostSecurityTests, ReaderContractTests and
/// LibraryContractTests; see evidence/t042-local-security.md.
module LexScholar.Server.Tests.LocalSecurityTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Security.Cryptography
open System.Text
open System.Text.Json
open LexScholar.Infrastructure
open Xunit

let private fixture name = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))

let private token (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    json.RootElement.GetProperty("requestToken").GetString()

let private send (client: HttpClient) (method: HttpMethod) (path: string) (content: HttpContent option) =
    let csrf = token client
    use message = new HttpRequestMessage(method, path)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf) |> ignore
    content |> Option.iter (fun body -> message.Content <- body)
    client.Send(message)

let private json (body: string) = new StringContent(body, Encoding.UTF8, "application/json") :> HttpContent

let private upload (client: HttpClient) (bytes: byte array) (fileName: string) =
    let content = new MultipartFormDataContent()
    let part = new ByteArrayContent(bytes)
    part.Headers.ContentType <- MediaTypeHeaderValue("application/pdf")
    content.Add(part, "file", fileName)
    send client HttpMethod.Post "/api/documents" (Some (content :> HttpContent))

let private text (response: HttpResponseMessage) = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

let private expectStatus (expected: HttpStatusCode) (response: HttpResponseMessage) =
    Assert.True((expected = response.StatusCode), $"Expected {expected}, got {response.StatusCode}: {text response}")

let private documentId (response: HttpResponseMessage) =
    use document = JsonDocument.Parse(text response)
    document.RootElement.GetProperty("id").GetGuid()

/// Every library row: id, storage key, SHA-256 and original filename.
let private rows (directory: string) =
    use db = WorkspaceDb.create directory
    [ for row in db.Set<LibraryDocumentRow>() -> row.Id, row.StorageKey, row.Sha256, row.OriginalFileName ]

let private seedProjects (directory: string) count =
    use db = WorkspaceDb.create directory
    let time = DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero)
    let workspace = Guid.NewGuid()
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    let ids = [ for _ in 1 .. count -> Guid.NewGuid() ]
    ids |> List.iteri (fun index id ->
        let name = $"Project {index + 1}"
        db.Add(ResearchProjectRow(Id = id, WorkspaceId = workspace, Name = name, NormalizedName = name.ToUpperInvariant(), CreatedUtc = time)) |> ignore)
    db.SaveChanges() |> ignore
    ids

let private isGeneratedKey (value: string) =
    value.Length = 32 && value |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

/// Every file under a directory, as paths relative to it.
let private allFiles (directory: string) =
    Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
    |> Array.map (fun path -> Path.GetRelativePath(directory, path))
    |> Array.sort |> Array.toList

let private contentMarkers = [ "%PDF"; "SQLite format" ]

// Path traversal (FR-012, FR-014)

let traversalIds : TheoryData<string> =
    TheoryData<string>([ "..%2F..%2Fworkspace.sqlite"; "%2e%2e%2f%2e%2e%2fetc%2fpasswd"; "originals%2F0123456789abcdef0123456789abcdef"; "..%5C..%5Cworkspace.sqlite" ])

[<Theory; MemberData(nameof traversalIds); Trait("Task", "T042")>]
let ``encoded traversal ids never reach a file`` (id: string) =
    use harness = TestHarness.Start()
    let projectId = Guid.NewGuid()
    let paths =
        [ HttpMethod.Get, $"/api/documents/{id}/content"
          HttpMethod.Patch, $"/api/projects/{projectId}/documents/{id}"
          HttpMethod.Delete, $"/api/projects/{projectId}/documents/{id}"
          HttpMethod.Get, $"/api/projects/{id}/documents" ]
    for method, path in paths do
        use response =
            if method = HttpMethod.Get then harness.Client.GetAsync(path).GetAwaiter().GetResult()
            else send harness.Client method path (Some (json """{"role":"FocusedSource"}"""))
        expectStatus HttpStatusCode.NotFound response
        let body = text response
        for marker in contentMarkers do Assert.DoesNotContain(marker, body)

[<Fact; Trait("Task", "T042")>]
let ``a storage key is never accepted as a document id`` () =
    use harness = TestHarness.Start()
    use imported = upload harness.Client (fixture "valid.pdf") "valid.pdf"
    expectStatus HttpStatusCode.Created imported
    let _, storageKey, _, _ = rows harness.Root |> List.exactlyOne
    // A 32-hex storage key is also a valid UUID, so it passes the route
    // constraint and reaches the lookup, which resolves only document ids.
    use response = harness.Client.GetAsync($"/api/documents/{storageKey}/content").GetAwaiter().GetResult()
    expectStatus HttpStatusCode.NotFound response
    use problem = JsonDocument.Parse(text response)
    Assert.Equal("document_not_found", problem.RootElement.GetProperty("code").GetString())

let traversalFileNames : TheoryData<string, string> =
    TheoryData<string, string>([ struct ("../../escape.pdf", "escape.pdf"); struct ("/tmp/abs.pdf", "abs.pdf"); struct (@"..\..\win.pdf", "win.pdf") ])

[<Theory; MemberData(nameof traversalFileNames); Trait("Task", "T042")>]
let ``a client filename never chooses a storage path`` (fileName: string, recorded: string) =
    use harness = TestHarness.Start()
    let before = allFiles harness.Root
    use response = upload harness.Client (fixture "valid.pdf") fileName
    expectStatus HttpStatusCode.Created response
    let _, storageKey, _, originalFileName = rows harness.Root |> List.exactlyOne
    Assert.Equal(recorded, originalFileName)
    Assert.True(isGeneratedKey storageKey, $"'{storageKey}' is not a generated key.")
    let created = allFiles harness.Root |> List.except before
    Assert.Equal<string list>([ Path.Combine("originals", storageKey) ], created |> List.filter (fun path -> not (path.StartsWith("workspace.sqlite"))))
    let parent = Path.GetDirectoryName(harness.Root)
    for name in [ "escape.pdf"; "abs.pdf"; "win.pdf" ] do
        Assert.False(File.Exists(Path.Combine(parent, name)), $"{name} was written outside the data directory.")

// Original-byte preservation (FR-012) and local storage (FR-014)

[<Fact; Trait("Task", "T042")>]
let ``original bytes are preserved and stored only under the data directory`` () =
    use data = TemporaryWorkspace.Create()
    let settings = [ "LexScholar:DataDirectory", data.Root ]
    let original = fixture "valid.pdf"
    // FR-014: the host writes nothing into its content root, which is not the
    // configured data directory here. Checked before each host is disposed.
    let assertContentRootUntouched (harness: TestHarness) =
        Assert.True(List.isEmpty (allFiles harness.Root), $"The host wrote files outside the data directory: {allFiles harness.Root}")
    let assertPreserved (harness: TestHarness) (id: Guid) step =
        let _, storageKey, sha256, _ = rows data.Root |> List.find (fun (rowId, _, _, _) -> rowId = id)
        let path = Path.Combine(data.Root, "originals", storageKey)
        Assert.True((UnixFileMode.UserRead = File.GetUnixFileMode(path)), $"{step}: the stored original is no longer read-only.")
        Assert.True((Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) = sha256), $"{step}: stored bytes differ from the recorded SHA-256.")
        let served = harness.Client.GetByteArrayAsync($"/api/documents/{id}/content").GetAwaiter().GetResult()
        Assert.True((served = original), $"{step}: served bytes differ from the uploaded original.")
    let id, projects =
        use harness = TestHarness.Start(settings)
        let projects = seedProjects data.Root 2
        use first = upload harness.Client original "valid.pdf"
        expectStatus HttpStatusCode.Created first
        let id = documentId first
        assertPreserved harness id "import"
        use again = upload harness.Client original "renamed.pdf"
        expectStatus HttpStatusCode.OK again
        assertPreserved harness id "identical re-import"
        for project, role in [ projects[0], "FocusedSource"; projects[1], "ReferenceSource" ] do
            use associated = send harness.Client HttpMethod.Post $"/api/projects/{project}/documents" (Some (json $"""{{"documentId":"{id}","role":"{role}"}}"""))
            expectStatus HttpStatusCode.Created associated
        assertPreserved harness id "association"
        use changed = send harness.Client HttpMethod.Patch $"/api/projects/{projects[0]}/documents/{id}" (Some (json """{"role":"ReferenceSource"}"""))
        expectStatus HttpStatusCode.OK changed
        assertPreserved harness id "role change"
        use removed = send harness.Client HttpMethod.Delete $"/api/projects/{projects[1]}/documents/{id}" None
        expectStatus HttpStatusCode.NoContent removed
        assertPreserved harness id "association removal"
        assertContentRootUntouched harness
        id, projects
    ignore projects
    do
        use restarted = TestHarness.Start(settings)
        assertPreserved restarted id "restart"
        assertContentRootUntouched restarted
    // Inside the data directory: only the database, the lock and originals.
    let stored = allFiles data.Root
    let expected = set [ "workspace.sqlite"; ".lexscholar.lock" ]
    let unexpected =
        stored |> List.filter (fun path ->
            not (expected.Contains path) && not (path.StartsWith("workspace.sqlite-")) && not (path.StartsWith("originals" + string Path.DirectorySeparatorChar)))
    Assert.Empty(unexpected)

// Error non-disclosure (FR-012, FR-017, SC-007)

let errorCases : TheoryData<string> =
    TheoryData<string>([ "no-file"; "not-a-pdf"; "corrupt"; "encrypted"; "too-large"; "too-many-pages"
                         "invalid-role"; "unknown-project"; "unknown-document"; "duplicate-association"; "unavailable" ])

[<Theory; MemberData(nameof errorCases); Trait("Task", "T042")>]
let ``error responses carry a contract code and disclose no stored data`` (case: string) =
    use data = TemporaryWorkspace.Create()
    let projectId, documentId, storageKey, sha256 =
        use seeding = TestHarness.Start(workspace = data)
        let projectId = seedProjects data.Root 1 |> List.head
        use imported = upload seeding.Client (fixture "valid.pdf") "secret-name.pdf"
        expectStatus HttpStatusCode.Created imported
        let id, key, sha, _ = rows data.Root |> List.exactlyOne
        use associated = send seeding.Client HttpMethod.Post $"/api/projects/{projectId}/documents" (Some (json $"""{{"documentId":"{id}","role":"FocusedSource"}}"""))
        expectStatus HttpStatusCode.Created associated
        projectId, id, key, sha
    let limits =
        match case with
        | "too-large" -> [ "LexScholar:ImportLimits:MaxBytes", "100" ]
        | "too-many-pages" -> [ "LexScholar:ImportLimits:MaxPages", "1" ]
        | _ -> []
    use harness = TestHarness.Start(limits, workspace = data)
    let client = harness.Client
    let status, code, (response: HttpResponseMessage) =
        match case with
        | "no-file" -> HttpStatusCode.BadRequest, "not_a_pdf", send client HttpMethod.Post "/api/documents" (Some (new MultipartFormDataContent() :> HttpContent))
        | "not-a-pdf" -> HttpStatusCode.UnprocessableEntity, "not_a_pdf", upload client (fixture "not-a-pdf.txt") "secret-name.pdf"
        | "corrupt" -> HttpStatusCode.UnprocessableEntity, "corrupt_pdf", upload client (fixture "corrupt.pdf") "secret-name.pdf"
        | "encrypted" -> HttpStatusCode.UnprocessableEntity, "encrypted_pdf", upload client (fixture "encrypted.pdf") "secret-name.pdf"
        | "too-large" -> HttpStatusCode.RequestEntityTooLarge, "upload_too_large", upload client (fixture "distinct.pdf") "secret-name.pdf"
        | "too-many-pages" -> HttpStatusCode.UnprocessableEntity, "page_limit_exceeded", upload client (fixture "distinct.pdf") "secret-name.pdf"
        | "invalid-role" -> HttpStatusCode.BadRequest, "invalid_role", send client HttpMethod.Patch $"/api/projects/{projectId}/documents/{documentId}" (Some (json """{"role":"../../etc"}"""))
        | "unknown-project" -> HttpStatusCode.NotFound, "not_found", send client HttpMethod.Patch $"/api/projects/{Guid.NewGuid()}/documents/{documentId}" (Some (json """{"role":"FocusedSource"}"""))
        | "unknown-document" -> HttpStatusCode.NotFound, "document_not_found", client.GetAsync($"/api/documents/{Guid.NewGuid()}/content").GetAwaiter().GetResult()
        | "duplicate-association" -> HttpStatusCode.Conflict, "duplicate_association", send client HttpMethod.Post $"/api/projects/{projectId}/documents" (Some (json $"""{{"documentId":"{documentId}","role":"ReferenceSource"}}"""))
        | "unavailable" ->
            File.Delete(Path.Combine(data.Root, "originals", storageKey))
            HttpStatusCode.ServiceUnavailable, "document_unavailable", client.GetAsync($"/api/documents/{documentId}/content").GetAwaiter().GetResult()
        | other -> failwithf "Unknown case %s" other
    use response = response
    expectStatus status response
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType.MediaType)
    let body = text response
    use problem = JsonDocument.Parse(body)
    Assert.Equal(code, problem.RootElement.GetProperty("code").GetString())
    for secret in [ data.Root; harness.Root; storageKey; sha256; "secret-name"; "originals" ] @ contentMarkers do
        Assert.DoesNotContain(secret, body)
