module LexScholar.Browser.Tests.MissingFileJourneyTests

open System.IO
open System.Text.Json
open Microsoft.Playwright
open Xunit
open LexScholar.Server.Tests
open LexScholar.Infrastructure
open LexScholar.Browser.Tests.Ui

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T005"); Trait("Category", "Browser")>]
let ``a browser navigation to a missing file shows a Portuguese page`` (browserName: string) =
    withBrowser browserName (fun browser -> task {
        use harness = TestHarness.Start(serveClient = true)
        let! page = openApp browser harness
        do! SummaryJourneyTests.arrange page
        use! response = harness.Client.GetAsync("/api/documents")
        let! body = response.Content.ReadAsStringAsync()
        use json = JsonDocument.Parse(body)
        let content = json.RootElement.[0].GetProperty("contentUrl").GetString()
        for path in Directory.GetFiles(DocumentStore.originalsDirectory harness.Root) do
            File.Delete(path)
        let! tab = page.Context.NewPageAsync()
        let! _ = tab.GotoAsync(System.Uri(harness.Client.BaseAddress, content).AbsoluteUri)
        do! Assertions.Expect(tab.GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = "Documento indisponível"))).ToBeVisibleAsync()
    })
