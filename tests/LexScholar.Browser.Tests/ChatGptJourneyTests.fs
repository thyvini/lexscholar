module LexScholar.Browser.Tests.ChatGptJourneyTests

open LexScholar.Client
open System
open System.IO
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Server.Tests

// Real-browser journey for summarizing on her ChatGPT plan (F005 T011) against an
// isolated in-process host serving the client, with FakeOpenAi standing in for
// auth.openai.com and api.openai.com. The browser never reaches OpenAI: the
// authorization address is intercepted, and the journey continues at the host's
// own callback with the issued state. Only auto-waiting assertions; no sleeps.

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

let private answer = """{"points":[{"text":"Main idea","pages":[1,2]},{"text":"Second idea","pages":[3]}]}"""
let private usageUrl = "https://chatgpt.com/settings/usage"

let private openDocument (page: IPage) (fileName: string) = task {
    let row = page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = fileName))
    do! click (button row Texts.openDocument)
    do! Assertions.Expect(page.GetByTestId("main").Locator("iframe")).ToHaveCountAsync(0) }

/// Opens article-en.pdf as a project document of an active project.
let private arrange (page: IPage) = task {
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
    do! Assertions.Expect(page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))).ToContainTextAsync(Texts.focusedSource)
    // A second document: a finished summary answers a repeated ask of the same document (F004), so the usage limit needs its own.
    do! input.SetInputFilesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", "article-pt.pdf"))
    do! Assertions.Expect(page.GetByText((Texts.imported "article-pt.pdf"))).ToBeVisibleAsync()
    do! click (button (page.Locator("ul.documents li").Filter(LocatorFilterOptions(HasText = "article-pt.pdf"))) Texts.addFocused)
    do! Assertions.Expect(page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-pt.pdf"))).ToContainTextAsync(Texts.focusedSource)
    do! openDocument page "article-en.pdf" }

let private summarize (page: IPage) = click (page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = LexScholar.Client.Texts.summarize, Exact = true)))
let private panel (page: IPage) = page.Locator("section.summary")

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T011"); Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``she signs in, summarizes on her plan, meets the usage limit and signs out`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        let script call =
            if call <= 2 then ResponsesScript.Completed answer
            else ResponsesScript.Status(429, "subscription_sharing_usage_limit_exceeded")
        use fake = new FakeOpenAi(script)
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
        let! page = context.NewPageAsync()
        // The authorization address is captured, never followed.
        let authorization = TaskCompletionSource<Uri>()
        let intercept (route: IRoute) : Task =
            authorization.TrySetResult(Uri route.Request.Url) |> ignore
            route.FulfillAsync(RouteFulfillOptions(Status = 200, ContentType = "text/html", Body = "<html><body>OpenAI</body></html>"))
        use! _ = page.RouteAsync("https://auth.openai.com/**", Func<IRoute, Task>(intercept))
        let! _ = page.GotoAsync("/")
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = LexScholar.Client.Texts.projects, Exact = true)).First).ToBeVisibleAsync()
        do! arrange page

        // Not signed in: the sign-in action is offered, branded as OpenAI asks.
        let signIn = page.Locator("button[data-oai-siwc-button]").First
        do! Assertions.Expect(signIn).ToContainTextAsync(LexScholar.Client.Texts.signIn)
        do! Assertions.Expect(page.Locator(".chatgpt-account")).ToContainTextAsync("signed out")
        do! click signIn
        let! url = authorization.Task
        let query = System.Web.HttpUtility.ParseQueryString url.Query
        Assert.Equal("auth.openai.com", url.Host)
        fake.Nonce <- query["nonce"]
        let state = Uri.EscapeDataString query["state"]
        let! _ = page.GotoAsync($"/auth/callback?state={state}&code=code-1&client_id={fake.ClientId}")

        // Back in the app, signed in, with the welcome once and the query removed.
        let welcome = page.Locator("section.chatgpt-welcome")
        do! Assertions.Expect(welcome).ToContainTextAsync("You’re using your ChatGPT plan")
        do! Assertions.Expect(welcome.GetByRole(AriaRole.Link, LocatorGetByRoleOptions(Name = "Manage usage"))).ToHaveAttributeAsync("href", usageUrl)
        do! Assertions.Expect(page).Not.ToHaveURLAsync(Regex "chatgpt=")
        // The dismissal is a background request: wait for it before reloading.
        let dismissed = page.WaitForResponseAsync("**/api/chatgpt/welcome/dismiss")
        do! click (button welcome "Got it")
        let! _ = dismissed
        do! Assertions.Expect(welcome).ToHaveCountAsync(0)
        let! _ = page.ReloadAsync()
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = LexScholar.Client.Texts.projects, Exact = true)).First).ToBeVisibleAsync()
        do! Assertions.Expect(welcome).ToHaveCountAsync(0)
        do! openDocument page "article-en.pdf"
        do! Assertions.Expect(page.Locator(".chatgpt-account")).ToContainTextAsync("Signed in to ChatGPT as researcher@example.test")

        // The first summary asks, naming the plan and her account.
        let dialog = page.GetByRole(AriaRole.Alertdialog)
        do! summarize page
        do! Assertions.Expect(dialog).ToContainTextAsync("your ChatGPT plan (account researcher@example.test)")
        do! Assertions.Expect(dialog).ToContainTextAsync("ChatGPT data controls")
        do! Assertions.Expect(dialog.GetByRole(AriaRole.Link)).ToHaveAttributeAsync("href", Regex "help\\.openai\\.com")
        Assert.Empty(fake.RequestsTo("api.openai.com", "/v1/responses"))
        do! click (button dialog "Send")
        do! Assertions.Expect(panel page).ToContainTextAsync("Generated summary")
        do! Assertions.Expect((panel page).Locator(".summary-context")).ToContainTextAsync("ChatGPT plan")
        do! Assertions.Expect((panel page).GetByRole(AriaRole.Link)).ToHaveTextAsync([| "pp. 1–2"; "p. 3" |])
        let using = page.Locator(".summary-using-plan")
        do! Assertions.Expect(using).ToContainTextAsync(LexScholar.Client.Texts.usingPlan)
        do! Assertions.Expect(using.GetByRole(AriaRole.Link, LocatorGetByRoleOptions(Name = LexScholar.Client.Texts.manageUsage))).ToHaveAttributeAsync("href", usageUrl)

        // Asking again after dismissing (which deletes the stored summary, F004) goes straight through, with no confirmation.
        do! click (button (panel page) LexScholar.Client.Texts.delete)
        do! click (button (page.GetByRole(AriaRole.Alertdialog)) LexScholar.Client.Texts.delete)
        do! summarize page
        do! Assertions.Expect(panel page).ToContainTextAsync("Generated summary")
        do! Assertions.Expect(dialog).ToHaveCountAsync(0)
        Assert.Equal(2, (fake.RequestsTo("api.openai.com", "/v1/responses")).Length)

        // The usage limit, on the other document, points at her usage settings and keeps her signed in.
        do! click (button (panel page) LexScholar.Client.Texts.delete)
        do! click (button (page.GetByRole(AriaRole.Alertdialog)) LexScholar.Client.Texts.delete)
        do! openDocument page "article-pt.pdf"
        do! summarize page
        let alert = page.GetByRole(AriaRole.Alert).Filter(LocatorFilterOptions(HasText = "Usage limit reached"))
        do! Assertions.Expect(alert).ToBeVisibleAsync()
        do! Assertions.Expect(page.Locator("a.manage-usage")).ToHaveAttributeAsync("href", usageUrl)
        do! Assertions.Expect(page.Locator(".chatgpt-account")).ToContainTextAsync("Signed in to ChatGPT as researcher@example.test")

        // Signing out offers sign-in again and revokes at OpenAI.
        do! click (page.Locator("button.chatgpt-sign-out"))
        do! Assertions.Expect(page.Locator("button[data-oai-siwc-button]").First).ToBeVisibleAsync()
        do! Assertions.Expect(page.Locator(".chatgpt-account")).ToContainTextAsync("signed out")
        Assert.Single(fake.RequestsTo("auth.openai.com", "/api/accounts/oauth/revoke")) |> ignore })
