module LexScholar.Browser.Tests.ProjectJourneyTests

open System
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Server.Tests

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

/// Runs a journey in a headless browser, owning Playwright and browser cleanup.
let private withBrowser (name: string) (journey: IBrowser -> Task) : Task = task {
    use! playwright = Playwright.CreateAsync()
    let! browser =
        task {
            try
                return! playwright.[name].LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
            with :? PlaywrightException as error ->
                return failwithf "Could not launch %s. Install the Playwright browsers with scripts/install-browsers.sh.\n%s" name error.Message
        }
    use _ = browser
    do! journey browser }

let private startHost (workspace: TemporaryWorkspace) =
    TestHarness.Start(serveClient = true, workspace = workspace)

let private openApp (browser: IBrowser) (harness: TestHarness) = task {
    let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
    let! page = context.NewPageAsync()
    let! _ = page.GotoAsync("/")
    do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = "Your projects"))).ToBeVisibleAsync()
    return context, page }

let private row (page: IPage) (name: string) =
    page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = name))

let private emptyState (page: IPage) =
    page.GetByText("No projects yet. Create one to get started.")

let private createProject (page: IPage) (name: string) = task {
    do! page.GetByLabel("Project name").FillAsync(name)
    do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Create project")).ClickAsync()
    do! Assertions.Expect(row page name).ToBeVisibleAsync() }

let private expectActive (page: IPage) (name: string) = task {
    do! Assertions.Expect(row page name).ToHaveCountAsync(1)
    do! Assertions.Expect((row page name).Locator("strong.active")).ToHaveTextAsync("Active")
    do! Assertions.Expect(page.Locator("strong.active")).ToHaveCountAsync(1) }

let private expectSelectable (page: IPage) (name: string) =
    Assertions.Expect((row page name).GetByRole(AriaRole.Button, LocatorGetByRoleOptions(Name = "Select"))).ToBeVisibleAsync()

let private select (page: IPage) (name: string) =
    (row page name).GetByRole(AriaRole.Button, LocatorGetByRoleOptions(Name = "Select")).ClickAsync()

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T019"); Trait("Category", "Browser")>]
let ``empty workspace shows the project-list empty state`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use workspace = TemporaryWorkspace.Create()
        use harness = startHost workspace
        let! _, page = openApp browser harness
        do! Assertions.Expect(emptyState page).ToBeVisibleAsync()
        do! Assertions.Expect(page.Locator("ul.projects li")).ToHaveCountAsync(0)
        do! Assertions.Expect(page.Locator("strong.active")).ToHaveCountAsync(0) })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T019"); Trait("Category", "Browser")>]
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

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T019"); Trait("Category", "Browser")>]
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
