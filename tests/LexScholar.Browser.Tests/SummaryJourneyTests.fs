module LexScholar.Browser.Tests.SummaryJourneyTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Server.Tests

// Real-browser summary journeys (F003 T004) against an isolated in-process host
// serving the client, with the fake chat service standing in for the configured
// one. Only Playwright's auto-waiting assertions are used; there are no sleeps.

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

let private openApp (browser: IBrowser) (harness: TestHarness) = task {
    let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
    let! page = context.NewPageAsync()
    let! _ = page.GotoAsync("/")
    do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = "Your projects"))).ToBeVisibleAsync()
    return page }

let private button (scope: ILocator) (name: string) =
    scope.GetByRole(AriaRole.Button, LocatorGetByRoleOptions(Name = name, Exact = true))

let private click (control: ILocator) = task {
    do! Assertions.Expect(control).ToBeVisibleAsync()
    do! control.ClickAsync() }

let private settings = [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "test-model" ]

let private answer = """{"points":[{"text":"<b>Main</b> idea","pages":[1,2]},{"text":"Second idea","pages":[3]}]}"""

/// Opens article-en.pdf as a project document of an active project.
let private arrange (page: IPage) = task {
    do! page.GetByLabel("Project name").FillAsync("Alpha")
    do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Create project")).ClickAsync()
    let row = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = "Alpha"))
    do! click (button row "Select")
    do! Assertions.Expect(row.Locator("strong.active")).ToHaveTextAsync("Active")
    let input = page.GetByLabel("Import a PDF")
    do! Assertions.Expect(input).ToBeVisibleAsync()
    do! input.SetInputFilesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", "article-en.pdf"))
    do! Assertions.Expect(page.GetByText("Imported article-en.pdf.")).ToBeVisibleAsync()
    do! click (button (page.Locator("ul.documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))) "Add as Focused Source")
    let documentRow = page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))
    do! Assertions.Expect(documentRow).ToContainTextAsync("Focused Source")
    do! click (button documentRow "Open")
    do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = "Reader"))).ToBeVisibleAsync() }

let private panel (page: IPage) = page.Locator("section.summary")

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T004"); Trait("Category", "Browser")>]
let ``a requested summary is labelled as generated, shows its context and plain-text points, and can be dismissed`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeChatService(Reply answer)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! arrange page
        do! click (page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Summarize", Exact = true)))
        do! Assertions.Expect(panel page).ToContainTextAsync("Generated summary")
        do! Assertions.Expect(panel page).ToContainTextAsync("for your review")
        let context = panel page |> fun p -> p.Locator(".summary-context")
        do! Assertions.Expect(context).ToContainTextAsync("article-en.pdf")
        do! Assertions.Expect(context).ToContainTextAsync("127.0.0.1:9")
        do! Assertions.Expect(context).ToContainTextAsync("test-model")
        do! Assertions.Expect(context).ToContainTextAsync("pages 1–12")
        let points = (panel page).Locator("ul.summary-points li")
        do! Assertions.Expect(points).ToHaveCountAsync(2)
        // Generated text is plain text: markup in a point is shown literally.
        do! Assertions.Expect(points.First.Locator(".point-text")).ToHaveTextAsync("<b>Main</b> idea")
        do! Assertions.Expect(points.First.Locator("b")).ToHaveCountAsync(0)
        do! Assertions.Expect(points.First.Locator("button.citation")).ToHaveTextAsync([| "1"; "2" |])
        do! click (button (panel page) "Dismiss")
        do! Assertions.Expect(panel page).ToHaveCountAsync(0)
        // Dismissing changes nothing about the document or its association.
        do! Assertions.Expect(page.Locator("ul.project-documents li")).ToHaveCountAsync(1)
        do! Assertions.Expect(page.Locator("ul.project-documents li")).ToContainTextAsync("Focused Source")
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = "Reader"))).ToBeVisibleAsync() })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T004"); Trait("Category", "Browser")>]
let ``without a configured service the researcher is told how to configure one`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        do! arrange page
        do! click (page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Summarize", Exact = true)))
        let alert = page.GetByRole(AriaRole.Alert).Filter(LocatorFilterOptions(HasText = "LexScholar:Summary:Endpoint"))
        do! Assertions.Expect(alert).ToContainTextAsync("LexScholar:Summary:Model")
        do! Assertions.Expect(panel page).ToHaveCountAsync(0) })
