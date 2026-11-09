module LexScholar.Browser.Tests.ProjectJourneyTests

open System
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Server.Tests

// Real-browser journeys against an isolated in-process host serving the
// WebAssembly client. Browsers are Playwright's pinned builds; install them
// once as described in specs/001-research-workspace/quickstart.md.

let private installHint =
    "Install the Playwright browsers first: build this project, then run "
    + "bin/<Configuration>/net10.0/.playwright/node/linux-x64/node "
    + "bin/<Configuration>/net10.0/.playwright/package/cli.js install chromium firefox"

let private launch (name: string) = task {
    // WebAssembly boot can exceed Playwright's 5-second default, especially in Firefox.
    Assertions.SetDefaultExpectTimeout(30_000.0f)
    let! playwright = Playwright.CreateAsync()
    try
        let! browser = playwright.[name].LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
        return playwright, browser
    with :? PlaywrightException as error ->
        playwright.Dispose()
        return failwithf "Could not launch %s. %s\n%s" name installHint error.Message }

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
let ``empty workspace shows the project-list empty state`` (browserName: string) : Task = task {
    use workspace = TemporaryWorkspace.Create()
    use harness = startHost workspace
    let! playwright, browser = launch browserName
    use _ = playwright
    try
        let! context, page = openApp browser harness
        do! Assertions.Expect(emptyState page).ToBeVisibleAsync()
        do! Assertions.Expect(page.Locator("ul.projects li")).ToHaveCountAsync(0)
        do! Assertions.Expect(page.Locator("strong.active")).ToHaveCountAsync(0)
        do! context.CloseAsync()
    finally browser.CloseAsync().GetAwaiter().GetResult() }

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T019"); Trait("Category", "Browser")>]
let ``creating and selecting projects shows the active project`` (browserName: string) : Task = task {
    use workspace = TemporaryWorkspace.Create()
    use harness = startHost workspace
    let! playwright, browser = launch browserName
    use _ = playwright
    try
        let! context, page = openApp browser harness
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
        do! expectSelectable page "Beta"
        do! context.CloseAsync()
    finally browser.CloseAsync().GetAwaiter().GetResult() }

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T019"); Trait("Category", "Browser")>]
let ``restart over the same data directory restores the active project`` (browserName: string) : Task = task {
    use workspace = TemporaryWorkspace.Create()
    let! playwright, browser = launch browserName
    use _ = playwright
    try
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
        let! context, page = openApp browser restarted
        do! Assertions.Expect(page.Locator("ul.projects li")).ToHaveCountAsync(2)
        do! expectSelectable page "Alpha"
        do! expectActive page "Beta"
        do! context.CloseAsync()
    finally browser.CloseAsync().GetAwaiter().GetResult() }
