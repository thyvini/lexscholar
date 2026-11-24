module LexScholar.Server.Tests.LibraryExportContractTests

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Text.Json
open Microsoft.Extensions.DependencyInjection
open LexScholar.Application
open LexScholar.Infrastructure
open LexScholar.Server
open Xunit

let private start () =
    TestHarness.Start(configure = fun app ->
        LibraryControlEndpoints.mapForTests app app.Configuration["LexScholar:DataDirectory"] (app.Services.GetRequiredService<LibraryCoordination>()))

let private export (host: TestHarness) = task {
    use! response = host.Client.GetAsync("/api/antiforgery")
    use! body = response.Content.ReadAsStreamAsync()
    use json = JsonDocument.Parse(body)
    use request = new HttpRequestMessage(HttpMethod.Post, "/api/library/export")
    request.Headers.Add("Origin", host.Client.BaseAddress.GetLeftPart(UriPartial.Authority))
    request.Headers.Add("X-CSRF-TOKEN", json.RootElement.GetProperty("requestToken").GetString())
    return! host.Client.SendAsync(request)
}

[<Fact; Trait("Task", "T004")>]
let ``empty internal export is a conflict and public export remains unmapped`` () = task {
    use host = start ()
    use! response = export host
    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)
    use! body = response.Content.ReadAsStreamAsync()
    use json = JsonDocument.Parse(body)
    Assert.Equal("empty_library", json.RootElement.GetProperty("code").GetString())
    use production = TestHarness.Start()
    use! disabled = export production
    Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode)
}

[<Fact; Trait("Task", "T004")>]
let ``internal ZIP preserves shared and unassociated originals with exact v1 metadata and safe paths`` () = task {
    use host = start ()
    let workspace, first, second, unused = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    let shared, alone = Guid.NewGuid(), Guid.NewGuid()
    let imported = DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours 2.)
    let bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
    Directory.CreateDirectory(DocumentStore.originalsDirectory host.Root) |> ignore
    for id in [ shared; alone ] do
        File.WriteAllBytes(Path.Combine(DocumentStore.originalsDirectory host.Root, $"{id:N}"), bytes)
    do
        use db = WorkspaceDb.create host.Root
        db.Add(WorkspaceRow(Id = workspace, CreatedUtc = imported)) |> ignore
        db.SaveChanges() |> ignore
        for id, name in [first, "First"; second, "Second"; unused, "Unused"] do
            db.Add(ResearchProjectRow(Id = id, WorkspaceId = workspace, Name = name, NormalizedName = name.ToUpperInvariant(), CreatedUtc = imported)) |> ignore
        for id, name in [shared, "../../outside\\unsafe.pdf"; alone, "/absolute.pdf"] do
            db.Add(LibraryDocumentRow(Id = id, Sha256 = $"hash-{id}", StorageKey = $"{id:N}", OriginalFileName = name, ByteLength = int64 bytes.Length, PageCount = 1, ImportedUtc = imported)) |> ignore
        db.SaveChanges() |> ignore
        for project, role in [first, "FocusedSource"; second, "ReferenceSource"] do
            db.Add(ProjectDocumentRow(ProjectId = project, DocumentId = shared, Role = role, AddedUtc = imported)) |> ignore
        db.SaveChanges() |> ignore
    use! response = export host
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Equal("application/zip", response.Content.Headers.ContentType.MediaType)
    Assert.Equal("attachment", response.Content.Headers.ContentDisposition.DispositionType)
    use! content = response.Content.ReadAsStreamAsync()
    use zip = new ZipArchive(content, ZipArchiveMode.Read)
    Assert.Equal<string list>(
        ["README.txt"; "manifest.json"; $"pdf/{alone:D}.pdf"; $"pdf/{shared:D}.pdf"] |> List.sort,
        zip.Entries |> Seq.map _.FullName |> Seq.sort |> Seq.toList)
    use readmeStream = zip.GetEntry("README.txt").Open()
    use readme = new StreamReader(readmeStream)
    let explanation = readme.ReadToEnd()
    Assert.Contains("Gerado para sua revisão. Não verificado.", explanation)
    Assert.Contains("contadas a partir de 1", explanation)
    Assert.Contains("não oferece restauração no LexScholar", explanation)
    for id in [shared; alone] do
        use source = zip.GetEntry($"pdf/{id:D}.pdf").Open()
        use actual = new MemoryStream()
        source.CopyTo(actual)
        Assert.Equal<byte>(bytes, actual.ToArray())
    use manifest = zip.GetEntry("manifest.json").Open()
    use json = JsonDocument.Parse(manifest)
    let root = json.RootElement
    let fields (element: JsonElement) = element.EnumerateObject() |> Seq.map _.Name |> Seq.sort |> Seq.toList
    Assert.Equal<string list>(["associations"; "documents"; "format"; "projects"; "version"], fields root)
    Assert.Equal("lexscholar-library", root.GetProperty("format").GetString())
    Assert.Equal(1, root.GetProperty("version").GetInt32())
    let documents = root.GetProperty("documents").EnumerateArray() |> Seq.toList
    Assert.Equal(2, documents.Length)
    for item in documents do
        Assert.Equal<string list>(["byteLength"; "id"; "importedUtc"; "originalFileName"; "pageCount"; "pdfPath"; "sha256"], fields item)
        let id = item.GetProperty("id").GetGuid()
        Assert.Equal($"pdf/{id:D}.pdf", item.GetProperty("pdfPath").GetString())
        Assert.Equal((if id = shared then "../../outside\\unsafe.pdf" else "/absolute.pdf"), item.GetProperty("originalFileName").GetString())
        Assert.Equal(int64 bytes.Length, item.GetProperty("byteLength").GetInt64())
        Assert.Equal(1, item.GetProperty("pageCount").GetInt32())
        Assert.Equal($"hash-{id}", item.GetProperty("sha256").GetString())
        Assert.Equal(imported.UtcDateTime, item.GetProperty("importedUtc").GetDateTime())
        Assert.EndsWith("Z", item.GetProperty("importedUtc").GetString())
    let projects = root.GetProperty("projects").EnumerateArray() |> Seq.toList
    Assert.Equal(2, projects.Length)
    for item in projects do
        Assert.Equal<string list>(["createdUtc"; "id"; "name"], fields item)
        Assert.Equal((if item.GetProperty("id").GetGuid() = first then "First" else "Second"), item.GetProperty("name").GetString())
        Assert.Equal(imported.UtcDateTime, item.GetProperty("createdUtc").GetDateTime())
    let associations = root.GetProperty("associations").EnumerateArray() |> Seq.toList
    Assert.Equal(2, associations.Length)
    for item in associations do
        Assert.Equal<string list>(["addedUtc"; "documentId"; "projectId"; "role"; "summary"], fields item)
        Assert.Equal(shared, item.GetProperty("documentId").GetGuid())
        Assert.Equal((if item.GetProperty("projectId").GetGuid() = first then "FocusedSource" else "ReferenceSource"), item.GetProperty("role").GetString())
        Assert.Equal(imported.UtcDateTime, item.GetProperty("addedUtc").GetDateTime())
        Assert.Equal(JsonValueKind.Null, item.GetProperty("summary").ValueKind)
    Assert.Equal(2, (LibraryRepository.list host.Root).Length)
    Assert.Empty(Directory.GetFiles(Path.Combine(host.Root, "exports")))
    // T005 now includes stored summaries; public success remains gated at T008.
    SummaryRepository.save host.Root { Project = first; Document = shared }
        { DocumentId = shared; Points = []; PagesCovered = [1]; UnreadablePages = []
          OmittedPoints = 0; GeneratedAt = imported; ServiceHost = "local"; Model = "fixture" }
    use! withSummary = export host
    Assert.Equal(HttpStatusCode.OK, withSummary.StatusCode)
    Assert.Equal("application/zip", withSummary.Content.Headers.ContentType.MediaType)
    Assert.Empty(Directory.GetFiles(Path.Combine(host.Root, "exports")))
}
