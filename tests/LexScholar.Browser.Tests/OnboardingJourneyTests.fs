module LexScholar.Browser.Tests.OnboardingJourneyTests

open System
open Microsoft.Playwright
open Xunit
open LexScholar.Client
open LexScholar.Server.Tests

let private openFirstRun (browser: IBrowser) (harness: TestHarness) = task {
    let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
    let! page = context.NewPageAsync()
    let! _ = page.GotoAsync("/")
    do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
    return page
}

let private noMenus (page: IPage) = task {
    do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(0)
    do! Assertions.Expect(page.GetByText(Texts.chooseDocument, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
}

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``onboarding stays through project import sign-in and reload until finish`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi()
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! page = openFirstRun browser harness
        do! noMenus page
        do! page.GetByLabel(Texts.projectName).FillAsync("Dissertação")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Dissertação", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! noMenus page
        let! _ = page.ReloadAsync()
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! page.GetByLabel(Texts.choosePdfs).SetInputFilesAsync(IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
        do! Assertions.Expect(page.GetByText(Texts.imported "valid.pdf", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Ui.signInWithChatGpt page harness fake
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText(Texts.signedIn, PageGetByTextOptions(Exact = true)).First).ToBeVisibleAsync()
        do! noMenus page
        do! Ui.click (Ui.button (page.Locator("body")) Texts.finishOnboarding)
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        let! _ = page.ReloadAsync()
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        do! Assertions.Expect(page.GetByText(Texts.welcome, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``onboarding can skip optional setup and finish with no project`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openFirstRun browser harness
        do! noMenus page
        do! Ui.click (Ui.button (page.Locator("body")) Texts.skipOnboarding)
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        let! _ = page.ReloadAsync()
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``project setup can finish without import or sign-in and existing workspaces bypass onboarding`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openFirstRun browser harness
        do! page.GetByLabel(Texts.projectName).FillAsync("Pesquisa")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! noMenus page
        do! Ui.click (Ui.button (page.Locator("body")) Texts.finishOnboarding)
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        // A fresh browser session has no onboarding marker, but the workspace has a project.
        let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
        let! another = context.NewPageAsync()
        let! _ = another.GotoAsync("/")
        do! Assertions.Expect(another.Locator(".workspace[data-ready='True']")).ToBeVisibleAsync()
        do! Assertions.Expect(another.GetByText(Texts.welcome, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        do! Assertions.Expect(another.Locator("ul.projects li")).ToContainTextAsync("Pesquisa")
    })
