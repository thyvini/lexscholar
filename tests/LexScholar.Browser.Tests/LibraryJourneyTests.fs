module LexScholar.Browser.Tests.LibraryJourneyTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Server.Tests
open LexScholar.Client

// Real-browser library journeys (T033) against an isolated in-process host
// serving the WebAssembly client, with real fixtures from tests/Fixtures.
// Only Playwright's auto-waiting assertions are used; there are no sleeps.

let private withBrowser = Ui.withBrowser

let private openApp (browser: IBrowser) (harness: TestHarness) = task {
    let! page = Ui.openApp browser harness
    do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
    return page }

let private fixture name = Path.Combine(AppContext.BaseDirectory, "Fixtures", name)

let private createProject (page: IPage) (name: string) = task {
    do! page.GetByLabel(Texts.projectName).FillAsync(name)
    do! Ui.click (Ui.button (page.Locator("body")) Texts.createProject)
    do! Assertions.Expect(page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = name))).ToBeVisibleAsync() }

let private select (page: IPage) (name: string) = task {
    let row = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = name))
    do! Ui.click (Ui.button row Texts.select)
    do! Assertions.Expect(row.Locator("strong.active")).ToHaveTextAsync(Texts.active) }

let private importFile (page: IPage) (name: string) = task {
    let input = page.GetByLabel("Import a PDF")
    do! Assertions.Expect(input).ToBeVisibleAsync()
    do! input.SetInputFilesAsync(fixture name) }

let private libraryRows (page: IPage) = page.Locator("ul.documents li")
let private libraryRow (page: IPage) (name: string) = (libraryRows page).Filter(LocatorFilterOptions(HasText = name))
let private projectRows (page: IPage) = page.Locator("ul.project-documents li")
let private projectRow (page: IPage) (name: string) = (projectRows page).Filter(LocatorFilterOptions(HasText = name))

let private addToProject (page: IPage) (name: string) (role: string) = task {
    do! (libraryRow page name).GetByRole(AriaRole.Button, LocatorGetByRoleOptions(Name = $"Add as {role}")).ClickAsync()
    do! Assertions.Expect(projectRow page name).ToContainTextAsync(role) }

let private remove (page: IPage) (name: string) =
    (projectRow page name).GetByRole(AriaRole.Button, LocatorGetByRoleOptions(Name = "Remove")).ClickAsync()

let private noProjectDocuments (page: IPage) = page.GetByText(Texts.noDocuments)

/// Imports a fixture and waits for its success notice.
let private imported (page: IPage) (name: string) = task {
    do! importFile page name
    do! Assertions.Expect(page.GetByText($"Imported {name}.")).ToBeVisibleAsync() }

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T033"); Trait("Task", "T002"); Trait("Category", "Browser")>]
let ``importing adds to the shared library, reuses identical bytes and explains rejections`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        do! Assertions.Expect(page.GetByText("No documents in the library yet.")).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText(Texts.noProject)).ToBeVisibleAsync()
        do! imported page "valid.pdf"
        do! Assertions.Expect(libraryRows page).ToHaveCountAsync(1)
        do! Assertions.Expect(libraryRow page "valid.pdf").ToBeVisibleAsync()
        // Identical bytes under another name reuse the existing document.
        do! importFile page "valid-renamed.pdf"
        do! Assertions.Expect(page.GetByText("Already in the library: valid.pdf.")).ToBeVisibleAsync()
        do! Assertions.Expect(libraryRows page).ToHaveCountAsync(1)
        do! importFile page "not-a-pdf.txt"
        do! Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("That file is not a PDF. Choose a PDF file.")
        do! importFile page "distinct.pdf"
        do! Assertions.Expect(page.GetByText("Imported distinct.pdf.")).ToBeVisibleAsync()
        do! Assertions.Expect(libraryRows page).ToHaveCountAsync(2) })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T033"); Trait("Task", "T002"); Trait("Category", "Browser")>]
let ``switching projects shows each project's own documents and roles`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        do! createProject page "Alpha"
        do! createProject page "Beta"
        do! imported page "valid.pdf"
        do! imported page "distinct.pdf"

        do! select page "Alpha"
        do! Assertions.Expect(noProjectDocuments page).ToBeVisibleAsync()
        do! addToProject page "valid.pdf" "Focused Source"
        do! Assertions.Expect(projectRows page).ToHaveCountAsync(1)

        do! select page "Beta"
        // Nothing from Alpha is shown under Beta.
        do! Assertions.Expect(noProjectDocuments page).ToBeVisibleAsync()
        do! Assertions.Expect(projectRows page).ToHaveCountAsync(0)
        do! addToProject page "valid.pdf" "Reference Source"
        do! addToProject page "distinct.pdf" "Focused Source"
        do! Assertions.Expect(projectRows page).ToHaveCountAsync(2)

        do! select page "Alpha"
        do! Assertions.Expect(projectRows page).ToHaveCountAsync(1)
        do! Assertions.Expect(projectRow page "valid.pdf").ToContainTextAsync("Focused Source")
        do! Assertions.Expect(projectRow page "distinct.pdf").ToHaveCountAsync(0)

        do! select page "Beta"
        do! Assertions.Expect(projectRows page).ToHaveCountAsync(2)
        do! Assertions.Expect(projectRow page "valid.pdf").ToContainTextAsync("Reference Source")
        do! Assertions.Expect(projectRow page "distinct.pdf").ToContainTextAsync("Focused Source") })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T033"); Trait("Task", "T002"); Trait("Category", "Browser")>]
let ``removing associations keeps the shared document and other projects' associations`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        do! createProject page "Alpha"
        do! createProject page "Beta"
        do! imported page "valid.pdf"
        do! select page "Alpha"
        do! addToProject page "valid.pdf" "Focused Source"
        do! select page "Beta"
        do! addToProject page "valid.pdf" "Reference Source"

        do! remove page "valid.pdf"
        do! Assertions.Expect(noProjectDocuments page).ToBeVisibleAsync()
        do! Assertions.Expect(libraryRow page "valid.pdf").ToBeVisibleAsync()

        do! select page "Alpha"
        do! Assertions.Expect(projectRow page "valid.pdf").ToContainTextAsync("Focused Source")
        // The final association: the document stays in the shared library.
        do! remove page "valid.pdf"
        do! Assertions.Expect(noProjectDocuments page).ToBeVisibleAsync()
        do! Assertions.Expect(libraryRows page).ToHaveCountAsync(1)
        do! addToProject page "valid.pdf" "Reference Source" })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T002"); Trait("Category", "Browser")>]
let ``workspace groups sources and opens the library in the main column`` (browserName: string) =
    Ui.withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = Ui.openApp browser harness
        do! Assertions.Expect(page.GetByTestId("main")).ToContainTextAsync(Texts.chooseDocument)
        let! columns = page.Locator(".workspace").EvaluateAsync<string>("element => getComputedStyle(element).gridTemplateColumns")
        Assert.Equal(3, columns.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)
        do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
        do! Assertions.Expect(page.Locator(".breadcrumb")).ToHaveTextAsync(Texts.library)
        do! createProject page "Alpha"
        do! createProject page "Beta"
        do! select page "Alpha"
        do! Ui.click (Ui.button (page.Locator("body")) Texts.addDocuments)
        do! Assertions.Expect(page.GetByTestId("main").GetByLabel("Import a PDF")).ToBeVisibleAsync()
        do! imported page "valid.pdf"
        do! imported page "distinct.pdf"
        do! addToProject page "valid.pdf" "Focused Source"
        do! addToProject page "distinct.pdf" "Reference Source"
        let focused = Ui.listNamed page Texts.focusedSources
        let reference = Ui.listNamed page Texts.referenceSources
        do! Assertions.Expect(focused).ToContainTextAsync("valid.pdf")
        do! Assertions.Expect(focused).Not.ToContainTextAsync("distinct.pdf")
        do! Assertions.Expect(reference).ToContainTextAsync("distinct.pdf")
        do! Assertions.Expect(reference).Not.ToContainTextAsync("valid.pdf")
        do! Assertions.Expect(page.Locator(".project-count")).ToHaveTextAsync("2")
        do! Assertions.Expect(page.Locator(".document-column")).ToContainTextAsync(Texts.documentCount 2)
        do! Assertions.Expect(page.Locator(".library-count")).ToHaveTextAsync("2")
        do! (projectRow page "valid.pdf").GetByRole(AriaRole.Button, LocatorGetByRoleOptions(Name = "Open", Exact = true)).ClickAsync()
        do! Assertions.Expect(page.GetByTestId("main").GetByRole(AriaRole.Heading, LocatorGetByRoleOptions(Name = "valid.pdf", Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.Locator(".breadcrumb")).ToContainTextAsync("Alpha / valid.pdf")
        do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
        do! Assertions.Expect(page.GetByTestId("main").GetByLabel("Import a PDF")).ToBeVisibleAsync()
        do! Assertions.Expect(page.Locator(".breadcrumb")).ToHaveTextAsync($"Alpha / {Texts.library}")
        do! Assertions.Expect(page.Locator(".project-documents li.selected")).ToHaveCountAsync(0)
        // Inactive counts are intentionally deferred; only Alpha has a count.
        do! Assertions.Expect(page.Locator(".project-count")).ToHaveCountAsync(1)
    })
