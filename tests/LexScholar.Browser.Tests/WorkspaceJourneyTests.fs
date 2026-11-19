module LexScholar.Browser.Tests.WorkspaceJourneyTests

open System
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Client
open LexScholar.Server.Tests
open LexScholar.Browser.Tests.Ui

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T004"); Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``opening a document shows its summary request without an embedded reader`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        let contentRequests = System.Collections.Concurrent.ConcurrentQueue<string>()
        page.Request.Add(fun request ->
            if request.Url.Contains("/api/documents/") && request.Url.Contains("/content") then
                contentRequests.Enqueue(request.Url))
        do! SummaryJourneyTests.arrange page
        do! Assertions.Expect(page.GetByTestId("main").Locator("iframe")).ToHaveCountAsync(0)
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.requestSummary))).ToBeVisibleAsync()
        Assert.Empty(contentRequests)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T004"); Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``changing a role moves the document and leaves the other project unchanged`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        do! SummaryJourneyTests.arrange page
        let documentRow = page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))
        do! click (button documentRow Texts.makeReference)
        do! Assertions.Expect(documentRow).ToContainTextAsync(Texts.referenceSource)
        do! page.GetByLabel(Texts.projectName).FillAsync("Beta")
        do! click (button (page.Locator("body")) Texts.createProject)
        let beta = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = "Beta"))
        do! click (button beta Texts.select)
        // Selection changes the sidebar's loading status and button positions.
        // Wait for its rendered result before clicking library navigation.
        do! Assertions.Expect(beta).ToHaveClassAsync("selected")
        do! click (button (page.Locator("body")) Texts.library)
        let libraryRow = page.Locator("ul.documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))
        do! click (button libraryRow Texts.addReference)
        do! Assertions.Expect(documentRow).ToContainTextAsync(Texts.referenceSource)
        do! click (button documentRow Texts.makeFocused)
        do! Assertions.Expect(documentRow).ToContainTextAsync(Texts.focusedSource)
        let alpha = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = "Alpha"))
        do! click (button alpha Texts.select)
        do! Assertions.Expect(documentRow).ToContainTextAsync(Texts.referenceSource)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T006"); Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``changing a role moves the document while its summary stays visible`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        let answer = """{"points":[{"text":"Retained point","pages":[1]}]}"""
        use fake = new FakeOpenAi(ResponsesScript.Completed answer)
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! page = openApp browser harness
        do! SummaryJourneyTests.arrange page
        do! signInWithChatGpt page harness fake
        do! click (button (page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))) Texts.openDocument)
        do! click (button (page.Locator("body")) Texts.summarize)
        do! click (button (page.GetByRole(AriaRole.Alertdialog)) Texts.sendAndSummarize)
        let panel = page.Locator("section.summary")
        do! Assertions.Expect(panel).ToContainTextAsync("Retained point")
        let focused = listNamed page Texts.focusedSources
        let reference = listNamed page Texts.referenceSources
        let row (list: ILocator) = list.Locator("li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))
        do! Assertions.Expect(row focused).ToHaveCountAsync(1)
        do! click (button (row focused) Texts.makeReference)
        do! Assertions.Expect(row focused).ToHaveCountAsync(0)
        do! Assertions.Expect(row reference).ToHaveCountAsync(1)
        do! Assertions.Expect(panel).ToContainTextAsync("Retained point")
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T003"); Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``PDF and citation links open the same document in fresh tabs and keep the summary visible`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        let answer = """{"points":[{"text":"Range","pages":[1,2]},{"text":"Single","pages":[3]},{"text":"Separate","pages":[3,5]},{"text":"Last","pages":[12]}]}"""
        use fake = new FakeOpenAi(ResponsesScript.Completed answer)
        use harness = TestHarness.Start(serveClient = true, summaryHandler = fake)
        let! page = openApp browser harness
        do! SummaryJourneyTests.arrange page
        do! signInWithChatGpt page harness fake
        do! click (button (page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "article-en.pdf"))) Texts.openDocument)
        do! click (button (page.Locator("body")) Texts.summarize)
        do! click (button (page.GetByRole(AriaRole.Alertdialog)) Texts.sendAndSummarize)
        let panel = page.Locator("section.summary")
        do! Assertions.Expect(panel).ToContainTextAsync(Texts.summaryReady)
        use! response = harness.Client.GetAsync("/api/documents")
        let! body = response.Content.ReadAsStringAsync()
        use json = JsonDocument.Parse(body)
        let content = json.RootElement.[0].GetProperty("contentUrl").GetString()
        let points = panel.Locator("ul.summary-points li")
        do! Assertions.Expect(points.Nth(2).GetByRole(AriaRole.Link)).ToHaveTextAsync([| "p. 3"; "p. 5" |])
        let opened = ResizeArray<IPage>()
        let requests = System.Collections.Concurrent.ConcurrentQueue<string>()
        for name, fragment in [ "p. 3", "#page=3"; "pp. 1–2", "#page=1"; "p. 12", "#page=12"; "p. 3", "#page=3"; Texts.openPdf, "" ] do
            let control = link (page.GetByTestId("main")) name |> fun links -> links.First
            do! Assertions.Expect(control).ToHaveAttributeAsync("href", content + fragment)
            do! Assertions.Expect(control).ToHaveAttributeAsync("target", "_blank")
            do! Assertions.Expect(control).ToHaveAttributeAsync("rel", "noopener")
            let requested = TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)
            let handler = EventHandler<IRequest>(fun _ request ->
                if request.IsNavigationRequest && request.Url.Contains("/api/documents/") then
                    requests.Enqueue(request.Url)
                    requested.TrySetResult(request.Url) |> ignore)
            page.Context.Request.AddHandler(handler)
            let! popup = page.Context.RunAndWaitForPageAsync(fun () -> control.ClickAsync())
            let! url = requested.Task.WaitAsync(TimeSpan.FromSeconds(10.0))
            page.Context.Request.RemoveHandler(handler)
            Assert.Equal(Uri(harness.Client.BaseAddress, content).AbsoluteUri, url)
            Assert.DoesNotContain(popup, opened)
            opened.Add(popup)
            printfn "%s %s: popup URL %s; href %s" browserName name popup.Url (content + fragment)
            // Bundled browsers download PDFs; href retains the fragment if navigation aborts.
            do! Assertions.Expect(panel).ToContainTextAsync(Texts.generated)
            do! Assertions.Expect(panel.Locator(".summary-context")).ToContainTextAsync("ChatGPT plan")
            do! Assertions.Expect(points).ToHaveCountAsync(4)
        Assert.Equal(5, requests.Count)
        Assert.All(requests, fun url -> Assert.Equal(Uri(harness.Client.BaseAddress, content).AbsoluteUri, url))
    })
