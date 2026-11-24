module LexScholar.Server.Tests.LibraryDeletionPreviewTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open LexScholar.Application
open LexScholar.Infrastructure
open LexScholar.Server
open Xunit

let private start () =
    TestHarness.Start(configure = fun app ->
        let configuration = app.Configuration["LexScholar:DataDirectory"]
        LibraryDeletionEndpoints.mapForTests app configuration)

let private postTo (harness: TestHarness) (path: string) body =
    use tokenResponse = harness.Client.GetAsync("/api/antiforgery").Result
    use token = JsonDocument.Parse(tokenResponse.Content.ReadAsStringAsync().Result)
    use request = new HttpRequestMessage(HttpMethod.Post, path)
    request.Headers.Add("Origin", harness.Client.BaseAddress.GetLeftPart(UriPartial.Authority))
    request.Headers.Add("X-CSRF-TOKEN", token.RootElement.GetProperty("requestToken").GetString())
    request.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    harness.Client.Send(request)

let private post harness body = postTo harness "/api/library/deletions/preview" body

let private selection ids = JsonSerializer.Serialize({| documentIds = ids |})

let private seed root =
    use db = WorkspaceDb.create root
    let now = DateTimeOffset.UtcNow
    let workspace, first, second, shared, alone = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = now)) |> ignore
    for id, name in [first, "Projeto A"; second, "Projeto B"] do
        db.Add(ResearchProjectRow(Id = id, WorkspaceId = workspace, Name = name, NormalizedName = name, CreatedUtc = now)) |> ignore
    for id, name in [shared, "Compartilhado.pdf"; alone, "Avulso.pdf"] do
        let key = $"{id:N}.pdf"
        Directory.CreateDirectory(Path.Combine(root, "originals")) |> ignore
        File.WriteAllText(Path.Combine(root, "originals", key), "fixture-only")
        db.Add(LibraryDocumentRow(Id = id, OriginalFileName = name, StorageKey = key, Sha256 = string id, ByteLength = 12L, PageCount = 1, ImportedUtc = now)) |> ignore
    for id, role in [first, "FocusedSource"; second, "ReferenceSource"] do
        db.Add(ProjectDocumentRow(ProjectId = id, DocumentId = shared, Role = role, AddedUtc = now)) |> ignore
    db.Add(DocumentSummaryRow(ProjectId = first, DocumentId = shared, Content = """{"version":1,"text":"SECRET SUMMARY"}""", GeneratedAt = now)) |> ignore
    db.SaveChanges() |> ignore
    first, second, shared, alone

let private preview harness ids =
    use response = post harness (selection ids)
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    JsonDocument.Parse(response.Content.ReadAsStringAsync().Result)

[<Theory; Trait("Task", "T009")>]
[<InlineData("{}")>]
[<InlineData("null")>]
[<InlineData("{")>]
[<InlineData("""{"documentIds":[]}""")>]
[<InlineData("""{"documentIds":["bad"]}""")>]
[<InlineData("""{"documentIds":[null]}""")>]
[<InlineData("""{"documentIds":[123]}""")>]
[<InlineData("""{"documentIds":"bad"}""")>]
[<InlineData("""{"documentIds":["00000000-0000-0000-0000-000000000000"]}""")>]
let ``invalid selection is rejected without disclosing input`` body =
    use harness = start ()
    use response = post harness body
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result)
    Assert.Equal("invalid_selection", json.RootElement.GetProperty("code").GetString())

[<Fact; Trait("Task", "T009")>]
let ``duplicate and unknown IDs are rejected`` () =
    use harness = start ()
    let _, _, shared, _ = seed harness.Root
    for ids, status, code in
        [ [shared; shared], HttpStatusCode.BadRequest, "invalid_selection"
          [shared; Guid.NewGuid()], HttpStatusCode.NotFound, "document_not_found" ] do
        use response = post harness (selection ids)
        Assert.Equal(status, response.StatusCode)
        use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result)
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString())

[<Fact; Trait("Task", "T009")>]
let ``preview shows complete impact without paths summary text or mutation`` () =
    use harness = start ()
    let first, second, shared, alone = seed harness.Root
    let before = File.ReadAllBytes(harness.DatabasePath)
    let files = Directory.GetFiles(Path.Combine(harness.Root, "originals")) |> Array.map (fun path -> path, File.ReadAllBytes(path))
    use result = preview harness [shared; alone]
    let documents = result.RootElement.GetProperty("documents").EnumerateArray() |> Seq.toList
    Assert.Equal(2, documents.Length)
    let item id = documents |> List.find (fun item -> item.GetProperty("id").GetGuid() = id)
    Assert.Equal("Compartilhado.pdf", (item shared).GetProperty("name").GetString())
    Assert.Equal("Avulso.pdf", (item alone).GetProperty("name").GetString())
    Assert.Empty((item alone).GetProperty("projects").EnumerateArray())
    let projects = (item shared).GetProperty("projects").EnumerateArray() |> Seq.toList
    Assert.Equal(2, projects.Length)
    for id, name, role, hasSummary in [first, "Projeto A", "FocusedSource", true; second, "Projeto B", "ReferenceSource", false] do
        let project = projects |> List.find (fun p -> p.GetProperty("id").GetGuid() = id)
        Assert.Equal(name, project.GetProperty("name").GetString())
        Assert.Equal(role, project.GetProperty("role").GetString())
        Assert.Equal(hasSummary, project.GetProperty("hasSummary").GetBoolean())
    let text = result.RootElement.GetRawText()
    Assert.DoesNotContain("SECRET", text)
    Assert.DoesNotContain(harness.Root, text)
    Assert.DoesNotContain("storageKey", text)
    Assert.Equal<byte>(before, File.ReadAllBytes(harness.DatabasePath))
    for path, bytes in files do Assert.Equal<byte>(bytes, File.ReadAllBytes(path))
    use db = WorkspaceDb.create harness.Root
    Assert.Empty(db.Set<DeletionOperationRow>())
    Assert.Empty(db.Set<DeletionItemRow>())

[<Fact; Trait("Task", "T009")>]
let ``revision is stable across selection order and changes with every impact field`` () =
    use harness = start ()
    let first, second, shared, alone = seed harness.Root
    let revision ids =
        use result = preview harness ids
        result.RootElement.GetProperty("revision").GetString()
    let mutable previous = revision [shared; alone]
    Assert.Equal(previous, revision [alone; shared])
    Assert.NotEqual<string>(previous, revision [shared])
    let change mutate =
        use db = WorkspaceDb.create harness.Root
        mutate db
        db.SaveChanges() |> ignore
        let current = revision [shared; alone]
        Assert.NotEqual<string>(previous, current)
        previous <- current
    change (fun db -> db.Set<LibraryDocumentRow>().Find(shared).OriginalFileName <- "Renomeado.pdf")
    change (fun db -> db.Set<ResearchProjectRow>().Find(first).Name <- "Novo nome")
    change (fun db -> db.Set<ProjectDocumentRow>().Find(first, shared).Role <- "ReferenceSource")
    change (fun db -> db.Set<DocumentSummaryRow>().Find(first, shared).Content <- """{"version":1,"text":"REPLACED"}""")
    change (fun db -> db.Set<DocumentSummaryRow>().Find(first, shared).Content <- """{"version":2,"text":"REPLACED"}""")
    change (fun db -> db.Set<DocumentSummaryRow>().Find(first, shared).GeneratedAt <- DateTimeOffset.UtcNow.AddDays(1))
    change (fun db -> db.Remove(db.Set<DocumentSummaryRow>().Find(first, shared)) |> ignore)
    change (fun db -> db.Add(DocumentSummaryRow(ProjectId = second, DocumentId = shared, Content = """{"version":1}""", GeneratedAt = DateTimeOffset.UtcNow)) |> ignore)
    change (fun db -> db.Remove(db.Set<DocumentSummaryRow>().Find(second, shared)) |> ignore)
    change (fun db -> db.Remove(db.Set<ProjectDocumentRow>().Find(second, shared)) |> ignore)

[<Fact; Trait("Task", "T009")>]
let ``impact and revision come from one snapshot while a second connection commits`` () =
    use harness = start ()
    let first, _, shared, alone = seed harness.Root
    let capture () = LibraryDeletion.preview (LibraryDeletionRepository.capture harness.Root) [shared; alone]
    let before = capture ()
    let during =
        LibraryExportSnapshot.withSnapshot harness.Root (fun snapshot ->
            // The first read is established. This second connection commits
            // before the impact queries, without sleeps or timing assumptions.
            use writer = WorkspaceDb.create harness.Root
            writer.Set<LibraryDocumentRow>().Find(shared).OriginalFileName <- "After.pdf"
            writer.Set<ResearchProjectRow>().Find(first).Name <- "After"
            writer.Set<ProjectDocumentRow>().Find(first, shared).Role <- "ReferenceSource"
            writer.Set<DocumentSummaryRow>().Find(first, shared).Content <- """{"version":2,"text":"AFTER"}"""
            writer.Add(ProjectDocumentRow(ProjectId = first, DocumentId = alone, Role = "FocusedSource", AddedUtc = DateTimeOffset.UtcNow)) |> ignore
            writer.SaveChanges() |> ignore
            LibraryDeletion.preview (LibraryDeletionRepository.read snapshot) [shared; alone])
    Assert.Equal(before, during)
    Assert.NotEqual<Result<LibraryDeletion.Preview, LibraryDeletion.PreviewError>>(before, capture ())

[<Fact; Trait("Task", "T017")>]
let ``production preview and confirmation reject malformed bodies`` () =
    use harness = TestHarness.Start()
    for path in ["/api/library/deletions/preview"; "/api/library/deletions"] do
        use response = postTo harness path "{}"
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
