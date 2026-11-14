module LexScholar.Browser.Tests.ChatGptJourneyTests

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

let private openDocument (page: IPage) = task {
    let row = page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))
    do! click (button row "Open")
    do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = "Reader"))).ToBeVisibleAsync() }

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
    do! Assertions.Expect(page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))).ToContainTextAsync("Focused Source")
    do! openDocument page }

let private summarize (page: IPage) = click (page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Summarize", Exact = true)))
let private panel (page: IPage) = page.Locator("section.summary")

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T011"); Trait("Category", "Browser")>]
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
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = "Your projects"))).ToBeVisibleAsync()
        do! arrange page

        // Not signed in: the sign-in action is offered, branded as OpenAI asks.
        let signIn = page.Locator("button[data-oai-siwc-button]").First
        do! Assertions.Expect(signIn).ToContainTextAsync("Continue with ChatGPT")
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
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = "Your projects"))).ToBeVisibleAsync()
        do! Assertions.Expect(welcome).ToHaveCountAsync(0)
        do! openDocument page
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
        do! Assertions.Expect((panel page).Locator("button.citation")).ToHaveTextAsync([| "1"; "2"; "3" |])
        let using = page.Locator(".summary-using-plan")
        do! Assertions.Expect(using).ToContainTextAsync("Using ChatGPT plan")
        do! Assertions.Expect(using.GetByRole(AriaRole.Link, LocatorGetByRoleOptions(Name = "Manage usage"))).ToHaveAttributeAsync("href", usageUrl)

        // The second summary goes straight through.
        do! click (button (panel page) "Dismiss")
        do! summarize page
        do! Assertions.Expect(panel page).ToContainTextAsync("Generated summary")
        do! Assertions.Expect(dialog).ToHaveCountAsync(0)
        Assert.Equal(2, (fake.RequestsTo("api.openai.com", "/v1/responses")).Length)

        // The usage limit points at her usage settings and keeps her signed in.
        do! click (button (panel page) "Dismiss")
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
