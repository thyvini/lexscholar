module LexScholar.Browser.Tests.LibraryExportJourneyTests

open System
open System.IO
open System.IO.Compression
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Playwright
open Xunit
open LexScholar.Client
open LexScholar.Infrastructure
open LexScholar.Server.Tests
open LexScholar.Server
open LexScholar.Application

type private BrokenTransfer() =
    inherit Stream()
    let mutable position = 0L
    override _.CanRead = true
    override _.CanSeek = true
    override _.CanWrite = false
    override _.Length = 4096L
    override _.Position with get () = position and set value = position <- value
    override _.Seek(offset, _) = position <- offset; position
    override _.Flush() = ()
    override _.SetLength(_) = raise (NotSupportedException())
    override _.Write(_, _, _) = raise (NotSupportedException())
    override _.Read(buffer, offset, _) =
        if position > 0L then raise (IOException("Injected transfer interruption"))
        Array.Fill<byte>(buffer, 1uy, offset, 8)
        position <- 8L
        8
    override this.ReadAsync(buffer: Memory<byte>, _: CancellationToken) =
        if position > 0L then ValueTask.FromException<int>(IOException("Injected transfer interruption"))
        else
            buffer.Span.Slice(0, 8).Fill(1uy)
            position <- 8L
            ValueTask<int>(8)

let private exportButton (page: IPage) =
    page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Exportar biblioteca", Exact = true))

let private openLibrary browser host = task {
    let! page = Ui.openApp browser host
    do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
    return page
}

let private seed root =
    let document = Guid.NewGuid()
    let bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
    DocumentStore.ensureDirectories root
    File.WriteAllBytes(Path.Combine(DocumentStore.originalsDirectory root, $"{document:N}"), bytes)
    use db = WorkspaceDb.create root
    db.Add(LibraryDocumentRow(Id = document, Sha256 = $"{document:N}", StorageKey = $"{document:N}",
        OriginalFileName = "source.pdf", ByteLength = int64 bytes.Length, PageCount = 1,
        ImportedUtc = DateTimeOffset.UtcNow)) |> ignore
    db.SaveChanges() |> ignore
    document, bytes

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T008"); Trait("Category", "Browser")>]
let ``public export downloads a complete local archive with no credentials or settings`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use host = TestHarness.Start(serveClient = true)
        let document, bytes = seed host.Root
        let shared, _ = seed host.Root
        let workspace, first, second = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
        let now = DateTimeOffset.UtcNow
        do
            use db = WorkspaceDb.create host.Root
            db.Add(WorkspaceRow(Id = workspace, CreatedUtc = now)) |> ignore
            db.SaveChanges() |> ignore
            for id, name in [ first, "Primeiro"; second, "Segundo" ] do
                db.Add(ResearchProjectRow(Id = id, WorkspaceId = workspace, Name = name,
                    NormalizedName = name.ToUpperInvariant(), CreatedUtc = now)) |> ignore
            db.SaveChanges() |> ignore
            for id, role in [first, "FocusedSource"; second, "ReferenceSource"] do
                db.Add(ProjectDocumentRow(ProjectId = id, DocumentId = shared, Role = role, AddedUtc = now)) |> ignore
            db.SaveChanges() |> ignore
        for project, point in [first, "Primeiro ponto"; second, "Segundo ponto"] do
            SummaryRepository.save host.Root { Project = project; Document = shared }
                { DocumentId = shared; Points = [{ Text = point; Pages = [1] }]; PagesCovered = [1]
                  UnreadablePages = []; OmittedPoints = 0; GeneratedAt = now; ServiceHost = "local"; Model = "fixture" }
        File.WriteAllText(Path.Combine(host.Root, "chatgpt-signin.json"), "credential-canary")
        File.WriteAllText(Path.Combine(host.Root, "unrelated-settings.json"), "settings-canary")
        let! page = openLibrary browser host
        let external = ResizeArray<string>()
        page.Request.Add(fun request ->
            if (Uri request.Url).Host <> "127.0.0.1" && (Uri request.Url).Host <> "localhost" then
                external.Add(request.Url))
        do! Assertions.Expect(page.GetByText("Inclui todos os PDFs, projetos, vínculos, papéis e resumos salvos. Os resumos são gerados para revisão, não verificados.")).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText("Para uso fora do LexScholar. Não há restauração no aplicativo. Credenciais e configurações não são incluídas.")).ToBeVisibleAsync()
        let downloadTask = page.WaitForDownloadAsync()
        do! Ui.click (exportButton page)
        let! download = downloadTask
        Assert.Equal("lexscholar-library.zip", download.SuggestedFilename)
        let! failure = download.FailureAsync()
        Assert.Null(failure)
        let! path = download.PathAsync()
        use zip = ZipFile.OpenRead(path)
        Assert.Equal<string list>(["README.txt"; "manifest.json"; $"pdf/{document:D}.pdf"; $"pdf/{shared:D}.pdf";
            $"summaries/{first:D}/{shared:D}.txt"; $"summaries/{second:D}/{shared:D}.txt"] |> List.sort,
            zip.Entries |> Seq.map _.FullName |> Seq.sort |> Seq.toList)
        use source = zip.GetEntry($"pdf/{document:D}.pdf").Open()
        use actual = new MemoryStream()
        source.CopyTo(actual)
        Assert.Equal<byte>(bytes, actual.ToArray())
        use manifest = zip.GetEntry("manifest.json").Open()
        use json = JsonDocument.Parse(manifest)
        Assert.Equal(2, json.RootElement.GetProperty("documents").GetArrayLength())
        Assert.Equal(2, json.RootElement.GetProperty("associations").GetArrayLength())
        for item in json.RootElement.GetProperty("associations").EnumerateArray() do
            let project = item.GetProperty("projectId").GetGuid()
            Assert.Equal((if project = first then "FocusedSource" else "ReferenceSource"), item.GetProperty("role").GetString())
            let summary = item.GetProperty("summary")
            let point = summary.GetProperty("points").[0]
            Assert.Equal((if project = first then "Primeiro ponto" else "Segundo ponto"), point.GetProperty("text").GetString())
            Assert.Equal(1, point.GetProperty("pages").[0].GetInt32())
            use reader = new StreamReader(zip.GetEntry(summary.GetProperty("textPath").GetString()).Open())
            let readable = reader.ReadToEnd()
            Assert.Contains("Gerado para sua revisão. Não verificado.", readable)
            Assert.Contains(point.GetProperty("text").GetString(), readable)
        do! Assertions.Expect(page.GetByText("Arquivo recebido. Confira o download no navegador; o LexScholar não confirma o salvamento no disco.")).ToBeVisibleAsync()
        Assert.Empty(external)
        Assert.Equal(2, (LibraryRepository.list host.Root).Length)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T008"); Trait("Category", "Browser")>]
let ``empty library and unavailable original show Portuguese errors without a download`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use host = TestHarness.Start(serveClient = true)
        let! page = openLibrary browser host
        let downloads = ResizeArray<IDownload>()
        page.Download.Add(downloads.Add)
        do! Ui.click (exportButton page)
        do! Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Não há documentos para exportar.")
        let document, _ = seed host.Root
        File.Delete(Path.Combine(DocumentStore.originalsDirectory host.Root, $"{document:N}"))
        do! Ui.click (exportButton page)
        do! Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Não foi possível exportar: documento indisponível — source.pdf.")
        Assert.Empty(downloads)
        Assert.Single(LibraryRepository.list host.Root) |> ignore
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T008"); Trait("Category", "Browser")>]
let ``failed transfer never produces download success and permits retry`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use host = TestHarness.Start(serveClient = true)
        seed host.Root |> ignore
        let! page = openLibrary browser host
        let downloads = ResizeArray<IDownload>()
        page.Download.Add(downloads.Add)
        use! _ = page.RouteAsync("**/api/library/export", fun route -> route.AbortAsync("connectionreset"))
        do! Ui.click (exportButton page)
        do! Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Não foi possível concluir a exportação. Tente novamente. Um download incompleto não é uma exportação completa.")
        do! Assertions.Expect(exportButton page).ToBeEnabledAsync()
        do! Assertions.Expect(page.GetByText("Arquivo recebido. Confira o download no navegador; o LexScholar não confirma o salvamento no disco.")).ToHaveCountAsync(0)
        Assert.Empty(downloads)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T008"); Trait("Category", "Browser")>]
let ``an interrupted successful HTTP body is not offered as a complete archive`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use host = TestHarness.Start(serveClient = true, configure = fun app ->
            LibraryControlEndpoints.mapWithBuildForTests app (app.Services.GetRequiredService<LibraryCoordination>())
                (fun _ -> Ok (new BrokenTransfer() :> Stream)))
        let! page = openLibrary browser host
        let downloads = ResizeArray<IDownload>()
        page.Download.Add(downloads.Add)
        do! Ui.click (exportButton page)
        do! Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Não foi possível concluir a exportação. Tente novamente. Um download incompleto não é uma exportação completa.")
        Assert.Empty(downloads)
    })
