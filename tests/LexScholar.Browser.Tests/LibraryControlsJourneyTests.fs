module LexScholar.Browser.Tests.LibraryControlsJourneyTests

open System
open System.IO
open System.IO.Compression
open System.Threading.Tasks
open Microsoft.Playwright
open LexScholar.Client
open LexScholar.Infrastructure
open LexScholar.Server.Tests
open Xunit

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T018"); Trait("Category", "Browser")>]
let ``export preview cancel confirm and acknowledge stay on the workspace origin`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use host = TestHarness.Start(serveClient = true)
        let document = Guid.NewGuid()
        let bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
        DocumentStore.ensureDirectories host.Root
        File.WriteAllBytes(Path.Combine(DocumentStore.originalsDirectory host.Root, $"{document:N}"), bytes)
        do
            use db = WorkspaceDb.create host.Root
            db.Add(LibraryDocumentRow(Id = document, OriginalFileName = "local-fixture.pdf", StorageKey = $"{document:N}",
                Sha256 = "fixture", ByteLength = int64 bytes.Length, PageCount = 1, ImportedUtc = DateTimeOffset.UtcNow)) |> ignore
            db.SaveChanges() |> ignore
        File.WriteAllText(Path.Combine(host.Root, "chatgpt-signin.json"), "BROWSER-CREDENTIAL-T018")
        File.WriteAllText(Path.Combine(host.Root, "appsettings.json"), "BROWSER-SETTINGS-T018")
        let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = string host.Client.BaseAddress))
        use _ = context
        let foreign = System.Collections.Concurrent.ConcurrentQueue<string>()
        // Observe the whole context, including startup and any new pages. Abort
        // off-origin requests so a regression cannot send fixture data externally.
        use! _ = context.RouteAsync("**/*", Func<IRoute, Task>(fun route ->
            if (Uri route.Request.Url).GetLeftPart(UriPartial.Authority) <> host.Client.BaseAddress.GetLeftPart(UriPartial.Authority) then
                foreign.Enqueue(route.Request.Url)
                route.AbortAsync()
            else route.ContinueAsync()))
        let! page = context.NewPageAsync()
        let! _ = page.GotoAsync("/")
        do! Ui.enterWorkspace page
        do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
        let button name = page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = name, Exact = true))
        let downloadTask = page.WaitForDownloadAsync()
        do! Ui.click (button "Exportar biblioteca")
        let! download = downloadTask
        let! failure = download.FailureAsync()
        Assert.Null(failure)
        let! path = download.PathAsync()
        use zip = ZipFile.OpenRead(path)
        Assert.Equal<string list>(["README.txt"; "manifest.json"; $"pdf/{document:D}.pdf"],
            zip.Entries |> Seq.map _.FullName |> Seq.sort |> Seq.toList)
        for entry in zip.Entries do
            use reader = new StreamReader(entry.Open())
            let text = reader.ReadToEnd()
            for secret in ["BROWSER-CREDENTIAL-T018"; "BROWSER-SETTINGS-T018"; host.Root] do
                Assert.DoesNotContain(secret, text)
        do! page.GetByRole(AriaRole.Checkbox,
            PageGetByRoleOptions(Name = $"{Texts.deletionSelection}: local-fixture.pdf", Exact = true)).CheckAsync()
        do! Ui.click (button Texts.deleteSelection)
        do! Assertions.Expect(page.GetByText(Texts.deletionCopies)).ToBeVisibleAsync()
        do! Ui.click (button Texts.deletionCancel)
        Assert.Single(LibraryRepository.list host.Root) |> ignore
        Assert.True(File.Exists(Path.Combine(DocumentStore.originalsDirectory host.Root, $"{document:N}")))
        do! Ui.click (button Texts.deleteSelection)
        do! Ui.click (button Texts.deletionConfirm)
        do! Assertions.Expect(page.GetByText(Texts.deletionComplete, PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Ui.click (button Texts.deletionAcknowledge)
        do! Assertions.Expect(page.GetByText(Texts.deletionComplete, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        Assert.Empty(LibraryRepository.list host.Root)
        Assert.False(File.Exists(Path.Combine(DocumentStore.originalsDirectory host.Root, $"{document:N}")))
        Assert.Empty(foreign)
    })
