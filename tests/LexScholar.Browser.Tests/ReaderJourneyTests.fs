module LexScholar.Browser.Tests.ReaderJourneyTests

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Server.Tests

// Real-browser reader and role journeys (T040) against an isolated in-process
// host serving the WebAssembly client. The PDF itself is rendered by the
// browser's native viewer inside the frame; its controls and page navigation
// are browser behavior and are checked manually (see the T040 evidence).
// Only Playwright's auto-waiting assertions are used; there are no sleeps.

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

let private fixture name = Path.Combine(AppContext.BaseDirectory, "Fixtures", name)

let private button (scope: ILocator) (name: string) =
    scope.GetByRole(AriaRole.Button, LocatorGetByRoleOptions(Name = name, Exact = true))

/// Asserts the control is visible before using it, so a missing UI fails on an assertion.
let private click (control: ILocator) = task {
    do! Assertions.Expect(control).ToBeVisibleAsync()
    do! control.ClickAsync() }

let private projectRow (page: IPage) (name: string) = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = name))
let private libraryRow (page: IPage) (name: string) = page.Locator("ul.documents li").Filter(LocatorFilterOptions(HasText = name))
let private documentRow (page: IPage) (name: string) = page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = name))
let private frame (page: IPage) = page.Locator("iframe.reader-frame")

let private createProject (page: IPage) (name: string) = task {
    do! page.GetByLabel("Project name").FillAsync(name)
    do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Create project")).ClickAsync()
    do! Assertions.Expect(projectRow page name).ToBeVisibleAsync() }

let private select (page: IPage) (name: string) = task {
    do! click (button (projectRow page name) "Select")
    do! Assertions.Expect((projectRow page name).Locator("strong.active")).ToHaveTextAsync("Active") }

let private import (page: IPage) (name: string) = task {
    let input = page.GetByLabel("Import a PDF")
    do! Assertions.Expect(input).ToBeVisibleAsync()
    do! input.SetInputFilesAsync(fixture name)
    do! Assertions.Expect(page.GetByText($"Imported {name}.")).ToBeVisibleAsync() }

let private addToProject (page: IPage) (name: string) (role: string) = task {
    do! click (button (libraryRow page name) $"Add as {role}")
    do! Assertions.Expect(documentRow page name).ToContainTextAsync(role) }

let private openDocument (page: IPage) (name: string) = click (button (documentRow page name) "Open")

/// Two projects, Alpha active, with valid.pdf imported and associated with
/// Alpha as Focused Source and Beta as Reference Source.
let private arrange (page: IPage) = task {
    do! createProject page "Alpha"
    do! createProject page "Beta"
    do! import page "valid.pdf"
    do! select page "Beta"
    do! addToProject page "valid.pdf" "Reference Source"
    do! select page "Alpha"
    do! addToProject page "valid.pdf" "Focused Source" }

/// The content URL the API reports for the only library document.
let private contentUrl (harness: TestHarness) =
    use response = harness.Client.GetAsync("/api/documents").GetAwaiter().GetResult()
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    json.RootElement.[0].GetProperty("contentUrl").GetString()

let private storedOriginal (harness: TestHarness) =
    Directory.GetFiles(Path.Combine(harness.Root, "originals")) |> Array.exactlyOne

/// Puts the stored original in the named unavailable state.
let private makeUnavailable (condition: string) (path: string) =
    match condition with
    | "missing" -> File.Delete(path)
    | "shorter" ->
        let bytes = File.ReadAllBytes(path)
        File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        File.WriteAllBytes(path, bytes |> Array.take (bytes.Length - 1))
        File.SetUnixFileMode(path, UnixFileMode.UserRead)
    | "unreadable" ->
        File.SetUnixFileMode(path, UnixFileMode.None)
        let readable =
            try
                File.OpenRead(path).Dispose()
                true
            with :? UnauthorizedAccessException -> false
        Assert.False(readable, "The original is still readable with mode 000 (running as root?), so the unreadable case would not be exercised.")
    | other -> failwithf "Unknown condition %s." other

let unavailableCases : TheoryData<string, string> =
    TheoryData<string, string>(
        [ for browser in [ "chromium"; "firefox" ] do
            for condition in [ "missing"; "shorter"; "unreadable" ] -> struct (browser, condition) ])

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T040"); Trait("Category", "Browser")>]
let ``opening a project document shows it in the browser's native viewer`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        do! arrange page
        let url = contentUrl harness
        do! openDocument page "valid.pdf"
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = "Reader"))).ToBeVisibleAsync()
        do! Assertions.Expect(frame page).ToHaveAttributeAsync("src", url)
        do! Assertions.Expect(frame page).ToHaveAttributeAsync("title", "valid.pdf")
        let link = page.GetByRole(AriaRole.Link, PageGetByRoleOptions(Name = "Open in new tab"))
        do! Assertions.Expect(link).ToHaveAttributeAsync("href", url)
        do! Assertions.Expect(link).ToHaveAttributeAsync("target", "_blank")
        do! Assertions.Expect(link).ToHaveAttributeAsync("rel", "noopener")
        // The frame's URL serves the stored PDF inline, for the browser's viewer.
        let! response = page.APIRequest.GetAsync(url)
        Assert.Equal(200, response.Status)
        Assert.Equal("application/pdf", response.Headers.["content-type"])
        Assert.Equal("inline", response.Headers.["content-disposition"])
        let! body = response.BodyAsync()
        Assert.Equal<byte array>(File.ReadAllBytes(fixture "valid.pdf"), body)
        do! click (button (page.Locator("section.reader")) "Close")
        do! Assertions.Expect(frame page).ToHaveCountAsync(0) })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T040"); Trait("Category", "Browser")>]
let ``changing a role in one project leaves the other project's role unchanged`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        do! arrange page
        do! click (button (documentRow page "valid.pdf") "Make Reference Source")
        do! Assertions.Expect(documentRow page "valid.pdf").ToContainTextAsync("Reference Source")
        do! Assertions.Expect(button (documentRow page "valid.pdf") "Make Focused Source").ToBeVisibleAsync()
        do! select page "Beta"
        do! Assertions.Expect(documentRow page "valid.pdf").ToContainTextAsync("Reference Source")
        do! click (button (documentRow page "valid.pdf") "Make Focused Source")
        do! Assertions.Expect(documentRow page "valid.pdf").ToContainTextAsync("Focused Source")
        do! select page "Alpha"
        do! Assertions.Expect(documentRow page "valid.pdf").ToContainTextAsync("Reference Source") })

[<Theory; MemberData(nameof unavailableCases); Trait("Task", "T040"); Trait("Category", "Browser")>]
let ``an unavailable document shows a clear error and keeps its records`` (browserName: string, condition: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        do! arrange page
        makeUnavailable condition (storedOriginal harness)
        do! openDocument page "valid.pdf"
        do! Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync(
                "This document's stored file is missing, unreadable or not the recorded size. Its library record and project associations are unchanged.")
        do! Assertions.Expect(frame page).ToHaveCountAsync(0)
        // No repair or deletion: the record and both associations remain.
        do! Assertions.Expect(libraryRow page "valid.pdf").ToBeVisibleAsync()
        do! Assertions.Expect(documentRow page "valid.pdf").ToContainTextAsync("Focused Source")
        do! select page "Beta"
        do! Assertions.Expect(documentRow page "valid.pdf").ToContainTextAsync("Reference Source") })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T040"); Trait("Category", "Browser")>]
let ``switching projects closes the reader`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        do! arrange page
        do! openDocument page "valid.pdf"
        do! Assertions.Expect(frame page).ToHaveCountAsync(1)
        do! select page "Beta"
        do! Assertions.Expect(frame page).ToHaveCountAsync(0)
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = "Reader"))).ToHaveCountAsync(0) })
