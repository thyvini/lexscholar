/// Real SQLite and filesystem tests for opening stored originals (T035): an
/// original is served only if it exists, is readable and has its recorded
/// length; otherwise it is reported unavailable and its record and associations
/// stay unchanged. Same-length changes are not detected by design (see
/// research.md, "Document availability on open").
module LexScholar.Infrastructure.Tests.ContentAvailabilityTests

open System
open System.IO
open System.Threading
open LexScholar.Application
open LexScholar.Application.ImportDocument
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private fixture name = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))

/// A migrated, isolated data directory.
type private Workspace() =
    let directory = new TemporaryDirectory()
    do WorkspaceDb.initialize directory.Path
    member _.Path = directory.Path
    interface IDisposable with
        member _.Dispose() =
            // Restore permissions changed by a test so the directory can be removed.
            let originals = DocumentStore.originalsDirectory directory.Path
            if Directory.Exists(originals) then
                for file in Directory.GetFiles(originals) do
                    File.SetUnixFileMode(file, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            (directory :> IDisposable).Dispose()

/// Imports valid.pdf through the real import workflow and associates it with a
/// new project.
let private importAssociated workspace : LibraryDocument =
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
    use input = new MemoryStream(fixture "valid.pdf", false)
    let document =
        match (create dependencies input "valid.pdf" CancellationToken.None).GetAwaiter().GetResult() with
        | Ok outcome -> outcome.Document
        | Error rejection -> failwithf "Expected the import to succeed, got %A." rejection
    use connection = openConnection workspace
    let workspaceId = Guid.NewGuid()
    let projectId = Guid.NewGuid()
    addWorkspace connection workspaceId
    addProject connection projectId workspaceId "project"
    execute connection $"INSERT INTO ProjectDocuments (ProjectId, DocumentId, Role, AddedUtc) VALUES ('{projectId:D}', '{document.Id:D}', 'FocusedSource', '2026-05-17T12:34:56.0000000Z')"
    document

let private readAll workspace sql =
    use connection = openConnection workspace
    use command = connection.CreateCommand()
    command.CommandText <- sql
    use reader = command.ExecuteReader()
    [ while reader.Read() do
        yield [ for i in 0 .. reader.FieldCount - 1 -> string (reader.GetValue(i)) ] ]

/// Every stored column of every document and association row.
let private snapshot workspace =
    readAll workspace "SELECT Id, Sha256, StorageKey, OriginalFileName, ByteLength, PageCount, ImportedUtc FROM LibraryDocuments ORDER BY Id",
    readAll workspace "SELECT ProjectId, DocumentId, Role, AddedUtc FROM ProjectDocuments ORDER BY ProjectId, DocumentId"

let private originalPath workspace (document: LibraryDocument) =
    Path.Combine(DocumentStore.originalsDirectory workspace, document.StorageKey)

let private openContent workspace (id: Guid) =
    (VerifiedDocumentContent.openRead workspace id CancellationToken.None).GetAwaiter().GetResult()

/// Rewrites a read-only original outside LexScholar, keeping it read-only.
let private rewrite (path: string) (bytes: byte array) =
    File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
    File.WriteAllBytes(path, bytes)
    File.SetUnixFileMode(path, UnixFileMode.UserRead)

/// Puts the stored original in the named unavailable state.
let private arrange condition (path: string) =
    match condition with
    | "missing" ->
        File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        File.Delete(path)
    | "unreadable" ->
        File.SetUnixFileMode(path, UnixFileMode.None)
        let readable =
            try
                File.OpenRead(path).Dispose()
                true
            with :? UnauthorizedAccessException -> false
        Assert.False(readable, "The original is still readable with mode 000 (running as root or with CAP_DAC_OVERRIDE), so the unreadable case would not be exercised. Run the tests as a normal user.")
    | "shorter" -> rewrite path (File.ReadAllBytes(path) |> Array.take 700)
    | "longer" -> rewrite path (Array.append (File.ReadAllBytes(path)) [| 0x0Auy |])
    | other -> failwithf "Unknown condition %s." other

let private assertUnavailable result =
    match result with
    | Error DocumentUnavailable -> ()
    | Ok (content: VerifiedContent) ->
        content.Stream.Dispose()
        Assert.Fail("Expected DocumentUnavailable, but the content was opened.")
    | Error other -> Assert.Fail($"Expected DocumentUnavailable, got {other}.")

let conditions : TheoryData<string> = TheoryData<string>([ "missing"; "unreadable"; "shorter"; "longer" ])

[<Fact; Trait("Task", "T035")>]
let ``an available original opens read-only with its exact bytes and metadata`` () =
    use workspace = new Workspace()
    let document = importAssociated workspace.Path
    match openContent workspace.Path document.Id with
    | Error failure -> Assert.Fail($"Expected the content to open, got {failure}.")
    | Ok content ->
        use stream = content.Stream
        Assert.False(stream.CanWrite)
        Assert.Equal(document.ByteLength, content.ByteLength)
        Assert.Equal("valid.pdf", content.OriginalFileName)
        use copy = new MemoryStream()
        stream.CopyTo(copy)
        Assert.Equal<byte array>(fixture "valid.pdf", copy.ToArray())

[<Fact; Trait("Task", "T035")>]
let ``stored originals are read-only`` () =
    use workspace = new Workspace()
    let document = importAssociated workspace.Path
    let path = originalPath workspace.Path document
    Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(path))
    Assert.Throws<UnauthorizedAccessException>(fun () -> File.OpenWrite(path).Dispose()) |> ignore
    Assert.Equal<byte array>(fixture "valid.pdf", File.ReadAllBytes(path))

[<Theory; MemberData(nameof conditions); Trait("Task", "T035")>]
let ``an unavailable original is reported with its record and associations unchanged`` (condition: string) =
    use workspace = new Workspace()
    let document = importAssociated workspace.Path
    let before = snapshot workspace.Path
    arrange condition (originalPath workspace.Path document)
    assertUnavailable (openContent workspace.Path document.Id)
    Assert.Equal(before, snapshot workspace.Path)

[<Theory; MemberData(nameof conditions); Trait("Task", "T035")>]
let ``an unavailable original is still reported after restart with its record and associations unchanged`` (condition: string) =
    use workspace = new Workspace()
    let document = importAssociated workspace.Path
    let before = snapshot workspace.Path
    arrange condition (originalPath workspace.Path document)
    assertUnavailable (openContent workspace.Path document.Id)
    // Restart: the host migrates and runs startup cleanup before serving.
    WorkspaceDb.initialize workspace.Path
    StorageCleanup.run workspace.Path |> ignore
    assertUnavailable (openContent workspace.Path document.Id)
    Assert.Equal(before, snapshot workspace.Path)

[<Fact; Trait("Task", "T035")>]
let ``an unknown UUID is not found`` () =
    use workspace = new Workspace()
    importAssociated workspace.Path |> ignore
    let before = snapshot workspace.Path
    match openContent workspace.Path (Guid.NewGuid()) with
    | Error DocumentNotFound -> ()
    | Ok content ->
        content.Stream.Dispose()
        Assert.Fail("Expected DocumentNotFound, but content was opened.")
    | Error other -> Assert.Fail($"Expected DocumentNotFound, got {other}.")
    Assert.Equal(before, snapshot workspace.Path)
