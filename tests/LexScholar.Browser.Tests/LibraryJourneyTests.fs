module LexScholar.Browser.Tests.LibraryJourneyTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Server.Tests

// Real-browser library journeys (T033) against an isolated in-process host
// serving the WebAssembly client, with real fixtures from tests/Fixtures.
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

let private createProject (page: IPage) (name: string) = task {
    do! page.GetByLabel("Project name").FillAsync(name)
    do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Create project")).ClickAsync()
    do! Assertions.Expect(page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = name))).ToBeVisibleAsync() }

let private select (page: IPage) (name: string) = task {
    let row = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = name))
    do! row.GetByRole(AriaRole.Button, LocatorGetByRoleOptions(Name = "Select")).ClickAsync()
    do! Assertions.Expect(row.Locator("strong.active")).ToHaveTextAsync("Active") }

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

let private noProjectDocuments (page: IPage) = page.GetByText("No documents are associated with this project.")

/// Imports a fixture and waits for its success notice.
let private imported (page: IPage) (name: string) = task {
    do! importFile page name
    do! Assertions.Expect(page.GetByText($"Imported {name}.")).ToBeVisibleAsync() }

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T033"); Trait("Category", "Browser")>]
let ``importing adds to the shared library, reuses identical bytes and explains rejections`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        do! Assertions.Expect(page.GetByText("No documents in the library yet.")).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText("Select a project to see its documents.")).ToBeVisibleAsync()
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

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T033"); Trait("Category", "Browser")>]
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

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T033"); Trait("Category", "Browser")>]
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
