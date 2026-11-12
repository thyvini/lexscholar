/// Per-page text of stored originals for summaries (F003 T002, research.md
/// section 3), against real imported fixtures.
module LexScholar.Infrastructure.Tests.PdfTextTests

open System
open System.IO
open System.Threading
open LexScholar.Application
open LexScholar.Application.ImportDocument
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private fixture name = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", name))

let private import workspace (name: string) : LibraryDocument =
    let dependencies =
        { Limits = { MaxBytes = 100_000_000L; MaxPages = 2_000 }
          Validate = PdfValidator.validate
          OpenStagedWrite = DocumentStore.openStagedWrite workspace
          OpenStagedRead = DocumentStore.openStagedRead workspace
          Promote = DocumentStore.promote workspace
          Discard = DocumentStore.discard workspace
          OpenOriginal = DocumentStore.openOriginal workspace
          FindBySha256 = LibraryRepository.findBySha256 workspace
          Insert = LibraryRepository.insert workspace
          Now = fun () -> DateTimeOffset.UtcNow }
    use input = new MemoryStream(fixture name, false)
    match (create dependencies input name CancellationToken.None).GetAwaiter().GetResult() with
    | Ok outcome -> outcome.Document
    | Error rejection -> failwithf "Expected the import to succeed, got %A." rejection

let private extract workspace (id: Guid) =
    (PdfText.extractPages workspace id CancellationToken.None).GetAwaiter().GetResult()

let private withWorkspace (action: string -> unit) =
    use directory = new TemporaryDirectory()
    WorkspaceDb.initialize directory.Path
    action directory.Path

[<Fact; Trait("Task", "T002")>]
let ``pages are numbered by position, first page is 1, and carry their text`` () =
    withWorkspace (fun workspace ->
        let document = import workspace "article-en.pdf"
        match extract workspace document.Id with
        | Ok pages ->
            Assert.Equal<int list>([ 1 .. 12 ], pages |> List.map (fun page -> page.Number))
            Assert.All(pages, fun page -> Assert.False(String.IsNullOrWhiteSpace page.Text))
            Assert.DoesNotContain(pages, fun page -> page.Text.Contains('\n') || page.Text.Contains("  "))
        | Error error -> failwithf "Expected pages, got %A." error)

[<Fact; Trait("Task", "T002")>]
let ``partial.pdf keeps its positions and its pages 2 and 5 are unreadable`` () =
    withWorkspace (fun workspace ->
        let document = import workspace "partial.pdf"
        match extract workspace document.Id with
        | Ok pages ->
            Assert.Equal<int list>([ 1 .. 6 ], pages |> List.map (fun page -> page.Number))
            match Summaries.readability pages with
            | Ok readability ->
                Assert.Equal<int list>([ 2; 5 ], readability.Unreadable)
                Assert.Equal<int list>([ 1; 3; 4; 6 ], readability.Readable |> List.map (fun page -> page.Number))
            | Error reason -> failwithf "Expected readable pages, got %A." reason
        | Error error -> failwithf "Expected pages, got %A." error)

[<Fact; Trait("Task", "T002")>]
let ``more than 100 pages is refused as TooManyPages`` () =
    withWorkspace (fun workspace ->
        let document = import workspace "too-long.pdf"
        Assert.Equal(Error Summarize.ExtractionError.TooManyPages, extract workspace document.Id))

[<Fact; Trait("Task", "T002")>]
let ``unknown, missing or resized originals are not found or unavailable`` () =
    withWorkspace (fun workspace ->
        Assert.Equal(Error Summarize.ExtractionError.DocumentNotFound, extract workspace (Guid.NewGuid()))
        let document = import workspace "article-en.pdf"
        let path = Path.Combine(DocumentStore.originalsDirectory workspace, document.StorageKey)
        File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        File.Delete(path)
        Assert.Equal(Error Summarize.ExtractionError.DocumentUnavailable, extract workspace document.Id))
