module LexScholar.Browser.Tests.OnboardingJourneyTests

open System
open Microsoft.Playwright
open Xunit
open LexScholar.Client
open LexScholar.Server.Tests

let private openFirstRun (browser: IBrowser) (harness: TestHarness) = task {
    let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
    let! page = context.NewPageAsync()
    let! _ = page.GotoAsync("/")
    do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
    return page
}

let private noMenus (page: IPage) = task {
    do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(0)
    do! Assertions.Expect(page.GetByText(Texts.chooseDocument, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
}

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``onboarding stays through project import sign-in and reload until finish`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi()
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! page = openFirstRun browser harness
        do! noMenus page
        do! page.GetByLabel(Texts.projectName).FillAsync("Dissertação")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Dissertação", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! noMenus page
        let! _ = page.ReloadAsync()
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! page.GetByLabel(Texts.choosePdfs).SetInputFilesAsync(IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
        do! Assertions.Expect(page.GetByText(Texts.imported "valid.pdf", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Ui.signInWithChatGpt page harness fake
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText(Texts.signedIn, PageGetByTextOptions(Exact = true)).First).ToBeVisibleAsync()
        do! noMenus page
        do! Ui.click (Ui.button (page.Locator("body")) Texts.finishOnboarding)
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        let! _ = page.ReloadAsync()
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        do! Assertions.Expect(page.GetByText(Texts.welcome, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``onboarding can skip optional setup and finish with no project`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openFirstRun browser harness
        do! noMenus page
        do! Ui.click (Ui.button (page.Locator("body")) Texts.skipOnboarding)
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        let! _ = page.ReloadAsync()
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``project setup can finish without import or sign-in and existing workspaces bypass onboarding`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openFirstRun browser harness
        do! page.GetByLabel(Texts.projectName).FillAsync("Pesquisa")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! noMenus page
        do! Ui.click (Ui.button (page.Locator("body")) Texts.finishOnboarding)
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        // A fresh browser session has no onboarding marker, but the workspace has a project.
        let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
        let! another = context.NewPageAsync()
        let! _ = another.GotoAsync("/")
        do! Assertions.Expect(another.Locator(".workspace[data-ready='True']")).ToBeVisibleAsync()
        do! Assertions.Expect(another.GetByText(Texts.welcome, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        do! Assertions.Expect(another.Locator("ul.projects li")).ToContainTextAsync("Pesquisa")
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``localhost onboarding survives the numeric loopback sign-in callback`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi()
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let address = UriBuilder(harness.Client.BaseAddress)
        address.Host <- "localhost"
        let! context = browser.NewContextAsync()
        let! page = context.NewPageAsync()
        let! _ = page.GotoAsync(address.Uri.ToString())
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! page.GetByLabel(Texts.projectName).FillAsync("Pesquisa local")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa local", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Ui.signInWithChatGpt page harness fake
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText(Texts.signedIn, PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        Assert.Equal("127.0.0.1", Uri(page.Url).Host)
        do! noMenus page
        let! _ = page.ReloadAsync()
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa local", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! noMenus page
        do! Ui.click (Ui.button (page.Locator("body")) Texts.finishOnboarding)
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        let! _ = page.GotoAsync(address.Uri.ToString())
        do! Assertions.Expect(page.Locator(".workspace[data-ready='True']")).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText(Texts.welcome, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``onboarding account failure offers retry and recovers sign-in`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
        let! page = context.NewPageAsync()
        let fail (route: IRoute) =
            route.FulfillAsync(RouteFulfillOptions(Status = 503, ContentType = "application/json", Body = "{}"))
        use! _ = page.RouteAsync("**/api/summary/destinations", Func<IRoute, System.Threading.Tasks.Task>(fail))
        let! _ = page.GotoAsync("/")
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync(Texts.chatGptUnavailable.Trim())
        do! noMenus page
        do! page.UnrouteAsync("**/api/summary/destinations")
        do! Ui.click (Ui.button (page.Locator(".onboarding")) Texts.retry)
        do! Assertions.Expect(Ui.button (page.Locator(".onboarding")) Texts.signIn).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0)
        do! noMenus page
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``already signed-in handoff does not retain authorization intent after finish and sign-out`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi()
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! page = Ui.openApp browser harness
        do! page.GetByLabel(Texts.projectName).FillAsync("Pesquisa")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.Locator("ul.projects li")).ToContainTextAsync("Pesquisa")
        do! Ui.signInWithChatGpt page harness fake
        do! Assertions.Expect(page.Locator(".top-bar").GetByText(Texts.signedIn, LocatorGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        // Another sign-in may have completed while a localhost handoff was navigating.
        let! _ = page.GotoAsync("/index.html?onboarding=sign-in")
        do! Assertions.Expect(page.Locator(".onboarding").GetByText(Texts.signedIn, LocatorGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Ui.click (Ui.button (page.Locator("body")) Texts.finishOnboarding)
        let bar = page.Locator(".top-bar")
        do! bar.GetByText(Texts.account, LocatorGetByTextOptions(Exact = true)).ClickAsync()
        do! Ui.click (Ui.button bar Texts.signOut)
        do! Assertions.Expect(Ui.button bar Texts.signIn).ToBeVisibleAsync()
        do! Assertions.Expect(Ui.button bar Texts.signIn).ToBeEnabledAsync()
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``finish cancels handoff sign-in while destinations are still loading`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = Ui.openApp browser harness
        do! page.GetByLabel(Texts.projectName).FillAsync("Pesquisa")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.Locator("ul.projects li")).ToContainTextAsync("Pesquisa")
        let arrived = System.Threading.Tasks.TaskCompletionSource()
        let release = System.Threading.Tasks.TaskCompletionSource()
        let intercept (route: IRoute) =
            route.FulfillAsync(RouteFulfillOptions(Status = 200, ContentType = "text/html", Body = "<html><body>Unexpected authorization</body></html>"))
        use! _ = page.RouteAsync("https://auth.openai.com/**", Func<IRoute, System.Threading.Tasks.Task>(intercept))
        let hold (route: IRoute) : System.Threading.Tasks.Task = task {
            arrived.TrySetResult() |> ignore
            do! release.Task
            do! route.ContinueAsync()
        }
        use! _ = page.RouteAsync("**/api/summary/destinations", Func<IRoute, System.Threading.Tasks.Task>(hold))
        let! _ = page.GotoAsync("/index.html?onboarding=sign-in")
        do! arrived.Task
        try
            do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
            do! Ui.click (Ui.button (page.Locator("body")) Texts.finishOnboarding)
            do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        finally
            release.TrySetResult() |> ignore
        do! Assertions.Expect(Ui.button (page.Locator(".top-bar")) Texts.signIn).ToBeVisibleAsync()
        do! Assertions.Expect(Ui.button (page.Locator(".top-bar")) Texts.signIn).ToBeEnabledAsync()
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``interrupted localhost handoff does not finish onboarding when going back`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let address = UriBuilder(harness.Client.BaseAddress)
        address.Host <- "localhost"
        let! context = browser.NewContextAsync()
        let! page = context.NewPageAsync()
        let! _ = page.GotoAsync(address.Uri.ToString())
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! page.GetByLabel(Texts.projectName).FillAsync("Pesquisa interrompida")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa interrompida", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        let intercept (route: IRoute) =
            route.FulfillAsync(RouteFulfillOptions(Status = 200, ContentType = "text/html", Body = "<html><body>Interrupted handoff</body></html>"))
        use! _ = page.RouteAsync(harness.Client.BaseAddress.ToString() + "**", Func<IRoute, System.Threading.Tasks.Task>(intercept))
        do! Ui.click (Ui.button (page.Locator("body")) Texts.signIn)
        do! Assertions.Expect(page.GetByText("Interrupted handoff", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        let! _ = page.GoBackAsync()
        let! _ = page.ReloadAsync()
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa interrompida", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! noMenus page
    })
