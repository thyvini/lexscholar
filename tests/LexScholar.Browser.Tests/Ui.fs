module LexScholar.Browser.Tests.Ui

open System
open System.Threading.Tasks
open Microsoft.Playwright
open LexScholar.Server.Tests
open LexScholar.Client

let withBrowser (name: string) (journey: IBrowser -> Task) : Task = task {
    use! playwright = Playwright.CreateAsync()
    let! browser =
        task {
            try return! playwright.[name].LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
            with :? PlaywrightException as error ->
                return failwithf "Could not launch %s. Install the Playwright browsers with scripts/install-browsers.sh.\n%s" name error.Message
        }
    use _ = browser
    do! journey browser
}

/// Workspace journeys explicitly skip optional first-run setup.
let enterWorkspace (page: IPage) = task {
    do! Assertions.Expect(page.Locator(".workspace[data-ready='True']")).ToBeVisibleAsync()
    let skip = page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = Texts.skipOnboarding, Exact = true))
    let! visible = skip.IsVisibleAsync()
    if visible then do! skip.ClickAsync()
    do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.projects, Exact = true)).First).ToBeVisibleAsync()
}

let openApp (browser: IBrowser) (harness: TestHarness) = task {
    let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
    let! page = context.NewPageAsync()
    let! _ = page.GotoAsync("/")
    do! enterWorkspace page
    return page
}

let button (scope: ILocator) (name: string) = scope.GetByRole(AriaRole.Button, LocatorGetByRoleOptions(Name = name, Exact = true))
let link (scope: ILocator) (name: string) = scope.GetByRole(AriaRole.Link, LocatorGetByRoleOptions(Name = name, Exact = true))
let region (page: IPage) (name: string) = page.GetByRole(AriaRole.Region, PageGetByRoleOptions(Name = name, Exact = true))
let listNamed (page: IPage) (name: string) = page.GetByRole(AriaRole.List, PageGetByRoleOptions(Name = name, Exact = true))

let click (control: ILocator) = task {
    do! Assertions.Expect(control).ToBeVisibleAsync()
    do! control.ClickAsync()
}

let signInWithChatGpt (page: IPage) (_harness: TestHarness) (fake: FakeOpenAi) = task {
    let authorization = TaskCompletionSource<Uri>()
    let intercept (route: IRoute) : Task =
        authorization.TrySetResult(Uri route.Request.Url) |> ignore
        route.FulfillAsync(RouteFulfillOptions(Status = 200, ContentType = "text/html", Body = "<html><body>OpenAI</body></html>"))
    use! _ = page.RouteAsync("https://auth.openai.com/**", Func<IRoute, Task>(intercept))
    do! click (page.Locator("button[data-oai-siwc-button]").First)
    let! url = authorization.Task
    let query = System.Web.HttpUtility.ParseQueryString url.Query
    fake.Nonce <- query["nonce"]
    let state = Uri.EscapeDataString query["state"]
    let callback = query["redirect_uri"]
    let! _ = page.GotoAsync($"{callback}?state={state}&code=code-1&client_id={fake.ClientId}")
    return ()
}
