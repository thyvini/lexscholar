/// Journeys for background summaries (F004): leaving a document or project neither stops the work nor loses its result.
/// Only Playwright's auto-waiting assertions and the fake's handshakes are used; there are no sleeps.
module LexScholar.Browser.Tests.BackgroundSummaryJourneyTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Server.Tests

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

let private answer = """{"points":[{"text":"Main idea","pages":[1,2]}]}"""

let private projectRow (page: IPage) (name: string) = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = name))

let private selectProject (page: IPage) (name: string) = task {
    let row = projectRow page name
    do! click (button row "Select")
    do! Assertions.Expect(row.Locator("strong.active")).ToHaveTextAsync("Active") }

let private createProject (page: IPage) (name: string) = task {
    do! page.GetByLabel("Project name").FillAsync(name)
    do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Create project")).ClickAsync()
    do! selectProject page name }

let private importAndAdd (page: IPage) (fileName: string) = task {
    let input = page.GetByLabel("Import a PDF")
    do! Assertions.Expect(input).ToBeVisibleAsync()
    do! input.SetInputFilesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", fileName))
    do! Assertions.Expect(page.GetByText($"Imported {fileName}.")).ToBeVisibleAsync()
    do! click (button (page.Locator("ul.documents li").Filter(LocatorFilterOptions(HasText = fileName))) "Add as Focused Source")
    do! Assertions.Expect(page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = fileName))).ToContainTextAsync("Focused Source") }

let private openDocument (page: IPage) (fileName: string) = task {
    let row = page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = fileName))
    do! click (button row "Open")
    do! Assertions.Expect(page.Locator("iframe.reader-frame")).ToBeVisibleAsync() }

let private summarizeButton (page: IPage) = page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Summarize", Exact = true))

let private panel (page: IPage) = page.Locator("section.summary")

let private expectCitation (page: IPage) = task {
    do! Assertions.Expect(panel page).ToContainTextAsync("Main idea")
    do! click ((panel page).Locator("button.citation[data-page='2']"))
    do! Assertions.Expect(page.Locator("iframe.reader-frame")).ToHaveAttributeAsync("src", Text.RegularExpressions.Regex("/content#page=2$")) }

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T002"); Trait("Category", "Browser")>]
let ``a summary asked on one document is found on return after opening another document`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeChatService(Hold answer)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! createProject page "Alpha"
        do! importAndAdd page "article-en.pdf"
        do! importAndAdd page "article-pt.pdf"
        do! openDocument page "article-en.pdf"
        do! click (summarizeButton page)
        do! fake.Waiting.WaitAsync(TimeSpan.FromSeconds 30.)
        do! openDocument page "article-pt.pdf"
        do! Assertions.Expect(panel page).ToHaveCountAsync(0)
        fake.Release()
        do! openDocument page "article-en.pdf"
        do! expectCitation page
        Assert.False(fake.Cancelled.IsCompleted)
        Assert.Equal(1, fake.Requests.Length) })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T002"); Trait("Category", "Browser")>]
let ``a summary asked in one project is found on return after switching project`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeChatService(Hold answer)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! createProject page "Alpha"
        do! importAndAdd page "article-en.pdf"
        do! openDocument page "article-en.pdf"
        do! click (summarizeButton page)
        do! fake.Waiting.WaitAsync(TimeSpan.FromSeconds 30.)
        do! createProject page "Beta"
        do! Assertions.Expect(panel page).ToHaveCountAsync(0)
        fake.Release()
        do! selectProject page "Alpha"
        do! openDocument page "article-en.pdf"
        do! expectCitation page
        Assert.False(fake.Cancelled.IsCompleted)
        Assert.Equal(1, fake.Requests.Length) })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T004"); Trait("Category", "Browser")>]
let ``returning to a document while its summary runs shows it running, and the summary appears without asking again`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeChatService(Hold answer)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! createProject page "Alpha"
        do! importAndAdd page "article-en.pdf"
        do! importAndAdd page "article-pt.pdf"
        do! openDocument page "article-en.pdf"
        do! click (summarizeButton page)
        do! fake.Waiting.WaitAsync(TimeSpan.FromSeconds 30.)
        do! openDocument page "article-pt.pdf"
        do! openDocument page "article-en.pdf"
        // Back while it still runs: the running state, not the Summarize button.
        do! Assertions.Expect(page.GetByRole(AriaRole.Status).Filter(LocatorFilterOptions(HasText = "Summarizing"))).ToBeVisibleAsync()
        do! Assertions.Expect(summarizeButton page).ToHaveCountAsync(0)
        fake.Release()
        // No click: polling finds it.
        do! expectCitation page
        Assert.Equal(1, fake.Requests.Length) })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T005"); Trait("Category", "Browser")>]
let ``a summary that fails while she is on another document shows its message on return and Try again replaces it`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeChatService(fun call -> if call = 1 then Status 400 else Reply answer)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! createProject page "Alpha"
        do! importAndAdd page "article-en.pdf"
        do! importAndAdd page "article-pt.pdf"
        do! openDocument page "article-en.pdf"
        do! click (summarizeButton page)
        do! openDocument page "article-pt.pdf"
        do! Assertions.Expect(panel page).ToHaveCountAsync(0)
        do! openDocument page "article-en.pdf"
        // The failure belongs to the job, so it is found whether it ended before or after her return.
        let alert = page.GetByRole(AriaRole.Alert).Filter(LocatorFilterOptions(HasText = "MaxOutputTokens"))
        do! Assertions.Expect(alert).ToBeVisibleAsync()
        do! click (page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Try again", Exact = true)))
        do! expectCitation page
        Assert.Equal(2, fake.Requests.Length) })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T008"); Trait("Category", "Browser")>]
let ``Stop clears a running summary, the held call is cancelled and she can ask again`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeChatService(fun call -> if call = 1 then Hold answer else Reply answer)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! createProject page "Alpha"
        do! importAndAdd page "article-en.pdf"
        do! openDocument page "article-en.pdf"
        do! click (summarizeButton page)
        do! fake.Waiting.WaitAsync(TimeSpan.FromSeconds 30.)
        do! click (button (page.GetByRole(AriaRole.Status).Filter(LocatorFilterOptions(HasText = "Summarizing"))) "Stop")
        do! fake.Cancelled.WaitAsync(TimeSpan.FromSeconds 30.)
        do! Assertions.Expect(summarizeButton page).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByRole(AriaRole.Status).Filter(LocatorFilterOptions(HasText = "Summarizing"))).ToHaveCountAsync(0)
        Assert.Equal(1, fake.Requests.Length)
        do! click (summarizeButton page)
        do! expectCitation page
        Assert.Equal(2, fake.Requests.Length) })

let private documentRow (page: IPage) (fileName: string) = page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = fileName))

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T010"); Trait("Category", "Browser")>]
let ``while viewing another document the marker of a running summary turns to finished without a reload`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeChatService(Hold answer)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! createProject page "Alpha"
        do! importAndAdd page "article-en.pdf"
        do! importAndAdd page "article-pt.pdf"
        do! openDocument page "article-en.pdf"
        do! click (summarizeButton page)
        do! fake.Waiting.WaitAsync(TimeSpan.FromSeconds 30.)
        do! openDocument page "article-pt.pdf"
        // Viewing B: A's marker and the project's marker say running; B has none.
        do! Assertions.Expect((documentRow page "article-en.pdf").Locator(".summary-marker")).ToHaveTextAsync("Summary running")
        do! Assertions.Expect((documentRow page "article-pt.pdf").Locator(".summary-marker")).ToHaveCountAsync(0)
        do! Assertions.Expect((projectRow page "Alpha").Locator(".summary-marker")).ToHaveTextAsync("Summary running")
        fake.Release()
        // No reload, no click: polling updates both.
        do! Assertions.Expect((documentRow page "article-en.pdf").Locator(".summary-marker")).ToHaveTextAsync("Summary finished")
        do! Assertions.Expect((projectRow page "Alpha").Locator(".summary-marker")).ToHaveTextAsync("Summary finished") })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T010"); Trait("Category", "Browser")>]
let ``a failed summary is marked failed in the list`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeChatService(fun _ -> Status 400)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! createProject page "Alpha"
        do! importAndAdd page "article-en.pdf"
        do! importAndAdd page "article-pt.pdf"
        do! openDocument page "article-en.pdf"
        do! click (summarizeButton page)
        do! openDocument page "article-pt.pdf"
        do! Assertions.Expect((documentRow page "article-en.pdf").Locator(".summary-marker")).ToHaveTextAsync("Summary failed")
        do! Assertions.Expect((projectRow page "Alpha").Locator(".summary-marker")).ToHaveTextAsync("Summary failed") })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T010"); Trait("Category", "Browser")>]
let ``removing a document with a finished summary clears the project's marker`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeChatService(Reply answer)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! createProject page "Alpha"
        do! importAndAdd page "article-en.pdf"
        do! importAndAdd page "article-pt.pdf"
        do! openDocument page "article-en.pdf"
        do! click (summarizeButton page)
        do! expectCitation page
        // Another document is open when the removal happens, so the view does not change.
        do! openDocument page "article-pt.pdf"
        do! Assertions.Expect((projectRow page "Alpha").Locator(".summary-marker")).ToHaveTextAsync("Summary finished")
        do! click (button (documentRow page "article-en.pdf") "Remove")
        do! Assertions.Expect(documentRow page "article-en.pdf").ToHaveCountAsync(0)
        do! Assertions.Expect((projectRow page "Alpha").Locator(".summary-marker")).ToHaveCountAsync(0) })
