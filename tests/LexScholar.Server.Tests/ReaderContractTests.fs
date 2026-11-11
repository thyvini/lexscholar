/// HTTP contract tests for reading and classifying sources (T034): project-scoped
/// role changes and inline, range-capable content delivery with 404/503 problem
/// responses. Library data is arranged directly in the harness data directory.
module LexScholar.Server.Tests.ReaderContractTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Microsoft.Data.Sqlite
open LexScholar.Infrastructure
open Xunit

/// One workspace, projects A and B, and one library document associated with
/// both as FocusedSource, with its read-only original on disk.
type private Library =
    { ProjectA: Guid
      ProjectB: Guid
      DocumentId: Guid
      StorageKey: string
      Bytes: byte array }

let private time = DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero)

/// Deterministic content starting with a PDF signature. The content endpoint
/// does not parse, so the bytes need not be a complete PDF.
let private contentBytes =
    Array.append (Encoding.ASCII.GetBytes("%PDF-1.7\n")) (Array.init (4096 - 9) (fun i -> byte (i % 251)))

let private arrange directory =
    let library =
        { ProjectA = Guid.NewGuid()
          ProjectB = Guid.NewGuid()
          DocumentId = Guid.NewGuid()
          StorageKey = Guid.NewGuid().ToString("N")
          Bytes = contentBytes }
    use db = WorkspaceDb.create directory
    let workspace = Guid.NewGuid()
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    for id, name in [ library.ProjectA, "Project A"; library.ProjectB, "Project B" ] do
        db.Add(ResearchProjectRow(Id = id, WorkspaceId = workspace, Name = name, NormalizedName = name.ToUpperInvariant(), CreatedUtc = time)) |> ignore
    db.Add(LibraryDocumentRow(Id = library.DocumentId, Sha256 = Convert.ToHexStringLower(SHA256.HashData(library.Bytes)),
                              StorageKey = library.StorageKey, OriginalFileName = "source.pdf",
                              ByteLength = int64 library.Bytes.Length, PageCount = 1, ImportedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    for project in [ library.ProjectA; library.ProjectB ] do
        db.Add(ProjectDocumentRow(ProjectId = project, DocumentId = library.DocumentId, Role = "FocusedSource", AddedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    let path = Path.Combine(DocumentStore.originalsDirectory directory, library.StorageKey)
    File.WriteAllBytes(path, library.Bytes)
    File.SetUnixFileMode(path, UnixFileMode.UserRead)
    library

let private originalPath directory library =
    Path.Combine(DocumentStore.originalsDirectory directory, library.StorageKey)

let private readAll directory sql =
    use connection = new SqliteConnection(WorkspaceDb.connectionString directory)
    connection.Open()
    use command = connection.CreateCommand()
    command.CommandText <- sql
    use reader = command.ExecuteReader()
    [ while reader.Read() do
        yield [ for i in 0 .. reader.FieldCount - 1 -> string (reader.GetValue(i)) ] ]

/// Every stored column of every document and association row.
let private snapshot directory =
    readAll directory "SELECT Id, Sha256, StorageKey, OriginalFileName, ByteLength, PageCount, ImportedUtc FROM LibraryDocuments ORDER BY Id",
    readAll directory "SELECT ProjectId, DocumentId, Role, AddedUtc FROM ProjectDocuments ORDER BY ProjectId, DocumentId"

let private role directory (project: Guid) (document: Guid) =
    readAll directory $"SELECT Role FROM ProjectDocuments WHERE ProjectId = '{project:D}' AND DocumentId = '{document:D}'"
    |> List.exactlyOne |> List.exactlyOne

let private token (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    let result = json.RootElement.GetProperty("requestToken").GetString()
    Assert.False(String.IsNullOrWhiteSpace(result))
    result

let private patch (client: HttpClient) (path: string) (body: string) =
    let csrf = token client
    use request = new HttpRequestMessage(HttpMethod.Patch, path)
    request.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf) |> ignore
    request.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    client.Send(request)

let private getContent (client: HttpClient) (documentId: Guid) (range: RangeHeaderValue option) =
    use request = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{documentId:D}/content")
    range |> Option.iter (fun value -> request.Headers.Range <- value)
    client.Send(request)

let private bodyText (response: HttpResponseMessage) =
    response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

let private bodyBytes (response: HttpResponseMessage) =
    response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()

let private expectStatus status (response: HttpResponseMessage) =
    Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode)
    Assert.Equal(status, response.StatusCode)

let private expectCode code (response: HttpResponseMessage) =
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType.MediaType)
    use json = JsonDocument.Parse(bodyText response)
    Assert.Equal(code, json.RootElement.GetProperty("code").GetString())

/// Puts the stored original in the named unavailable state.
let private makeUnavailable condition (path: string) =
    let rewrite (bytes: byte array) =
        File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        File.WriteAllBytes(path, bytes)
        File.SetUnixFileMode(path, UnixFileMode.UserRead)
    match condition with
    | "missing" -> File.Delete(path)
    | "unreadable" ->
        File.SetUnixFileMode(path, UnixFileMode.None)
        let readable =
            try
                File.OpenRead(path).Dispose()
                true
            with :? UnauthorizedAccessException -> false
        Assert.False(readable, "The original is still readable with mode 000 (running as root or with CAP_DAC_OVERRIDE), so the unreadable case would not be exercised. Run the tests as a normal user.")
    | "shorter" -> rewrite (contentBytes |> Array.take 4000)
    | "longer" -> rewrite (Array.append contentBytes [| 0x0Auy |])
    | other -> failwithf "Unknown condition %s." other

let unavailableConditions : TheoryData<string> = TheoryData<string>([ "missing"; "unreadable"; "shorter"; "longer" ])

// Role isolation (FR-009, SC-004)

[<Fact; Trait("Task", "T034")>]
let ``PATCH changes the role in one project only`` () =
    use harness = TestHarness.Start()
    let library = arrange harness.Root
    let documentsBefore, _ = snapshot harness.Root
    use response =
        patch harness.Client $"/api/projects/{library.ProjectA:D}/documents/{library.DocumentId:D}" """{"role":"ReferenceSource"}"""
    expectStatus HttpStatusCode.OK response
    use json = JsonDocument.Parse(bodyText response)
    let body = json.RootElement
    Assert.Equal(library.ProjectA, body.GetProperty("projectId").GetGuid())
    Assert.Equal(library.DocumentId, body.GetProperty("document").GetProperty("id").GetGuid())
    Assert.Equal("ReferenceSource", body.GetProperty("role").GetString())
    Assert.Equal(time, body.GetProperty("addedUtc").GetDateTimeOffset())
    Assert.Equal("ReferenceSource", role harness.Root library.ProjectA library.DocumentId)
    Assert.Equal("FocusedSource", role harness.Root library.ProjectB library.DocumentId)
    let documentsAfter, _ = snapshot harness.Root
    Assert.Equal<string list list>(documentsBefore, documentsAfter)

[<Theory; InlineData("unassociated-document"); InlineData("unknown-project"); Trait("Task", "T034")>]
let ``PATCH for an association that does not exist is not found and changes nothing`` (case: string) =
    use harness = TestHarness.Start()
    let library = arrange harness.Root
    let path =
        match case with
        | "unassociated-document" -> $"/api/projects/{library.ProjectA:D}/documents/{Guid.NewGuid():D}"
        | _ -> $"/api/projects/{Guid.NewGuid():D}/documents/{library.DocumentId:D}"
    let before = snapshot harness.Root
    use response = patch harness.Client path """{"role":"ReferenceSource"}"""
    expectStatus HttpStatusCode.NotFound response
    expectCode "not_found" response
    Assert.Equal(before, snapshot harness.Root)

[<Theory; InlineData("""{"role":"Primary"}"""); InlineData("{}"); Trait("Task", "T034")>]
let ``PATCH with an invalid or missing role is rejected and changes nothing`` (body: string) =
    use harness = TestHarness.Start()
    let library = arrange harness.Root
    let before = snapshot harness.Root
    use response = patch harness.Client $"/api/projects/{library.ProjectA:D}/documents/{library.DocumentId:D}" body
    expectStatus HttpStatusCode.BadRequest response
    expectCode "invalid_role" response
    Assert.Equal(before, snapshot harness.Root)

// Content delivery

[<Theory; InlineData(false); InlineData(true); Trait("Task", "T034")>]
let ``PATCH maps deletion during role update to 404 but unrelated database errors to safe 500`` (abortUpdate: bool) =
    use harness = TestHarness.Start()
    let library = arrange harness.Root
    // Separate document and projects provide collateral-change sentinels.
    let unrelated = arrange harness.Root
    let documentsBefore, associationsBefore = snapshot harness.Root
    let projectsBefore = readAll harness.Root "SELECT * FROM ResearchProjects ORDER BY Id"
    let workspacesBefore = readAll harness.Root "SELECT * FROM Workspaces ORDER BY Id"
    use connection = new SqliteConnection(WorkspaceDb.connectionString harness.Root)
    connection.Open()
    use command = connection.CreateCommand()
    // Test-local deterministic ordering: the repository reads an existing row,
    // then its UPDATE encounters deletion. No timing or production hooks.
    let triggerBody =
        if abortUpdate then "SELECT RAISE(ABORT, 'role update denied');"
        else $"DELETE FROM ProjectDocuments WHERE ProjectId = '{library.ProjectA:D}' AND DocumentId = '{library.DocumentId:D}'; SELECT RAISE(IGNORE);"
    command.CommandText <- $"CREATE TRIGGER remove_before_role_update BEFORE UPDATE OF Role ON ProjectDocuments WHEN OLD.ProjectId = '{library.ProjectA:D}' AND OLD.DocumentId = '{library.DocumentId:D}' BEGIN {triggerBody} END"
    command.ExecuteNonQuery() |> ignore
    use response = patch harness.Client $"/api/projects/{library.ProjectA:D}/documents/{library.DocumentId:D}" """{"role":"ReferenceSource"}"""
    let documentsAfter, associationsAfter = snapshot harness.Root
    let expectedAssociations =
        associationsBefore
        |> List.filter (fun row -> abortUpdate || row[0] <> library.ProjectA.ToString("D") || row[1] <> library.DocumentId.ToString("D"))
    // Assert actual deletion persistence before checking HTTP mapping: EF must
    // not have rolled the trigger's deletion back after the zero-row update.
    Assert.Equal<string list list>(expectedAssociations, associationsAfter)
    Assert.Equal<string list list>(documentsBefore, documentsAfter)
    Assert.Equal<string list list>(projectsBefore, readAll harness.Root "SELECT * FROM ResearchProjects ORDER BY Id")
    Assert.Equal<string list list>(workspacesBefore, readAll harness.Root "SELECT * FROM Workspaces ORDER BY Id")
    for item in [ library; unrelated ] do
        let path = originalPath harness.Root item
        Assert.Equal<byte array>(item.Bytes, File.ReadAllBytes(path))
        Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(path))
    expectStatus (if abortUpdate then HttpStatusCode.InternalServerError else HttpStatusCode.NotFound) response
    expectCode (if abortUpdate then "internal_error" else "not_found") response
    let text = bodyText response
    for secret in [ harness.Root; library.StorageKey; unrelated.StorageKey; "role update denied"; "DbUpdate"; "SqliteException" ] do
        Assert.DoesNotContain(secret, text)
    if abortUpdate then
        use json = JsonDocument.Parse(text)
        Assert.Equal("An unexpected error occurred.", json.RootElement.GetProperty("title").GetString())

[<Fact; Trait("Task", "T034")>]
let ``GET content returns the exact original inline`` () =
    use harness = TestHarness.Start()
    let library = arrange harness.Root
    use response = getContent harness.Client library.DocumentId None
    expectStatus HttpStatusCode.OK response
    Assert.Equal("application/pdf", response.Content.Headers.ContentType.MediaType)
    Assert.Equal(Nullable(int64 library.Bytes.Length), response.Content.Headers.ContentLength)
    Assert.Contains("bytes", response.Headers.AcceptRanges)
    Assert.Equal("inline", response.Content.Headers.ContentDisposition.DispositionType)
    Assert.Equal<string seq>([ "nosniff" ], response.Headers.GetValues("X-Content-Type-Options"))
    Assert.Equal<byte array>(library.Bytes, bodyBytes response)

[<Fact; Trait("Task", "T034")>]
let ``GET content with a byte range returns that range`` () =
    use harness = TestHarness.Start()
    let library = arrange harness.Root
    use response = getContent harness.Client library.DocumentId (Some (RangeHeaderValue(100L, 199L)))
    expectStatus HttpStatusCode.PartialContent response
    let range = response.Content.Headers.ContentRange
    Assert.Equal((Nullable 100L, Nullable 199L, Nullable 4096L), (range.From, range.To, range.Length))
    Assert.Equal<byte array>(library.Bytes.[100..199], bodyBytes response)

[<Fact; Trait("Task", "T034")>]
let ``GET content with an unsatisfiable range is rejected`` () =
    use harness = TestHarness.Start()
    let library = arrange harness.Root
    use response = getContent harness.Client library.DocumentId (Some (RangeHeaderValue(5000L, Nullable())))
    expectStatus HttpStatusCode.RequestedRangeNotSatisfiable response

[<Fact; Trait("Task", "T034")>]
let ``GET content for an unknown UUID is not found`` () =
    use harness = TestHarness.Start()
    arrange harness.Root |> ignore
    let before = snapshot harness.Root
    use response = getContent harness.Client (Guid.NewGuid()) None
    expectStatus HttpStatusCode.NotFound response
    expectCode "document_not_found" response
    Assert.Equal(before, snapshot harness.Root)

[<Theory; MemberData(nameof unavailableConditions); Trait("Task", "T034")>]
let ``GET content for an unavailable original is 503 with records unchanged and no path disclosed`` (condition: string) =
    use harness = TestHarness.Start()
    let library = arrange harness.Root
    let before = snapshot harness.Root
    makeUnavailable condition (originalPath harness.Root library)
    use response = getContent harness.Client library.DocumentId None
    expectStatus HttpStatusCode.ServiceUnavailable response
    expectCode "document_unavailable" response
    let body = bodyText response
    // Contract wording: one title covers every unavailable condition (T039 review note).
    do
        use json = JsonDocument.Parse(body)
        Assert.Equal("The document's stored file is missing, unreadable or not the recorded size. Its library record and associations are unchanged.",
                     json.RootElement.GetProperty("title").GetString())
    Assert.DoesNotContain(harness.Root, body)
    Assert.DoesNotContain(library.StorageKey, body)
    Assert.Equal(before, snapshot harness.Root)
