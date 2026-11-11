/// HTTP contract tests for library listing, import, project associations and
/// safe failure responses (T023), against contracts/http-api.yaml.
module LexScholar.Server.Tests.LibraryContractTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading.Tasks
open LexScholar.Infrastructure
open Xunit

let private fixture name = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))

let private seedProjects directory count =
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

let private token (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    let result = json.RootElement.GetProperty("requestToken").GetString()
    Assert.False(String.IsNullOrWhiteSpace(result))
    result

let private request (client: HttpClient) (method: HttpMethod) (path: string) (csrf: string) (content: HttpContent option) =
    let message = new HttpRequestMessage(method, path)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf) |> ignore
    content |> Option.iter (fun body -> message.Content <- body)
    message

let private send (client: HttpClient) method path csrf (body: string option) =
    use message = request client method path csrf (body |> Option.map (fun text -> new StringContent(text, Encoding.UTF8, "application/json") :> HttpContent))
    client.Send(message)

let private multipart (parts: (string * string * byte array) list) =
    let content = new MultipartFormDataContent()
    for name, fileName, bytes in parts do
        let part = new ByteArrayContent(bytes)
        part.Headers.ContentType <- MediaTypeHeaderValue("application/pdf")
        if isNull fileName then content.Add(part, name) else content.Add(part, name, fileName)
    content

let private uploadAsync (client: HttpClient) csrf (bytes: byte array) (fileName: string) : Task<HttpResponseMessage> =
    let message = request client HttpMethod.Post "/api/documents" csrf (Some (multipart [ "file", fileName, bytes ] :> HttpContent))
    client.SendAsync(message)

let private upload client csrf bytes fileName = (uploadAsync client csrf bytes fileName).GetAwaiter().GetResult()

let private expectStatus status (response: HttpResponseMessage) =
    Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode)
    Assert.Equal(status, response.StatusCode)

let private body (response: HttpResponseMessage) = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

let private json (response: HttpResponseMessage) = JsonDocument.Parse(body response)

/// A problem response with the expected code that discloses no filesystem path.
let private expectProblem status code (root: string) (response: HttpResponseMessage) =
    expectStatus status response
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType.MediaType)
    let text = body response
    Assert.DoesNotContain(root, text)
    use document = JsonDocument.Parse(text)
    Assert.Equal(code, document.RootElement.GetProperty("code").GetString())

let private getJson (client: HttpClient) (path: string) =
    use response = client.GetAsync(path).GetAwaiter().GetResult()
    expectStatus HttpStatusCode.OK response
    json response

let private documentIds (client: HttpClient) =
    use listing = getJson client "/api/documents"
    [ for item in listing.RootElement.EnumerateArray() -> item.GetProperty("id").GetGuid() ]

let private files (directory: string) =
    if Directory.Exists(directory) then Directory.GetFiles(directory) |> Array.map Path.GetFileName |> Array.sort |> Array.toList
    else []

let private importId (response: HttpResponseMessage) =
    use document = json response
    document.RootElement.GetProperty("id").GetGuid()

/// Imports a fixture through HTTP and returns the new document's id.
let private imported client csrf name =
    use response = upload client csrf (fixture name) name
    expectStatus HttpStatusCode.Created response
    importId response

let private associationBody (documentId: string) (role: string) =
    JsonSerializer.Serialize({| documentId = documentId; role = role |})

let private associate client csrf (project: Guid) (document: Guid) role =
    send client HttpMethod.Post $"/api/projects/{project}/documents" csrf (Some (associationBody (string document) role))

/// (projectId, documentId, role) for each association listed for a project.
let private projectDocuments (client: HttpClient) (project: Guid) =
    use listing = getJson client $"/api/projects/{project}/documents"
    [ for item in listing.RootElement.EnumerateArray() ->
        item.GetProperty("projectId").GetGuid(), item.GetProperty("document").GetProperty("id").GetGuid(), item.GetProperty("role").GetString() ]

let private assertDocumentShape (element: JsonElement) (fileName: string) =
    Assert.NotEqual(Guid.Empty, element.GetProperty("id").GetGuid())
    Assert.Equal(fileName, element.GetProperty("originalFileName").GetString())
    Assert.Equal(753L, element.GetProperty("byteLength").GetInt64())
    Assert.Equal(2, element.GetProperty("pageCount").GetInt32())
    Assert.Equal(TimeSpan.Zero, element.GetProperty("importedUtc").GetDateTimeOffset().Offset)
    let id = element.GetProperty("id").GetGuid()
    Assert.Equal($"/api/documents/{id:D}/content", element.GetProperty("contentUrl").GetString())

[<Fact; Trait("Task", "T023")>]
let ``POST imports a new PDF, reuses identical bytes and keeps different bytes distinct`` () =
    use harness = TestHarness.Start()
    let csrf = token harness.Client
    use first = upload harness.Client csrf (fixture "valid.pdf") "valid.pdf"
    expectStatus HttpStatusCode.Created first
    use created = json first
    assertDocumentShape created.RootElement "valid.pdf"
    let id = created.RootElement.GetProperty("id").GetGuid()
    Assert.Equal(created.RootElement.GetProperty("contentUrl").GetString(), string first.Headers.Location)
    use second = upload harness.Client csrf (fixture "valid-renamed.pdf") "valid-renamed.pdf"
    expectStatus HttpStatusCode.OK second
    use reused = json second
    Assert.Equal(id, reused.RootElement.GetProperty("id").GetGuid())
    Assert.Equal("valid.pdf", reused.RootElement.GetProperty("originalFileName").GetString())
    let distinct = imported harness.Client csrf "distinct.pdf"
    Assert.NotEqual(id, distinct)
    Assert.Equal<Guid list>([ id; distinct ], documentIds harness.Client)
    Assert.Equal(2, (files (Path.Combine(harness.Root, "originals"))).Length)

[<Fact; Trait("Task", "T023")>]
let ``GET lists every library document, including unassociated ones`` () =
    use harness = TestHarness.Start()
    let csrf = token harness.Client
    let project = seedProjects harness.Root 1 |> List.head
    let associated = imported harness.Client csrf "valid.pdf"
    let unassociated = imported harness.Client csrf "distinct.pdf"
    do
        use response = associate harness.Client csrf project associated "FocusedSource"
        expectStatus HttpStatusCode.Created response
    use listing = getJson harness.Client "/api/documents"
    let items = listing.RootElement.EnumerateArray() |> Seq.toList
    Assert.Equal<Guid list>([ associated; unassociated ], items |> List.map (fun item -> item.GetProperty("id").GetGuid()))
    assertDocumentShape items[0] "valid.pdf"

[<Theory; Trait("Task", "T023")>]
[<InlineData("not-a-pdf.txt", 100000000L, 2000, 422, "not_a_pdf")>]
[<InlineData("corrupt.pdf", 100000000L, 2000, 422, "corrupt_pdf")>]
[<InlineData("encrypted.pdf", 100000000L, 2000, 422, "encrypted_pdf")>]
[<InlineData("valid.pdf", 752L, 2000, 413, "upload_too_large")>]
[<InlineData("valid.pdf", 100000000L, 1, 422, "page_limit_exceeded")>]
let ``POST rejects invalid or over-limit uploads safely and changes nothing`` (name: string, maxBytes: int64, maxPages: int, status: int, code: string) =
    use workspace = TemporaryWorkspace.Create()
    let existing =
        use seeding = TestHarness.Start(workspace = workspace)
        imported seeding.Client (token seeding.Client) "distinct.pdf"
    use harness =
        TestHarness.Start([ "LexScholar:ImportLimits:MaxBytes", string maxBytes; "LexScholar:ImportLimits:MaxPages", string maxPages ], workspace = workspace)
    let csrf = token harness.Client
    let originals = files (Path.Combine(harness.Root, "originals"))
    use response = upload harness.Client csrf (fixture name) name
    expectProblem (enum<HttpStatusCode> status) code harness.Root response
    Assert.Equal<Guid list>([ existing ], documentIds harness.Client)
    Assert.Equal<string list>(originals, files (Path.Combine(harness.Root, "originals")))
    Assert.Empty(files (Path.Combine(harness.Root, "staging")))

[<Fact; Trait("Task", "T023")>]
let ``POST without a file part is rejected as not a PDF without importing`` () =
    use harness = TestHarness.Start()
    let csrf = token harness.Client
    let notMultipart = new StringContent("{}", Encoding.UTF8, "application/json")
    use first = harness.Client.Send(request harness.Client HttpMethod.Post "/api/documents" csrf (Some notMultipart))
    expectProblem HttpStatusCode.BadRequest "not_a_pdf" harness.Root first
    // Only the first part is considered; a PDF after another part is not read.
    let otherFirst = multipart [ "note", null, Encoding.UTF8.GetBytes("note"); "file", "valid.pdf", fixture "valid.pdf" ]
    use second = harness.Client.Send(request harness.Client HttpMethod.Post "/api/documents" csrf (Some otherFirst))
    expectProblem HttpStatusCode.BadRequest "not_a_pdf" harness.Root second
    Assert.Empty(documentIds harness.Client)
    Assert.Empty(files (Path.Combine(harness.Root, "originals")))
    Assert.Empty(files (Path.Combine(harness.Root, "staging")))

[<Fact; Trait("Task", "T023")>]
let ``POST accepts bodies above the server's default request size up to the configured limit`` () =
    // Kestrel's default MaxRequestBodySize is 30,000,000 bytes; the configured
    // import limit, not that default, must decide.
    use harness = TestHarness.Start([ "LexScholar:ImportLimits:MaxBytes", "40000000" ])
    let csrf = token harness.Client
    let large = Array.create 30_000_001 (byte 'a')
    use response = upload harness.Client csrf large "large.pdf"
    expectProblem (enum<HttpStatusCode> 422) "not_a_pdf" harness.Root response
    Assert.Empty(documentIds harness.Client)
    Assert.Empty(files (Path.Combine(harness.Root, "staging")))

[<Fact; Trait("Task", "T023")>]
let ``simultaneous uploads of the same PDF all succeed with the same document`` () =
    use harness = TestHarness.Start()
    let csrf = token harness.Client
    let bytes = fixture "valid.pdf"
    let responses =
        Task.WhenAll([| for index in 1 .. 8 -> uploadAsync harness.Client csrf bytes (if index % 2 = 0 then "valid.pdf" else "valid-renamed.pdf") |])
            .GetAwaiter().GetResult()
    try
        for response in responses do
            Assert.True(response.StatusCode = HttpStatusCode.Created || response.StatusCode = HttpStatusCode.OK,
                         $"Expected 201 or 200, got {int response.StatusCode}.")
        let ids = responses |> Array.map importId |> Array.distinct
        Assert.Single(ids) |> ignore
        Assert.Equal(1, responses |> Array.filter (fun response -> response.StatusCode = HttpStatusCode.Created) |> Array.length)
        Assert.Equal<Guid list>(List.ofArray ids, documentIds harness.Client)
        Assert.Single(files (Path.Combine(harness.Root, "originals"))) |> ignore
        Assert.Empty(files (Path.Combine(harness.Root, "staging")))
    finally
        for response in responses do response.Dispose()

[<Fact; Trait("Task", "T023")>]
let ``POST associates one document with two projects under their own roles`` () =
    use harness = TestHarness.Start()
    let csrf = token harness.Client
    let projects = seedProjects harness.Root 2
    let document = imported harness.Client csrf "valid.pdf"
    for project, role in [ projects[0], "FocusedSource"; projects[1], "ReferenceSource" ] do
        use response = associate harness.Client csrf project document role
        expectStatus HttpStatusCode.Created response
        Assert.Equal($"/api/projects/{project:D}/documents/{document:D}", string response.Headers.Location)
        use created = json response
        Assert.Equal(project, created.RootElement.GetProperty("projectId").GetGuid())
        Assert.Equal(role, created.RootElement.GetProperty("role").GetString())
        Assert.Equal(TimeSpan.Zero, created.RootElement.GetProperty("addedUtc").GetDateTimeOffset().Offset)
        assertDocumentShape (created.RootElement.GetProperty("document")) "valid.pdf"
    Assert.Equal<(Guid * Guid * string) list>([ projects[0], document, "FocusedSource" ], projectDocuments harness.Client projects[0])
    Assert.Equal<(Guid * Guid * string) list>([ projects[1], document, "ReferenceSource" ], projectDocuments harness.Client projects[1])

[<Fact; Trait("Task", "T023")>]
let ``POST association failures return contract problems and change nothing`` () =
    use harness = TestHarness.Start()
    let csrf = token harness.Client
    let project = seedProjects harness.Root 1 |> List.head
    let document = imported harness.Client csrf "valid.pdf"
    do
        use response = associate harness.Client csrf project document "FocusedSource"
        expectStatus HttpStatusCode.Created response
    let path = $"/api/projects/{project}/documents"
    let cases =
        [ Some (associationBody (string document) "Primary"), HttpStatusCode.BadRequest, "invalid_role"
          Some (JsonSerializer.Serialize({| documentId = document |})), HttpStatusCode.BadRequest, "invalid_role"
          Some "{ not json", HttpStatusCode.BadRequest, "invalid_role"
          None, HttpStatusCode.BadRequest, "invalid_role"
          Some (associationBody "not-a-uuid" "ReferenceSource"), HttpStatusCode.NotFound, "document_not_found"
          Some (JsonSerializer.Serialize({| role = "ReferenceSource" |})), HttpStatusCode.NotFound, "document_not_found"
          Some (associationBody (string (Guid.NewGuid())) "ReferenceSource"), HttpStatusCode.NotFound, "document_not_found"
          Some (associationBody (string document) "ReferenceSource"), HttpStatusCode.Conflict, "duplicate_association" ]
    for payload, status, code in cases do
        use response = send harness.Client HttpMethod.Post path csrf payload
        expectProblem status code harness.Root response
    do
        use response = associate harness.Client csrf (Guid.NewGuid()) document "ReferenceSource"
        expectProblem HttpStatusCode.NotFound "not_found" harness.Root response
    Assert.Equal<(Guid * Guid * string) list>([ project, document, "FocusedSource" ], projectDocuments harness.Client project)

[<Fact; Trait("Task", "T023")>]
let ``DELETE removes only that association and keeps the shared document`` () =
    use harness = TestHarness.Start()
    let csrf = token harness.Client
    let projects = seedProjects harness.Root 2
    let document = imported harness.Client csrf "valid.pdf"
    for project, role in [ projects[0], "FocusedSource"; projects[1], "ReferenceSource" ] do
        use response = associate harness.Client csrf project document role
        expectStatus HttpStatusCode.Created response
    let path = $"/api/projects/{projects[0]}/documents/{document}"
    do
        use response = send harness.Client HttpMethod.Delete path csrf None
        expectStatus HttpStatusCode.NoContent response
    Assert.Empty(projectDocuments harness.Client projects[0])
    Assert.Equal<(Guid * Guid * string) list>([ projects[1], document, "ReferenceSource" ], projectDocuments harness.Client projects[1])
    Assert.Equal<Guid list>([ document ], documentIds harness.Client)
    do
        use response = send harness.Client HttpMethod.Delete path csrf None
        expectProblem HttpStatusCode.NotFound "not_found" harness.Root response
    // Removing the final association keeps the document in the library.
    do
        use response = send harness.Client HttpMethod.Delete $"/api/projects/{projects[1]}/documents/{document}" csrf None
        expectStatus HttpStatusCode.NoContent response
    Assert.Equal<Guid list>([ document ], documentIds harness.Client)
    Assert.Single(files (Path.Combine(harness.Root, "originals"))) |> ignore

[<Fact; Trait("Task", "T023")>]
let ``GET project documents returns not_found for an unknown project and an empty list for an empty one`` () =
    use harness = TestHarness.Start()
    let project = seedProjects harness.Root 1 |> List.head
    Assert.Empty(projectDocuments harness.Client project)
    use response = harness.Client.GetAsync($"/api/projects/{Guid.NewGuid()}/documents").GetAwaiter().GetResult()
    expectProblem HttpStatusCode.NotFound "not_found" harness.Root response
