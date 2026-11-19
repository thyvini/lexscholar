/// F004 T011: leaving a document while the confirmation is open sends nothing and leaves her with no summary (FR-006, scenario 7).
module LexScholar.Browser.Tests.PreSendJourneyTests
open LexScholar.Client

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

let private button (scope: ILocator) (name: string) =
    scope.GetByRole(AriaRole.Button, LocatorGetByRoleOptions(Name = name, Exact = true))

let private click (control: ILocator) = task {
    do! Assertions.Expect(control).ToBeVisibleAsync()
    do! control.ClickAsync() }

let private answer = """{"points":[{"text":"Main idea","pages":[1,2]}]}"""

let private selectProject (page: IPage) (name: string) = task {
    let row = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = name))
    do! click (button row LexScholar.Client.Texts.select)
    do! Assertions.Expect(row.Locator("strong.active")).ToHaveTextAsync(LexScholar.Client.Texts.active) }

let private importAndAdd (page: IPage) (fileName: string) = task {
    do! click (button (page.Locator("body")) LexScholar.Client.Texts.library)
    let input = page.GetByLabel(Texts.importPdf)
    do! Assertions.Expect(input).ToBeVisibleAsync()
    do! input.SetInputFilesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", fileName))
    do! Assertions.Expect(page.GetByText((Texts.imported fileName))).ToBeVisibleAsync()
    do! click (button (page.Locator("ul.documents li").Filter(LocatorFilterOptions(HasText = fileName))) Texts.addFocused)
    do! Assertions.Expect(page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = fileName))).ToContainTextAsync(Texts.focusedSource) }

let private openDocument (page: IPage) (fileName: string) = task {
    let row = page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = fileName))
    do! click (button row Texts.openDocument)
    do! Assertions.Expect(page.GetByTestId("main").Locator("iframe")).ToHaveCountAsync(0) }

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T011"); Trait("Task", "T007"); Trait("Task", "T010"); Trait("Category", "Browser")>]
let ``leaving a document while the confirmation is open sends nothing and she returns to an idle document`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi(ResponsesScript.Completed answer)
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
        let! page = context.NewPageAsync()
        let! _ = page.GotoAsync("/")
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = LexScholar.Client.Texts.projects, Exact = true)).First).ToBeVisibleAsync()
        do! page.GetByLabel(LexScholar.Client.Texts.projectName).FillAsync("Alpha")
        do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = LexScholar.Client.Texts.createProject)).ClickAsync()
        do! selectProject page "Alpha"
        do! importAndAdd page "article-en.pdf"
        do! importAndAdd page "article-pt.pdf"
        let dialog = page.GetByRole(AriaRole.Alertdialog)
        let summarize = page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = LexScholar.Client.Texts.summarize, Exact = true))
        do! openDocument page "article-en.pdf"
        do! Ui.signInWithChatGpt page harness fake
        do! openDocument page "article-en.pdf"
        do! click summarize
        do! Assertions.Expect(dialog).ToHaveAccessibleNameAsync("Enviar documentos ao ChatGPT?")
        do! Assertions.Expect(dialog.GetByRole(AriaRole.Link)).ToHaveAttributeAsync("href", "https://help.openai.com/pt-br/articles/20001542-using-your-chatgpt-plan-in-other-apps-and-sites")
        do! Assertions.Expect(dialog).ToContainTextAsync("article-en.pdf")
        do! openDocument page "article-pt.pdf"
        do! Assertions.Expect(dialog).ToHaveCountAsync(0)
        do! openDocument page "article-en.pdf"
        // Back on A: idle, not asked, not running, not summarized.
        do! Assertions.Expect(summarize).ToBeVisibleAsync()
        do! Assertions.Expect(dialog).ToHaveCountAsync(0)
        do! Assertions.Expect(page.Locator("section.summary")).ToHaveCountAsync(0)
        Assert.Empty(fake.RequestsTo("api.openai.com", "/v1/responses")) })
