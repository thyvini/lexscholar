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
        let existing = UriBuilder(harness.Client.BaseAddress)
        existing.Host <- "localhost"
        let! _ = another.GotoAsync(existing.Uri.ToString())
        do! Assertions.Expect(another.Locator(".workspace[data-ready='True']")).ToBeVisibleAsync()
        do! Assertions.Expect(another.GetByText(Texts.welcome, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        do! Assertions.Expect(another.Locator("ul.projects li")).ToContainTextAsync("Pesquisa")
        Assert.Equal("127.0.0.1", Uri(another.Url).Host)
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
        Assert.Equal("127.0.0.1", Uri(page.Url).Host)
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
let ``already signed-in onboarding does not authorize again after finish and sign-out`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi()
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! page = openFirstRun browser harness
        do! page.GetByLabel(Texts.projectName).FillAsync("Pesquisa")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Ui.signInWithChatGpt page harness fake
        do! Assertions.Expect(page.Locator(".onboarding").GetByText(Texts.signedIn, LocatorGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        let! _ = page.ReloadAsync()
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
let ``finish while destinations are still loading does not authorize later`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! context = browser.NewContextAsync(BrowserNewContextOptions(BaseURL = harness.Client.BaseAddress.ToString()))
        let! page = context.NewPageAsync()
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
        let! _ = page.GotoAsync("/index.html")
        do! arrived.Task
        try
            do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
            do! page.GetByLabel(Texts.projectName).FillAsync("Pesquisa")
            do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
            do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
            do! Ui.click (Ui.button (page.Locator("body")) Texts.finishOnboarding)
            do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        finally
            release.TrySetResult() |> ignore
        do! Assertions.Expect(Ui.button (page.Locator(".top-bar")) Texts.signIn).ToBeVisibleAsync()
        do! Assertions.Expect(Ui.button (page.Locator(".top-bar")) Texts.signIn).ToBeEnabledAsync()
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``interrupted initial localhost redirect does not finish onboarding when going back`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let address = UriBuilder(harness.Client.BaseAddress)
        address.Host <- "localhost"
        let! context = browser.NewContextAsync()
        let! page = context.NewPageAsync()
        let intercept (route: IRoute) =
            route.FulfillAsync(RouteFulfillOptions(Status = 200, ContentType = "text/html", Body = "<html><body>Interrupted handoff</body></html>"))
        use! _ = page.RouteAsync(harness.Client.BaseAddress.ToString() + "**", Func<IRoute, System.Threading.Tasks.Task>(intercept))
        let! _ = page.GotoAsync("data:text/html,<html><body>Previous page</body></html>")
        let! _ = page.GotoAsync(address.Uri.ToString())
        do! Assertions.Expect(page.GetByText("Interrupted handoff", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! page.UnrouteAsync(harness.Client.BaseAddress.ToString() + "**")
        let! _ = page.GoBackAsync()
        do! Assertions.Expect(page.GetByText("Previous page", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        let! _ = page.GoForwardAsync()
        let! _ = page.ReloadAsync()
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        Assert.Equal("127.0.0.1", Uri(page.Url).Host)
        do! Assertions.Expect(page.GetByLabel(Texts.projectName)).ToBeVisibleAsync()
        do! noMenus page
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``interrupted completion navigation cannot leave the other loopback origin in setup`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use fake = new FakeOpenAi()
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let source = UriBuilder(harness.Client.BaseAddress)
        source.Host <- "localhost"
        let! context = browser.NewContextAsync()
        let! page = context.NewPageAsync()
        let! _ = page.GotoAsync(source.Uri.ToString())
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! page.GetByLabel(Texts.projectName).FillAsync("Pesquisa concluída")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa concluída", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Ui.signInWithChatGpt page harness fake
        do! Assertions.Expect(page.GetByText(Texts.signedIn, PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        // Interrupt any completion synchronization before the other origin's app runs.
        let interrupt (route: IRoute) =
            route.FulfillAsync(RouteFulfillOptions(Status = 200, ContentType = "text/html", Body = "<html><body>Interrupted completion</body></html>"))
        use! _ = page.RouteAsync(source.Uri.ToString() + "**", Func<IRoute, System.Threading.Tasks.Task>(interrupt))
        do! Ui.click (Ui.button (page.Locator("body")) Texts.finishOnboarding)
        let! _ = page.GotoAsync(Uri(harness.Client.BaseAddress, "index.html").ToString())
        do! Assertions.Expect(page.Locator(".workspace[data-ready='True']")).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText(Texts.welcome, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        do! page.UnrouteAsync(source.Uri.ToString() + "**")
        let! _ = page.GotoAsync(source.Uri.ToString())
        do! Assertions.Expect(page.Locator(".workspace[data-ready='True']")).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText(Texts.welcome, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``localhost setup redirect replaces history so Back cannot bypass unfinished setup`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let source = UriBuilder(harness.Client.BaseAddress)
        source.Host <- "localhost"
        let! context = browser.NewContextAsync()
        let! page = context.NewPageAsync()
        let! _ = page.GotoAsync("data:text/html,<html><body>Previous page</body></html>")
        let! _ = page.GotoAsync(source.Uri.ToString())
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! page.GetByLabel(Texts.projectName).FillAsync("Pesquisa em andamento")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa em andamento", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        let! _ = page.GoBackAsync()
        do! Assertions.Expect(page.GetByText("Previous page", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        let! _ = page.GoForwardAsync()
        let! _ = page.ReloadAsync()
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa em andamento", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! noMenus page
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T014"); Trait("Category", "Browser")>]
let ``localhost revisits and Back respect active and completed setup in the same tab`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openFirstRun browser harness
        do! page.GetByLabel(Texts.projectName).FillAsync("Pesquisa revisitada")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa revisitada", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        let source = UriBuilder(harness.Client.BaseAddress)
        source.Host <- "localhost"
        let! _ = page.GotoAsync(source.Uri.ToString())
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText(Texts.projectCreated "Pesquisa revisitada", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! noMenus page
        let! _ = page.GoBackAsync()
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! noMenus page
        let! _ = page.GoForwardAsync()
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.welcome, Exact = true))).ToBeVisibleAsync()
        do! noMenus page
        do! Ui.click (Ui.button (page.Locator("body")) Texts.finishOnboarding)
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        let! _ = page.GotoAsync(source.Uri.ToString())
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        do! Assertions.Expect(page.GetByText(Texts.welcome, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        let! _ = page.GoBackAsync()
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        do! Assertions.Expect(page.GetByText(Texts.welcome, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        let! _ = page.GoBackAsync()
        do! Assertions.Expect(page.Locator("aside")).ToHaveCountAsync(2)
        do! Assertions.Expect(page.GetByText(Texts.welcome, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
    })
