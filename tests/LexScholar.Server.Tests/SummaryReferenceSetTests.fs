/// Reference-set checks for the summary endpoint (F003: SC-001, SC-005, FR-013).
module LexScholar.Server.Tests.SummaryReferenceSetTests

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

let private token (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    json.RootElement.GetProperty("requestToken").GetString()

let private message (client: HttpClient) (method: HttpMethod) (path: string) (content: HttpContent option) =
    let request = new HttpRequestMessage(method, path)
    request.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token client) |> ignore
    content |> Option.iter (fun body -> request.Content <- body)
    request

let private send client method path content =
    use request = message client method path content
    use response = (client: HttpClient).Send(request)
    response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

let private json (text: string) = new StringContent(text, Encoding.UTF8, "application/json")

let private local = [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ]
let private elsewhere = [ "LexScholar:Summary:Endpoint", "http://summary.example.test:8080/v1"; "LexScholar:Summary:Model", "gpt" ]

let private seedProject directory =
    use db = WorkspaceDb.create directory
    let time = DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero)
    let workspace, project = Guid.NewGuid(), Guid.NewGuid()
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    db.Add(ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = "P", NormalizedName = "P", CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    project

/// Imports a reference PDF and associates it with the project; returns the document id.
let private addDocument (client: HttpClient) (project: Guid) (name: string) =
    let part = new ByteArrayContent(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", name)))
    part.Headers.ContentType <- MediaTypeHeaderValue("application/pdf")
    let form = new MultipartFormDataContent()
    form.Add(part, "file", name)
    let status, text = send client HttpMethod.Post "/api/documents" (Some (form :> HttpContent))
    Assert.True(status = HttpStatusCode.Created || status = HttpStatusCode.OK, text)
    use created = JsonDocument.Parse(text)
    let document = created.RootElement.GetProperty("id").GetGuid()
    let body = JsonSerializer.Serialize({| documentId = string document; role = "FocusedSource" |})
    let status, _ = send client HttpMethod.Post $"/api/projects/{project}/documents" (Some (json body))
    Assert.Equal(HttpStatusCode.Created, status)
    document

let private summaryPath (project: Guid) (document: Guid) = $"/api/projects/{project}/documents/{document}/summary"

let private events (text: string) =
    text.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun line -> JsonDocument.Parse(line).RootElement.Clone())

let private ints (element: JsonElement) = [ for p in element.EnumerateArray() -> p.GetInt32() ]

/// Valid, out-of-range, unreadable (2, 5 in partial.pdf) and zero citations.
let private citations =
    [ "ok", [ 1 ]; "gone", [ 999 ]; "blank", [ 2 ]; "mixed", [ 1; 5 ]; "zero", [ 0 ] ]

let private citationAnswer =
    JsonSerializer.Serialize({| points = [ for text, pages in citations -> {| text = text; pages = pages |} ] |})

[<Theory; Trait("Task", "T011")>]
[<InlineData("article-en.pdf", 12)>]
[<InlineData("article-pt.pdf", 8)>]
[<InlineData("partial.pdf", 6)>]
[<InlineData("long-40.pdf", 40)>]
let ``every shown page exists and was readable, and the omitted points are counted`` (name: string, pageCount: int) =
    let fake = new FakeChatService(Reply citationAnswer)
    use harness = TestHarness.Start(settings = local, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project name
    let status, text = send harness.Client HttpMethod.Post (summaryPath project document) (Some (json """{"confirmSend":false}"""))
    Assert.Equal(HttpStatusCode.OK, status)
    let summary = Assert.Single(events text |> Array.filter (fun e -> e.GetProperty("type").GetString() = "summary"))
    let covered = ints (summary.GetProperty("pagesCovered"))
    let unreadable = ints (summary.GetProperty("unreadablePages"))
    let shown = [ for point in summary.GetProperty("points").EnumerateArray() do yield! ints (point.GetProperty("pages")) ]
    Assert.NotEmpty(shown)
    Assert.All(shown, fun page ->
        Assert.InRange(page, 1, pageCount)
        Assert.Contains(page, covered)
        Assert.DoesNotContain(page, unreadable))
    Assert.Equal<int list>([ 1 .. pageCount ], List.sort (covered @ unreadable))
    let omitted = summary.GetProperty("omittedPoints").GetInt32()
    if name = "long-40.pdf" then
        Assert.True(omitted >= 1, "out-of-range citations are omitted in every chunk")
    else
        // One service call: every point citing a page outside the covered pages is omitted.
        let expected = citations |> List.filter (fun (_, pages) -> pages |> List.exists (fun p -> not (List.contains p covered))) |> List.length
        Assert.Equal(expected, omitted)
        Assert.Equal(citations.Length - expected, summary.GetProperty("points").GetArrayLength())
    if name = "partial.pdf" then Assert.Equal<int list>([ 2; 5 ], unreadable)

[<Theory; Trait("Task", "T011")>]
[<InlineData("scanned.pdf", "no_text")>]
[<InlineData("little-text.pdf", "not_enough_text")>]
[<InlineData("too-long.pdf", "too_many_pages")>]
let ``documents without enough text are refused locally and the service is never called`` (name: string, code: string) =
    let fake = new FakeChatService(Reply citationAnswer)
    use harness = TestHarness.Start(settings = local, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = addDocument harness.Client project name
    let status, text = send harness.Client HttpMethod.Post (summaryPath project document) (Some (json """{"confirmSend":false}"""))
    Assert.Equal(enum<HttpStatusCode> 422, status)
    use problem = JsonDocument.Parse(text)
    Assert.Equal(code, problem.RootElement.GetProperty("code").GetString())
    Assert.Empty(fake.Requests)

/// Every database row of the library and project tables, and the hash of every stored original.
let private snapshot (root: string) =
    use db = WorkspaceDb.create root
    let rows =
        [ for d in db.Set<LibraryDocumentRow>() |> Seq.toList |> List.sortBy (fun d -> d.Id) ->
              $"doc {d.Id} {d.Sha256} {d.StorageKey} {d.OriginalFileName} {d.ByteLength} {d.PageCount} {d.ImportedUtc:O}"
          for p in db.Set<ProjectDocumentRow>() |> Seq.toList |> List.sortBy (fun p -> p.ProjectId, p.DocumentId) ->
              $"link {p.ProjectId} {p.DocumentId} {p.Role} {p.AddedUtc:O}"
          for p in db.Set<ResearchProjectRow>() |> Seq.toList -> $"project {p.Id} {p.WorkspaceId} {p.Name} {p.NormalizedName} {p.CreatedUtc:O}"
          for w in db.Set<WorkspaceRow>() |> Seq.toList -> $"workspace {w.Id} {w.ActiveProjectId} {w.CreatedUtc:O}" ]
    let originals =
        [ for file in Directory.GetFiles(DocumentStore.originalsDirectory root) |> Array.sort ->
              $"{Path.GetFileName file} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes file))}" ]
    Assert.NotEmpty(originals)
    rows, originals

let private importedAll (client: HttpClient) project =
    [ for name in [ "article-en.pdf"; "partial.pdf"; "scanned.pdf" ] -> addDocument client project name ]

[<Fact; Trait("Task", "T011")>]
let ``requesting a summary changes no database row and no original`` () =
    let fake = new FakeChatService(Reply citationAnswer)
    use harness = TestHarness.Start(settings = local, summaryHandler = fake)
    let project = seedProject harness.Root
    let documents = importedAll harness.Client project
    let before = snapshot harness.Root
    for document in documents do
        send harness.Client HttpMethod.Post (summaryPath project document) (Some (json """{"confirmSend":false}""")) |> ignore
    Assert.NotEmpty(fake.Requests)
    Assert.Equal(before, snapshot harness.Root)

[<Fact; Trait("Task", "T011")>]
let ``declining and confirming the send to another host change no database row and no original`` () =
    let fake = new FakeChatService(Reply citationAnswer)
    use harness = TestHarness.Start(settings = elsewhere, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = List.head (importedAll harness.Client project)
    let before = snapshot harness.Root
    // Declined: the client never confirms, so the only request is the unconfirmed one.
    let status, _ = send harness.Client HttpMethod.Post (summaryPath project document) (Some (json """{"confirmSend":false}"""))
    Assert.Equal(HttpStatusCode.Conflict, status)
    Assert.Empty(fake.Requests)
    Assert.Equal(before, snapshot harness.Root)
    // Confirmed.
    let confirmed = """{"confirmSend":true,"confirmedHost":"summary.example.test:8080","confirmedModel":"gpt"}"""
    let status, _ = send harness.Client HttpMethod.Post (summaryPath project document) (Some (json confirmed))
    Assert.Equal(HttpStatusCode.OK, status)
    Assert.NotEmpty(fake.Requests)
    Assert.Equal(before, snapshot harness.Root)

[<Fact; Trait("Task", "T011")>]
let ``abandoning a summary changes no database row and no original`` () =
    let fake = new FakeChatService(fun n -> if n = 1 then Reply citationAnswer else WaitUntilCancelled)
    use workspace = TemporaryWorkspace.Create()
    let before =
        use harness = TestHarness.Start(settings = local, summaryHandler = fake, workspace = workspace)
        let project = seedProject harness.Root
        let document = addDocument harness.Client project "long-40.pdf"
        let before = snapshot harness.Root
        use abandon = new Threading.CancellationTokenSource()
        use request = message harness.Client HttpMethod.Post (summaryPath project document) (Some (json """{"confirmSend":false}""" :> HttpContent))
        let response = harness.Client.Send(request, HttpCompletionOption.ResponseHeadersRead, abandon.Token)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        fake.Waiting.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()
        abandon.Cancel()
        response.Dispose()
        fake.Cancelled.WaitAsync(TimeSpan.FromSeconds 30.).GetAwaiter().GetResult()
        before // disposing the host waits for the abandoned request to end
    Assert.Equal(before, snapshot workspace.Root)
    Assert.Equal(2, fake.Requests.Length)
