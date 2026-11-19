module LexScholar.Browser.Tests.SummaryJourneyTests

open LexScholar.Client
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
    do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = LexScholar.Client.Texts.projects, Exact = true)).First).ToBeVisibleAsync()
    return page }

let private button (scope: ILocator) (name: string) =
    scope.GetByRole(AriaRole.Button, LocatorGetByRoleOptions(Name = name, Exact = true))

let private click (control: ILocator) = task {
    do! Assertions.Expect(control).ToBeVisibleAsync()
    do! control.ClickAsync() }

let private settings = [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "test-model" ]

let private answer = """{"points":[{"text":"<b>Main</b> idea","pages":[1,2]},{"text":"Second idea","pages":[3]}]}"""

/// Opens article-en.pdf as a project document of an active project.
let arrange (page: IPage) = task {
    do! page.GetByLabel(LexScholar.Client.Texts.projectName).FillAsync("Alpha")
    do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = LexScholar.Client.Texts.createProject)).ClickAsync()
    let row = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = "Alpha"))
    do! click (button row LexScholar.Client.Texts.select)
    do! Assertions.Expect(row.Locator("strong.active")).ToHaveTextAsync(LexScholar.Client.Texts.active)
    do! click (button (page.Locator("body")) LexScholar.Client.Texts.library)
    let input = page.GetByLabel(Texts.importPdf)
    do! Assertions.Expect(input).ToBeVisibleAsync()
    do! input.SetInputFilesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", "article-en.pdf"))
    do! Assertions.Expect(page.GetByText((Texts.imported "article-en.pdf"))).ToBeVisibleAsync()
    do! click (button (page.Locator("ul.documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))) Texts.addFocused)
    let documentRow = page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))
    do! Assertions.Expect(documentRow).ToContainTextAsync(Texts.focusedSource)
    do! click (button documentRow Texts.openDocument)
    do! Assertions.Expect(page.GetByTestId("main").GetByRole(AriaRole.Heading, LocatorGetByRoleOptions(Name = "article-en.pdf"))).ToBeVisibleAsync() }

let private panel (page: IPage) = page.Locator("section.summary")

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``historical configured service summary retains its recorded provenance`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi(ResponsesScript.Completed answer)
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! page = openApp browser harness
        do! arrange page
        do! Ui.signInWithChatGpt page harness fake
        use! projects = harness.Client.GetAsync("/api/projects")
        let! projectBody = projects.Content.ReadAsStringAsync()
        use projectJson = System.Text.Json.JsonDocument.Parse(projectBody)
        let project = projectJson.RootElement.GetProperty("projects").[0].GetProperty("id").GetGuid()
        use! documents = harness.Client.GetAsync("/api/documents")
        let! documentBody = documents.Content.ReadAsStringAsync()
        use documentJson = System.Text.Json.JsonDocument.Parse(documentBody)
        let document = documentJson.RootElement.[0].GetProperty("id").GetGuid()
        let generatedAt = DateTimeOffset(2026, 11, 14, 18, 54, 0, TimeSpan.Zero)
        let key: LexScholar.Domain.SummaryKey = { Project = project; Document = document }
        let summary: LexScholar.Domain.StoredSummary =
            { DocumentId = document; Points = [ { Text = "Historical point"; Pages = [ 1 ] } ]
              PagesCovered = [ 1; 2 ]; UnreadablePages = [ 2 ]; OmittedPoints = 1
              GeneratedAt = generatedAt; ServiceHost = "archive.example.test:8080"; Model = "historic-model" }
        LexScholar.Infrastructure.SummaryRepository.save harness.Root key summary
        let! _ = page.ReloadAsync()
        do! click (button (page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))) Texts.openDocument)
        let context = (panel page).Locator(".summary-context")
        do! Assertions.Expect(context).ToContainTextAsync("archive.example.test:8080")
        do! Assertions.Expect(context).ToContainTextAsync("historic-model")
        do! Assertions.Expect(context).ToContainTextAsync(generatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"))
        do! Assertions.Expect(context).ToContainTextAsync("pages 1–2")
        do! Assertions.Expect(context).ToContainTextAsync("Not readable")
        do! Assertions.Expect(context).ToContainTextAsync("1 generated points")
        do! Assertions.Expect(page.Locator(".summary-using-plan")).ToHaveCountAsync(0)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T004"); Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``a requested summary is labelled as generated, shows its context and plain-text points, and can be dismissed`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi(ResponsesScript.Completed answer)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! arrange page
        do! Ui.signInWithChatGpt page harness fake
        do! click (button (page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))) Texts.openDocument)
        do! click (page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = LexScholar.Client.Texts.summarize, Exact = true)))
        do! click (button (page.GetByRole(AriaRole.Alertdialog)) "Send")
        do! Assertions.Expect(panel page).ToContainTextAsync("Generated summary")
        do! Assertions.Expect(panel page).ToContainTextAsync("for your review")
        let context = panel page |> fun p -> p.Locator(".summary-context")
        do! Assertions.Expect(context).ToContainTextAsync("article-en.pdf")
        do! Assertions.Expect(context).ToContainTextAsync("ChatGPT plan")
        do! Assertions.Expect(context).ToContainTextAsync("gpt-")
        do! Assertions.Expect(context).ToContainTextAsync("pages 1–12")
        let points = (panel page).Locator("ul.summary-points li")
        do! Assertions.Expect(points).ToHaveCountAsync(2)
        // Generated text is plain text: markup in a point is shown literally.
        do! Assertions.Expect(points.First.Locator(".point-text")).ToHaveTextAsync("<b>Main</b> idea")
        do! Assertions.Expect(points.First.Locator("b")).ToHaveCountAsync(0)
        do! Assertions.Expect(points.First.GetByRole(AriaRole.Link)).ToHaveTextAsync("pp. 1–2")
        do! click (button (panel page) LexScholar.Client.Texts.delete)
        do! click (button (page.GetByRole(AriaRole.Alertdialog)) LexScholar.Client.Texts.delete)
        do! Assertions.Expect(panel page).ToHaveCountAsync(0)
        // Dismissing changes nothing about the document or its association.
        do! Assertions.Expect(page.Locator("ul.project-documents li")).ToHaveCountAsync(1)
        do! Assertions.Expect(page.Locator("ul.project-documents li")).ToContainTextAsync(Texts.focusedSource)
        do! Assertions.Expect(page.GetByTestId("main").GetByRole(AriaRole.Heading, LocatorGetByRoleOptions(Name = "article-en.pdf"))).ToBeVisibleAsync() })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T004"); Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``signed out even with a configured service the researcher is offered only ChatGPT sign-in`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true, settings = settings)
        let! page = openApp browser harness
        do! arrange page
        do! Assertions.Expect(page.Locator("button[data-oai-siwc-button]").First).ToContainTextAsync(LexScholar.Client.Texts.signIn)
        do! Assertions.Expect(page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = LexScholar.Client.Texts.summarize, Exact = true))).ToHaveCountAsync(0)
        do! Assertions.Expect(panel page).ToHaveCountAsync(0) })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T003"); Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``citations are page references that leave the reader and summary unchanged`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi(ResponsesScript.Completed answer)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! arrange page
        do! Ui.signInWithChatGpt page harness fake
        do! click (button (page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))) Texts.openDocument)
        do! click (page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = LexScholar.Client.Texts.summarize, Exact = true)))
        do! click (button (page.GetByRole(AriaRole.Alertdialog)) "Send")
        let citation = (panel page).GetByRole(AriaRole.Link, LocatorGetByRoleOptions(Name = "p. 3", Exact = true))
        do! Assertions.Expect(citation).ToHaveAttributeAsync("href", Text.RegularExpressions.Regex("/content#page=3$"))
        do! click citation
        do! Assertions.Expect(page.GetByTestId("main").Locator("iframe")).ToHaveCountAsync(0)
        do! Assertions.Expect(panel page).ToContainTextAsync("Generated summary") })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``with a configured service the plan alone is offered and confirmation can be cancelled`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi(ResponsesScript.Completed answer)
        let elsewhere = [ "LexScholar:Summary:Endpoint", "http://summary.example.test:8080/v1"; "LexScholar:Summary:Model", "test-model" ]
        use harness = TestHarness.Start(serveClient = true, settings = elsewhere, summaryHandler = fake)
        let! page = openApp browser harness
        do! arrange page
        do! Ui.signInWithChatGpt page harness fake
        do! click (button (page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))) Texts.openDocument)
        do! Assertions.Expect(page.Locator(".summary-destination")).ToHaveCountAsync(0)
        do! Assertions.Expect(page.Locator("body")).Not.ToContainTextAsync("LexScholar:Summary:Endpoint")
        do! Assertions.Expect(page.Locator(".summary-using-plan")).ToContainTextAsync("Usando seu plano ChatGPT")
        do! Assertions.Expect(page.Locator(".summary-using-plan").GetByRole(AriaRole.Link)).ToHaveTextAsync("Gerenciar uso")
        let dialog = page.GetByRole(AriaRole.Alertdialog)
        do! click (page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = LexScholar.Client.Texts.summarize, Exact = true)))
        do! Assertions.Expect(dialog).ToContainTextAsync("article-en.pdf")
        do! Assertions.Expect(dialog).ToContainTextAsync("ChatGPT plan")
        do! click (button dialog "Cancel")
        do! Assertions.Expect(dialog).ToHaveCountAsync(0)
        do! Assertions.Expect(panel page).ToHaveCountAsync(0)
        Assert.Empty(fake.RequestsTo("api.openai.com", "/v1/responses"))
        // Asking again asks again; Send then produces the summary with one service request.
        do! click (page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = LexScholar.Client.Texts.summarize, Exact = true)))
        do! click (button dialog "Send")
        do! Assertions.Expect(panel page).ToContainTextAsync("Generated summary")
        Assert.Single(fake.RequestsTo("api.openai.com", "/v1/responses")) |> ignore })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T010"); Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``a rejected request shows its explanation and Try again asks the service again`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi(fun call -> if call = 1 then ResponsesScript.UnstructuredStatus 400 else ResponsesScript.Completed answer)
        use harness = TestHarness.Start(serveClient = true, settings = settings, summaryHandler = fake)
        let! page = openApp browser harness
        do! arrange page
        do! Ui.signInWithChatGpt page harness fake
        do! click (button (page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))) Texts.openDocument)
        do! click (page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = LexScholar.Client.Texts.summarize, Exact = true)))
        do! click (button (page.GetByRole(AriaRole.Alertdialog)) "Send")
        let alert = page.GetByRole(AriaRole.Alert).Filter(LocatorFilterOptions(HasText = "The summary service"))
        do! Assertions.Expect(alert).ToContainTextAsync("Model")
        do! Assertions.Expect(alert).Not.ToContainTextAsync("service detail")
        do! click (page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Try again", Exact = true)))
        do! Assertions.Expect(panel page).ToContainTextAsync("Generated summary")
        Assert.Equal(2, (fake.RequestsTo("api.openai.com", "/v1/responses")).Length) })
