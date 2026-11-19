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

let private button = Ui.button

let private click (control: ILocator) = task {
    do! Assertions.Expect(control).ToBeVisibleAsync()
    do! control.ClickAsync() }

let private answer = """{"points":[{"text":"Main idea","pages":[1,2]}]}"""

let private selectProject (page: IPage) (name: string) = task {
    let row = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = name))
    do! click (button row LexScholar.Client.Texts.select)
    do! Assertions.Expect(row).ToHaveClassAsync("selected") }

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

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T020"); Trait("Category", "Browser")>]
let ``send confirmation is a centered modal with keyboard cancellation and deliberate send`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi(ResponsesScript.Completed answer)
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! page = Ui.openApp browser harness
        do! page.SetViewportSizeAsync(1440, 900)
        do! SummaryJourneyTests.arrange page
        do! Ui.signInWithChatGpt page harness fake
        do! openDocument page "article-en.pdf"
        let summarize = button (page.Locator("body")) Texts.summarize
        do! click summarize
        let dialog = page.GetByRole(AriaRole.Alertdialog)
        do! Assertions.Expect(dialog).ToHaveAttributeAsync("aria-describedby", "send-disclosure send-information")
        let! disclosure = dialog.Locator("#send-disclosure").InnerTextAsync()
        let! information = dialog.Locator("#send-information").InnerTextAsync()
        Assert.Contains("article-en.pdf", disclosure)
        Assert.Contains("researcher@example.test", disclosure)
        Assert.Equal(Texts.confirmationNote, information)
        do! Assertions.Expect(dialog).ToHaveAccessibleDescriptionAsync($"{disclosure} {information}")
        do! Assertions.Expect(dialog).ToHaveCSSAsync("position", "fixed")
        do! Assertions.Expect(dialog).ToHaveAttributeAsync("aria-modal", "true")
        let! modal = dialog.EvaluateAsync<bool>("e => e.matches(':modal')")
        Assert.True(modal)
        let! rect = dialog.BoundingBoxAsync()
        Assert.InRange(float (rect.X + rect.Width / 2.0f), 719.0, 721.0)
        Assert.InRange(float (rect.Y + rect.Height / 2.0f), 449.0, 451.0)
        do! Assertions.Expect(dialog).ToHaveCSSAsync("width", "520px")
        do! Assertions.Expect(dialog).ToHaveCSSAsync("font-family", "Inter, system-ui, sans-serif")
        let! backdrop = dialog.EvaluateAsync<string>("e => getComputedStyle(e, '::backdrop').backgroundColor")
        Assert.Equal("rgba(0, 0, 0, 0.4)", backdrop)
        do! Assertions.Expect(dialog.Locator(".send-info")).ToHaveCSSAsync("background-color", "rgb(238, 246, 246)")
        let cancel = button dialog Texts.cancel
        let send = button dialog Texts.sendAndSummarize
        do! Assertions.Expect(send).ToHaveCSSAsync("background-color", "rgb(14, 110, 128)")
        do! Assertions.Expect(send).ToHaveCSSAsync("color", "rgb(255, 255, 255)")
        do! Assertions.Expect(cancel).ToBeFocusedAsync()
        do! page.Mouse.ClickAsync(20.0f, 80.0f)
        do! Assertions.Expect(dialog).ToBeVisibleAsync()
        Assert.Empty(fake.RequestsTo("api.openai.com", "/v1/responses"))
        do! cancel.FocusAsync()
        do! page.Keyboard.PressAsync("Tab")
        do! Assertions.Expect(send).ToBeFocusedAsync()
        do! page.Keyboard.PressAsync("Tab")
        do! Assertions.Expect(dialog.GetByRole(AriaRole.Link)).ToBeFocusedAsync()
        do! page.Keyboard.PressAsync("Shift+Tab")
        do! Assertions.Expect(send).ToBeFocusedAsync()
        do! page.SetViewportSizeAsync(480, 700)
        let! narrow = dialog.BoundingBoxAsync()
        Assert.InRange(float narrow.X, 15.0, 17.0)
        Assert.InRange(float narrow.Width, 447.0, 449.0)
        do! page.SetViewportSizeAsync(1440, 900)
        do! page.Keyboard.PressAsync("Escape")
        do! Assertions.Expect(dialog).ToHaveCountAsync(0)
        do! Assertions.Expect(summarize).ToBeFocusedAsync()
        Assert.Empty(fake.RequestsTo("api.openai.com", "/v1/responses"))
        do! page.Keyboard.PressAsync("Enter")
        do! Assertions.Expect(cancel).ToBeFocusedAsync()
        do! page.Keyboard.PressAsync("Enter")
        do! Assertions.Expect(dialog).ToHaveCountAsync(0)
        do! Assertions.Expect(summarize).ToBeFocusedAsync()
        Assert.Empty(fake.RequestsTo("api.openai.com", "/v1/responses"))
        do! page.Keyboard.PressAsync("Enter")
        do! Assertions.Expect(cancel).ToBeFocusedAsync()
        do! page.Keyboard.PressAsync("Tab")
        do! page.Keyboard.PressAsync("Enter")
        do! Assertions.Expect(page.Locator(".summary-points")).ToContainTextAsync("Main idea")
        Assert.Single(fake.RequestsTo("api.openai.com", "/v1/responses")) |> ignore
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T020"); Trait("Task", "T011"); Trait("Task", "T007"); Trait("Task", "T010"); Trait("Category", "Browser")>]
let ``leaving a document while the confirmation is open sends nothing and she returns to an idle document`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi(ResponsesScript.Completed answer)
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
        let! page = context.NewPageAsync()
        let! _ = page.GotoAsync("/")
        do! Ui.enterWorkspace page
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
        // Simulate an app-driven document change. The modal intentionally makes
        // background controls inert to pointer/keyboard interaction.
        let other = button (page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-pt.pdf"))) Texts.openDocument
        let! _ = other.EvaluateAsync("element => element.click()")
        do! Assertions.Expect(dialog).ToHaveCountAsync(0)
        do! openDocument page "article-en.pdf"
        // Back on A: idle, not asked, not running, not summarized.
        do! Assertions.Expect(summarize).ToBeVisibleAsync()
        do! Assertions.Expect(dialog).ToHaveCountAsync(0)
        do! Assertions.Expect(page.Locator("section.summary")).ToHaveCountAsync(0)
        Assert.Empty(fake.RequestsTo("api.openai.com", "/v1/responses")) })
