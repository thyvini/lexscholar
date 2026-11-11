/// SC-002 corpus run (T044): imports every PDF listed in sc002-corpus.csv
/// through the real ImportDocument workflow into an isolated workspace and
/// requires at least 99 of 100 to succeed. The corpus is downloaded with
/// scripts/fetch-sc002-corpus.sh and never committed; LEXSCHOLAR_SC002_CORPUS
/// names its directory. Only identifiers, sizes, hashes and outcomes are
/// written, never file contents.
module LexScholar.Corpus.Tests.CorpusImportTests

open System
open System.IO
open System.Security.Cryptography
open System.Threading
open LexScholar.Application
open LexScholar.Application.ImportDocument
open LexScholar.Infrastructure
open Xunit

[<Literal>]
let CorpusVariable = "LEXSCHOLAR_SC002_CORPUS"

type private Entry =
    { Name: string
      Url: string
      License: string
      Bytes: int64
      Pages: int
      Sha256: string }

let private manifest () =
    let lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "sc002-corpus.csv"))
    Assert.Equal("arxiv_id,version,set,url,license,license_url,bytes,pages,sha256", lines[0])
    [ for line in lines |> Array.skip 1 |> Array.filter (String.IsNullOrWhiteSpace >> not) do
        match line.Split(',') with
        | [| id; version; _; url; license; _; bytes; pages; sha256 |] ->
            yield { Name = id + version; Url = url; License = license
                    Bytes = Int64.Parse(bytes); Pages = Int32.Parse(pages); Sha256 = sha256 }
        | _ -> failwithf "Malformed manifest line: %s" line ]

let private sha256 (path: string) =
    use stream = File.OpenRead(path)
    Convert.ToHexStringLower(SHA256.HashData(stream))

let private outcome (import: Import) (path: string) (name: string) =
    try
        use input = File.OpenRead(path)
        match (import input (name + ".pdf") CancellationToken.None).GetAwaiter().GetResult() with
        | Ok result when result.Reused -> "Reused", result.Document.PageCount
        | Ok result -> "Imported", result.Document.PageCount
        | Error rejection -> $"Rejected {rejection}", 0
    // Record only the exception type: messages could quote document data.
    with error -> $"Failed {error.GetType().Name}", 0

[<Fact; Trait("Task", "T044")>]
let ``at least 99 of the 100 SC-002 corpus PDFs import successfully`` () =
    let corpus = Environment.GetEnvironmentVariable(CorpusVariable)
    Assert.False(String.IsNullOrWhiteSpace(corpus),
                 $"Set {CorpusVariable} to the corpus directory downloaded by scripts/fetch-sc002-corpus.sh (see specs/001-research-workspace/verification.md).")
    let pdfs = Path.Combine(corpus, "pdfs")
    let entries = manifest ()
    Assert.Equal(100, entries.Length)
    // The run must use exactly the documented corpus.
    let mismatches =
        [ for entry in entries do
            let path = Path.Combine(pdfs, entry.Name + ".pdf")
            if not (File.Exists(path)) then yield $"{entry.Name}: missing"
            elif FileInfo(path).Length <> entry.Bytes then yield $"{entry.Name}: size differs from the manifest"
            elif sha256 path <> entry.Sha256 then yield $"{entry.Name}: SHA-256 differs from the manifest" ]
    Assert.Empty(mismatches)
    let workspace = Path.Combine(Path.GetTempPath(), "lexscholar-corpus-tests", Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(workspace) |> ignore
    try
        WorkspaceDb.initialize workspace
        let import =
            create
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
        let results =
            [ for entry in entries ->
                let result, pageCount = outcome import (Path.Combine(pdfs, entry.Name + ".pdf")) entry.Name
                entry, result, pageCount ]
        let lines =
            "name,bytes,sha256,manifest_pages,imported_pages,outcome"
            :: [ for entry, result, pageCount in results ->
                    $"{entry.Name},{entry.Bytes},{entry.Sha256},{entry.Pages},{pageCount},{result}" ]
        let resultsPath = Path.Combine(corpus, "results.csv")
        File.WriteAllLines(resultsPath, lines)
        let imported = results |> List.filter (fun (_, result, _) -> result = "Imported") |> List.length
        Assert.True(imported >= 99, $"SC-002 requires at least 99 of 100 imports; {imported} imported. See {resultsPath}.")
    finally
        Directory.Delete(workspace, true)
