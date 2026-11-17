module LexScholar.Browser.Tests.SharedPiecesJourneyTests

open System
open System.IO
open System.Collections.Concurrent
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Client
open LexScholar.Server.Tests
open LexScholar.Browser.Tests.Ui

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T001"); Trait("Category", "Browser")>]
let ``the app declares Brazilian Portuguese`` (browserName: string) : Task = task {
    use! playwright = Playwright.CreateAsync()
    use! browser = playwright.[browserName].LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
    use harness = TestHarness.Start(serveClient = true)
    let! page = browser.NewPageAsync()
    let! _ = page.GotoAsync(harness.Client.BaseAddress.ToString())
    do! Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "pt-BR")
}

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T001"); Trait("Category", "Browser")>]
let ``fonts are served locally and fake sign-in shows the current account`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi()
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
        let! page = context.NewPageAsync()
        let urls = ConcurrentBag<string>()
        page.Request.Add(fun request -> urls.Add(request.Url))
        let! _ = page.GotoAsync("/")
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.projects, Exact = true)).First).ToBeVisibleAsync()
        let! loaded = page.EvaluateAsync<bool>("async () => { await Promise.all([document.fonts.load('16px Inter'), document.fonts.load('16px \"Source Serif 4\"')]); return document.fonts.check('16px Inter') && document.fonts.check('16px \"Source Serif 4\"'); }")
        Assert.True(loaded)
        Assert.Contains(urls, fun url -> url.Contains("/fonts/inter/InterVariable.woff2"))
        Assert.Contains(urls, fun url -> url.Contains("/fonts/source-serif/SourceSerif4Variable.woff2"))
        Assert.All(urls, fun url -> Assert.Equal("127.0.0.1", Uri(url).Host))
        do! page.GetByLabel(Texts.projectName).FillAsync("Alpha")
        do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = Texts.createProject)).ClickAsync()
        let project = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = "Alpha"))
        do! click (button project Texts.select)
        do! Assertions.Expect(project.Locator("strong.active")).ToHaveTextAsync(Texts.active)
        do! click (button (page.Locator("body")) Texts.library)
        do! Assertions.Expect(page.GetByLabel("Import a PDF")).ToBeVisibleAsync()
        do! page.GetByLabel("Import a PDF").SetInputFilesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
        do! Assertions.Expect(page.GetByText("Imported valid.pdf.")).ToBeVisibleAsync()
        let document = page.Locator("ul.documents li").Filter(LocatorFilterOptions(HasText = "valid.pdf"))
        do! click (button document "Add as Focused Source")
        let associated = page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "valid.pdf"))
        do! click (button associated "Open")
        do! signInWithChatGpt page harness fake
        do! click (button associated "Open")
        do! Assertions.Expect(page.Locator(".chatgpt-account")).ToContainTextAsync("Signed in to ChatGPT as researcher@example.test")
    })
