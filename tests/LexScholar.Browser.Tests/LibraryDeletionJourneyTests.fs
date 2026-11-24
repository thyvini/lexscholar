module LexScholar.Browser.Tests.LibraryDeletionJourneyTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Playwright
open LexScholar.Application
open LexScholar.Client
open LexScholar.Infrastructure
open LexScholar.Server
open LexScholar.Server.Tests
open Xunit

let private seed root =
    let first, second, keep = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    let workspace, p1, p2 = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    let now = DateTimeOffset.UtcNow
    DocumentStore.ensureDirectories root
    let bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
    use db = WorkspaceDb.create root
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = now)) |> ignore
    db.SaveChanges() |> ignore
    for id, name in [p1, "Projeto primeiro"; p2, "Projeto segundo"] do
        db.Add(ResearchProjectRow(Id = id, WorkspaceId = workspace, Name = name, NormalizedName = name.ToUpperInvariant(), CreatedUtc = now)) |> ignore
    for id, name in [first, "compartilhado.pdf"; second, "sem-projeto.pdf"; keep, "preservado.pdf"] do
        File.WriteAllBytes(Path.Combine(DocumentStore.originalsDirectory root, $"{id:N}"), bytes)
        db.Add(LibraryDocumentRow(Id = id, OriginalFileName = name, StorageKey = $"{id:N}", Sha256 = $"{id:N}",
            ByteLength = int64 bytes.Length, PageCount = 1, ImportedUtc = now)) |> ignore
    db.SaveChanges() |> ignore
    for id, role in [p1, "FocusedSource"; p2, "ReferenceSource"] do
        db.Add(ProjectDocumentRow(ProjectId = id, DocumentId = first, Role = role, AddedUtc = now)) |> ignore
    db.SaveChanges() |> ignore
    for id in [p1; p2] do
        SummaryRepository.save root { Project = id; Document = first }
            { DocumentId = first; Points = [{ Text = "Ponto de teste"; Pages = [1] }]; PagesCovered = [1]
              UnreadablePages = []; OmittedPoints = 0; GeneratedAt = now; ServiceHost = "local"; Model = "fixture" }
    first, second, keep, p1

let private openLibrary browser host = task {
    let! page = Ui.openApp browser host
    do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
    return page
}
let private button (page: IPage) text = page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = text, Exact = true))
let private select (page: IPage) name =
    page.GetByRole(AriaRole.Checkbox, PageGetByRoleOptions(Name = $"{Texts.deletionSelection}: {name}", Exact = true)).CheckAsync()

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T017"); Trait("Category", "Browser")>]
let ``cancel and changed shared impact require confirmation then delete shared and unassociated only`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use workspace = TemporaryWorkspace.Create()
        let mutable keep = Guid.Empty
        do!
            task {
                use host = TestHarness.Start(serveClient = true, workspace = workspace)
                let first, second, untouched, p1 = seed host.Root
                keep <- untouched
                let! page = openLibrary browser host
                let external = ResizeArray<string>()
                page.Request.Add(fun request -> if (Uri request.Url).Host <> "127.0.0.1" then external.Add(request.Url))
                do! select page "compartilhado.pdf"
                do! select page "sem-projeto.pdf"
                do! Ui.click (button page Texts.deleteSelection)
                let impact = page.GetByRole(AriaRole.Region, PageGetByRoleOptions(Name = Texts.deletionImpact))
                do! Assertions.Expect(impact.GetByText("Projeto primeiro", LocatorGetByTextOptions(Exact = false))).ToBeVisibleAsync()
                do! Assertions.Expect(impact.GetByText(Texts.deletionSummary, LocatorGetByTextOptions(Exact = false))).ToHaveCountAsync(2)
                do! Assertions.Expect(impact.GetByText(Texts.deletionNoProjects)).ToBeVisibleAsync()
                do! Assertions.Expect(impact.GetByText(Texts.deletionCopies)).ToBeVisibleAsync()
                do! Ui.click (button page Texts.deletionCancel)
                Assert.Equal(3, (LibraryRepository.list host.Root).Length)
                Assert.Equal(2, (SummaryRepository.listKeys host.Root).Length)
                do
                    use db = WorkspaceDb.create host.Root
                    Assert.Empty(db.Set<DeletionOperationRow>())
                do! Ui.click (button page Texts.deleteSelection)
                do! Assertions.Expect(impact).ToBeVisibleAsync()
                do
                    use db = WorkspaceDb.create host.Root
                    db.Set<ResearchProjectRow>().Find(p1).Name <- "Projeto atualizado"
                    db.SaveChanges() |> ignore
                do! Ui.click (button page Texts.deletionConfirm)
                do! Assertions.Expect(page.GetByText(Texts.deletionChanged)).ToBeVisibleAsync()
                do! Assertions.Expect(impact.GetByText("Projeto atualizado", LocatorGetByTextOptions(Exact = false))).ToBeVisibleAsync()
                Assert.Equal(3, (LibraryRepository.list host.Root).Length)
                do! Ui.click (button page Texts.deletionConfirm)
                do! Assertions.Expect(page.GetByText(Texts.deletionComplete, PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
                for id in [first; second] do
                    Assert.False(File.Exists(Path.Combine(DocumentStore.originalsDirectory host.Root, $"{id:N}")))
                Assert.Empty(SummaryRepository.listKeys host.Root)
                Assert.Equal(untouched, (LibraryRepository.list host.Root).Head.Id)
                do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
                do! page.ReloadAsync() :> Task
                do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
                do! Assertions.Expect(page.GetByText(Texts.deletionComplete, PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
                Assert.Empty(external)
                do! page.CloseAsync()
            }
        use restarted = TestHarness.Start(workspace = workspace)
        Assert.Equal(keep, (LibraryRepository.list restarted.Root).Head.Id)
        Assert.Empty(SummaryRepository.listKeys restarted.Root)
    })

[<Theory; InlineData("chromium", true); InlineData("firefox", true);
  InlineData("chromium", false); InlineData("firefox", false);
  Trait("Task", "T017"); Trait("Category", "Browser")>]
let ``lost confirmation answer survives reload and retries the identical UUID`` browserName admitted =
    Ui.withBrowser browserName (fun browser -> task {
        use host = TestHarness.Start(serveClient = true)
        seed host.Root |> ignore
        let! page = openLibrary browser host
        do! select page "sem-projeto.pdf"
        do! Ui.click (button page Texts.deleteSelection)
        let requests = ResizeArray<string>()
        use! _ = page.RouteAsync("**/api/library/deletions", Func<IRoute, Task>(fun route -> task {
            if route.Request.Method = "POST" then
                requests.Add(route.Request.PostData)
                if admitted then
                    let! _ = route.FetchAsync()
                    ()
                do! route.AbortAsync("connectionreset")
            else do! route.ContinueAsync() }))
        do! Ui.click (button page Texts.deletionConfirm)
        do! Assertions.Expect(page.GetByText(Texts.deletionUnknown)).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText(Texts.deletionFailure)).ToBeVisibleAsync()
        do! Assertions.Expect(button page Texts.deletionRepeat).ToBeEnabledAsync()
        do! Assertions.Expect(button page Texts.deleteSelection).ToBeDisabledAsync()
        if not admitted then
            use db = WorkspaceDb.create host.Root
            Assert.Empty(db.Set<DeletionOperationRow>())
            do! Assertions.Expect(page.GetByText(Texts.deletionComplete, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        do! page.UnrouteAsync("**/api/library/deletions")
        do! page.ReloadAsync() :> Task
        do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
        do! Assertions.Expect(page.GetByText(Texts.deletionUnknown)).ToBeVisibleAsync()
        let sent = page.WaitForRequestAsync(fun request -> request.Method = "POST" && request.Url.EndsWith("/api/library/deletions"))
        do! Ui.click (button page Texts.deletionRepeat)
        let! repeated = sent
        Assert.Equal(Assert.Single(requests), repeated.PostData)
        do! Assertions.Expect(page.GetByText(Texts.deletionComplete, PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(button page Texts.deleteSelection).ToBeDisabledAsync() // no selection remains
        use db = WorkspaceDb.create host.Root
        Assert.Single(db.Set<DeletionOperationRow>()) |> ignore
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T017"); Trait("Category", "Browser")>]
let ``partial results remain visible across navigation and retry never claims early success`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        let mutable fail = true
        use host = TestHarness.Start(serveClient = true, configure = fun app ->
            let root = app.Configuration["LexScholar:DataDirectory"]
            let coordination = app.Services.GetRequiredService<LibraryCoordination>()
            let jobs = (app :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
            let runItem operation document =
                let finish =
                    LibraryDeletionRepository.finishOriginalWith ignore
                        (fun key -> if fail then raise (IOException("fixture failure")) else DocumentStore.removeOriginal root key)
                        coordination root
                LibraryDeletion.processItem coordination jobs (LibraryDeletionRepository.hasItem root)
                    (LibraryDeletionRepository.removeMetadata coordination root) finish operation document app.Lifetime.ApplicationStopping
            LibraryDeletionEndpoints.mapWithProcessingForTests app root runItem |> ignore)
        seed host.Root |> ignore
        let! page = openLibrary browser host
        do! select page "sem-projeto.pdf"
        do! Ui.click (button page Texts.deleteSelection)
        do! Ui.click (button page Texts.deletionConfirm)
        do! Assertions.Expect(page.GetByText(Texts.deletionPartial, PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText("sem-projeto.pdf — Não concluído", PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Assertions.Expect(page.GetByText(Texts.deletionComplete, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
        do! Ui.click (button page "Projeto primeiro")
        do! Assertions.Expect(page.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = Texts.library, Exact = true))).ToHaveCountAsync(0)
        do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
        do! Assertions.Expect(page.GetByText(Texts.deletionPartial, PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! page.ReloadAsync() :> Task
        do! Ui.click (Ui.button (page.Locator("body")) Texts.library)
        do! Assertions.Expect(button page Texts.deletionRetry).ToBeVisibleAsync()
        fail <- false
        do! Ui.click (button page Texts.deletionRetry)
        do! Assertions.Expect(page.GetByText(Texts.deletionComplete, PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
        do! Ui.click (button page Texts.deletionAcknowledge)
        do! Assertions.Expect(page.GetByText(Texts.deletionComplete, PageGetByTextOptions(Exact = true))).ToHaveCountAsync(0)
    })

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T017"); Trait("Category", "Browser")>]
let ``removing from one project still preserves the PDF and other project summary`` browserName =
    Ui.withBrowser browserName (fun browser -> task {
        use host = TestHarness.Start(serveClient = true)
        let shared, _, _, firstProject = seed host.Root
        let! page = Ui.openApp browser host
        do! Ui.click (button page "Projeto primeiro")
        let row = page.GetByRole(AriaRole.List, PageGetByRoleOptions(Name = Texts.focusedSources, Exact = true)).GetByRole(AriaRole.Listitem)
        do! Ui.click (Ui.button row "compartilhado.pdf")
        do! Ui.click (Ui.button row "compartilhado.pdf")
        do! Ui.click (Ui.button row Texts.remove)
        do! Assertions.Expect(row).ToHaveCountAsync(0)
        Assert.True(File.Exists(Path.Combine(DocumentStore.originalsDirectory host.Root, $"{shared:N}")))
        Assert.Equal(3, (LibraryRepository.list host.Root).Length)
        let remaining = Assert.Single(SummaryRepository.listKeys host.Root)
        Assert.NotEqual(firstProject, remaining.Project)
        Assert.Equal(shared, remaining.Document)
        use db = WorkspaceDb.create host.Root
        Assert.Empty(db.Set<DeletionOperationRow>())
    })
