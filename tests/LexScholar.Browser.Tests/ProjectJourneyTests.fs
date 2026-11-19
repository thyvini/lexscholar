module LexScholar.Browser.Tests.ProjectJourneyTests

open System
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Server.Tests
open LexScholar.Client

// Real-browser journeys against an isolated in-process host serving the
// WebAssembly client. Browsers are Playwright's pinned builds; install them
// once with scripts/install-browsers.sh.

/// Sets Playwright's default assertion timeout once per test run. WebAssembly
/// boot can exceed the 5-second default, especially in Firefox. An assembly
/// fixture is used because a module-level `do` is not run in this test
/// executable, so the setting would be silently ignored.
type PlaywrightDefaults() =
    do Assertions.SetDefaultExpectTimeout(30_000.0f)

[<assembly: AssemblyFixture(typeof<PlaywrightDefaults>)>]
do ()

let private withBrowser = Ui.withBrowser

let private startHost (workspace: TemporaryWorkspace) =
    TestHarness.Start(serveClient = true, workspace = workspace)

let private openApp (browser: IBrowser) (harness: TestHarness) = task {
    let! page = Ui.openApp browser harness
    return page.Context, page }

let private row (page: IPage) (name: string) =
    page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = name))

let private emptyState (page: IPage) =
    page.GetByText(Texts.noProjects)

let private createProject (page: IPage) (name: string) = task {
    do! page.GetByLabel(Texts.projectName).FillAsync(name)
    do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
    do! Assertions.Expect(row page name).ToBeVisibleAsync() }

let private expectActive (page: IPage) (name: string) = task {
    do! Assertions.Expect(row page name).ToHaveCountAsync(1)
    do! Assertions.Expect((row page name).Locator("strong.active")).ToHaveTextAsync(Texts.active)
    do! Assertions.Expect(page.Locator("strong.active")).ToHaveCountAsync(1) }

let private expectSelectable (page: IPage) (name: string) =
    Assertions.Expect(Ui.button (row page name) Texts.select).ToBeVisibleAsync()

let private select (page: IPage) (name: string) =
    Ui.click (Ui.button (row page name) Texts.select)

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T009"); Trait("Category", "Browser")>]
let ``first run introduces the three steps`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use workspace = TemporaryWorkspace.Create()
        use harness = startHost workspace
        let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
        let! page = context.NewPageAsync()
        let! _ = page.GotoAsync("/")
        do! Assertions.Expect(page.GetByText("Boas-vindas ao LexScholar", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText("Crie seu primeiro projeto", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText("Importe seus PDFs", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText("Entre para gerar resumos", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText("Requer um plano ChatGPT Plus ou Pro.", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(Ui.button (page.Locator("body")) Texts.signIn).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByLabel(Texts.choosePdfs)).ToHaveCountAsync(1)
        do! Assertions.Expect(Ui.button (page.Locator("body")) Texts.signIn).ToBeVisibleAsync()
        do! page.GetByLabel(Texts.projectName).FillAsync("Alpha")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Alpha", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(Ui.button (page.Locator("body")) Texts.signIn).ToBeVisibleAsync() })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T019"); Trait("Task", "T002"); Trait("Category", "Browser")>]
let ``empty workspace shows the project-list empty state`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use workspace = TemporaryWorkspace.Create()
        use harness = startHost workspace
        let! _, page = openApp browser harness
        do! Assertions.Expect(emptyState page).ToBeVisibleAsync()
        do! Assertions.Expect(page.Locator("ul.projects li")).ToHaveCountAsync(0)
        do! Assertions.Expect(page.Locator("strong.active")).ToHaveCountAsync(0) })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T019"); Trait("Task", "T002"); Trait("Category", "Browser")>]
let ``creating and selecting projects shows the active project`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use workspace = TemporaryWorkspace.Create()
        use harness = startHost workspace
        let! _, page = openApp browser harness
        do! Assertions.Expect(emptyState page).ToBeVisibleAsync()
        do! createProject page "Alpha"
        do! createProject page "Beta"
        do! Assertions.Expect(page.Locator("ul.projects li")).ToHaveCountAsync(2)
        do! Assertions.Expect(emptyState page).ToHaveCountAsync(0)
        // Creation does not activate a project.
        do! Assertions.Expect(page.Locator("strong.active")).ToHaveCountAsync(0)
        do! expectSelectable page "Alpha"
        do! expectSelectable page "Beta"

        do! select page "Beta"
        do! expectActive page "Beta"
        do! expectSelectable page "Alpha"

        do! select page "Alpha"
        do! expectActive page "Alpha"
        do! expectSelectable page "Beta" })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T019"); Trait("Task", "T002"); Trait("Category", "Browser")>]
let ``restart over the same data directory restores the active project`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use workspace = TemporaryWorkspace.Create()
        do!
            task {
                use harness = startHost workspace
                let! context, page = openApp browser harness
                do! createProject page "Alpha"
                do! createProject page "Beta"
                do! select page "Beta"
                do! expectActive page "Beta"
                do! context.CloseAsync()
            }
        // The first host is stopped and its data-directory lock released here.
        use restarted = startHost workspace
        let! _, page = openApp browser restarted
        do! Assertions.Expect(page.Locator("ul.projects li")).ToHaveCountAsync(2)
        do! expectSelectable page "Alpha"
        do! expectActive page "Beta" })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T002"); Trait("Category", "Browser")>]
let ``top bar exposes the signed-in account and sign-out`` (browserName: string) =
    Ui.withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi()
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! page = Ui.openApp browser harness
        let bar = page.Locator(".top-bar")
        do! Assertions.Expect(bar.GetByText(Texts.signedIn, LocatorGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        do! createProject page "Alpha"
        do! select page "Alpha"
        do! expectActive page "Alpha"
        do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
        do! page.GetByLabel(Texts.importPdf).SetInputFilesAsync(IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
        do! Assertions.Expect(page.GetByText((Texts.imported "valid.pdf"))).ToBeVisibleAsync()
        do! Ui.click (Ui.button (page.Locator("ul.documents li")) Texts.addFocused)
        do! Ui.click (Ui.button (page.Locator("ul.project-documents li")) Texts.openDocument)
        do! Assertions.Expect(page.GetByTestId("main").GetByRole(AriaRole.Heading, LocatorGetByRoleOptions(Name = "valid.pdf", Exact = true))).ToBeVisibleAsync()
        do! Ui.signInWithChatGpt page harness fake
        do! Assertions.Expect(bar.GetByText(Texts.signedIn, LocatorGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! bar.GetByText(Texts.account, LocatorGetByTextOptions(Exact = true)).ClickAsync()
        do! Assertions.Expect(bar).ToContainTextAsync("researcher@example.test")
        do! Ui.click (Ui.button bar Texts.signOut)
        do! Assertions.Expect(bar.GetByText(Texts.signedIn, LocatorGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
    })
