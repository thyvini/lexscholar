/// Real SQLite and filesystem import tests (T021): staging, validation, the
/// serialized commit, exact-byte reuse and non-mutating rejection.
module LexScholar.Infrastructure.Tests.LibraryTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Application.ImportDocument
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private defaults = { MaxBytes = 100_000_000L; MaxPages = 2_000 }
let private validSha = "0fcd00dc1ad92711575dfde326cda6c56b8fd83b0a01462cab6bfd012dcbe93b"
let private fixture name = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))

/// A migrated, isolated data directory.
type private Workspace() =
    let directory = new TemporaryDirectory()
    do WorkspaceDb.initialize directory.Path
    member _.Path = directory.Path
    interface IDisposable with member _.Dispose() = (directory :> IDisposable).Dispose()

let private dependencies directory limits =
    { Limits = limits
      Validate = PdfValidator.validate
      OpenStagedWrite = DocumentStore.openStagedWrite directory
      OpenStagedRead = DocumentStore.openStagedRead directory
      Promote = DocumentStore.promote directory
      Discard = DocumentStore.discard directory
      OpenOriginal = DocumentStore.openOriginal directory
      FindBySha256 = LibraryRepository.findBySha256 directory
      Insert = LibraryRepository.insert directory
      Now = fun () -> DateTimeOffset.UtcNow }

let private importFrom (import: Import) (input: Stream) fileName =
    (import input fileName CancellationToken.None).GetAwaiter().GetResult()

let private importBytes (import: Import) (bytes: byte array) fileName =
    use input = new MemoryStream(bytes, false)
    importFrom import input fileName

let private imported result =
    match result with
    | Ok outcome -> outcome
    | Error rejection -> failwithf "Expected the import to succeed, got %A." rejection

let private assertResult<'a when 'a: equality> (expected: 'a) (actual: 'a) =
    Assert.True((expected = actual), sprintf "Expected: %A\nActual: %A" expected actual)

let private files directory =
    if Directory.Exists(directory) then
        Directory.EnumerateFileSystemEntries(directory) |> Seq.map Path.GetFileName |> Seq.sort |> Seq.toList
    else []

let private stagingFiles workspace = files (Path.Combine(workspace, "staging"))
let private originals workspace = files (Path.Combine(workspace, "originals"))
let private originalPath workspace key = Path.Combine(workspace, "originals", key)

let private rows workspace : LibraryDocument list =
    use connection = openConnection workspace
    use command = connection.CreateCommand()
    command.CommandText <- "SELECT Id, Sha256, StorageKey, OriginalFileName, ByteLength, PageCount, ImportedUtc FROM LibraryDocuments ORDER BY Id"
    use reader = command.ExecuteReader()
    [ while reader.Read() do
        yield { Id = Guid.Parse(reader.GetString(0)); Sha256 = reader.GetString(1); StorageKey = reader.GetString(2)
                OriginalFileName = reader.GetString(3); ByteLength = reader.GetInt64(4); PageCount = reader.GetInt32(5)
                ImportedUtc = DateTimeOffset.Parse(reader.GetString(6), Globalization.CultureInfo.InvariantCulture) } ]

/// Adds a workspace, a project and an association to the given document.
let private associate workspace (documentId: Guid) =
    use connection = openConnection workspace
    let workspaceId = Guid.NewGuid()
    let projectId = Guid.NewGuid()
    addWorkspace connection workspaceId
    addProject connection projectId workspaceId "project"
    execute connection $"INSERT INTO ProjectDocuments (ProjectId, DocumentId, Role, AddedUtc) VALUES ('{projectId:D}', '{documentId:D}', 'FocusedSource', '2026-05-17T12:34:56.0000000Z')"

let private associations workspace =
    use connection = openConnection workspace
    use command = connection.CreateCommand()
    command.CommandText <- "SELECT ProjectId || '/' || DocumentId || '/' || Role FROM ProjectDocuments ORDER BY 1"
    use reader = command.ExecuteReader()
    [ while reader.Read() do yield reader.GetString(0) ]

let private isStorageKey (value: string) =
    value.Length = 32 && value |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

/// Asserts the stored original for a document: read-only, named by its
/// generated key and holding exactly the expected bytes.
let private assertStoredOriginal workspace (document: LibraryDocument) (bytes: byte array) =
    Assert.True(isStorageKey document.StorageKey, $"Storage key '{document.StorageKey}' is not a generated key.")
    let path = originalPath workspace document.StorageKey
    Assert.True(File.Exists(path), "The stored original is missing.")
    Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(path))
    Assert.Equal<byte array>(bytes, File.ReadAllBytes(path))

/// A source that records how many bytes were read from it.
type private CountingSource(bytes: byte array) =
    inherit MemoryStream(bytes, false)
    let mutable bytesRead = 0L
    member _.BytesRead = bytesRead
    override this.Read(buffer: byte array, offset, count) =
        let read = base.Read(buffer, offset, count)
        bytesRead <- bytesRead + int64 read
        read

/// A source that requests cancellation as soon as its first bytes are read.
type private CancellingSource(bytes: byte array, cancellation: CancellationTokenSource) =
    inherit MemoryStream(bytes, false)
    override this.Read(buffer: byte array, offset, count) =
        let read = base.Read(buffer, offset, count)
        cancellation.Cancel()
        read

[<Fact; Trait("Task", "T021")>]
let ``a new PDF is stored as one read-only original and one document row`` () =
    use workspace = new Workspace()
    let outcome = importBytes (create (dependencies workspace.Path defaults)) (fixture "valid.pdf") "valid.pdf" |> imported
    Assert.False(outcome.Reused)
    let document = outcome.Document
    assertResult (validSha, "valid.pdf", 753L, 2) (document.Sha256, document.OriginalFileName, document.ByteLength, document.PageCount)
    assertResult [ document ] (rows workspace.Path)
    Assert.Equal<string list>([ document.StorageKey ], originals workspace.Path)
    assertStoredOriginal workspace.Path document (fixture "valid.pdf")
    Assert.Empty(stagingFiles workspace.Path)

[<Fact; Trait("Task", "T021")>]
let ``identical bytes reuse the existing document`` () =
    use workspace = new Workspace()
    let import = create (dependencies workspace.Path defaults)
    let first = importBytes import (fixture "valid.pdf") "valid.pdf" |> imported
    let second = importBytes import (fixture "valid.pdf") "valid.pdf" |> imported
    Assert.False(first.Reused)
    Assert.True(second.Reused)
    assertResult first.Document second.Document
    Assert.Single(rows workspace.Path) |> ignore
    Assert.Equal<string list>([ first.Document.StorageKey ], originals workspace.Path)
    Assert.Empty(stagingFiles workspace.Path)

[<Fact; Trait("Task", "T021")>]
let ``identical bytes under a different filename reuse the existing document`` () =
    use workspace = new Workspace()
    let import = create (dependencies workspace.Path defaults)
    let first = importBytes import (fixture "valid.pdf") "valid.pdf" |> imported
    let renamed = importBytes import (fixture "valid-renamed.pdf") "valid-renamed.pdf" |> imported
    Assert.True(renamed.Reused)
    assertResult first.Document renamed.Document
    Assert.Equal("valid.pdf", renamed.Document.OriginalFileName)
    Assert.Single(rows workspace.Path) |> ignore
    Assert.Single(originals workspace.Path) |> ignore
    Assert.Empty(stagingFiles workspace.Path)

[<Fact; Trait("Task", "T021")>]
let ``different bytes produce a distinct document even under the same filename`` () =
    use workspace = new Workspace()
    let import = create (dependencies workspace.Path defaults)
    let valid = importBytes import (fixture "valid.pdf") "paper.pdf" |> imported
    let distinct = importBytes import (fixture "distinct.pdf") "paper.pdf" |> imported
    Assert.False(distinct.Reused)
    Assert.NotEqual(valid.Document.Id, distinct.Document.Id)
    Assert.NotEqual<string>(valid.Document.StorageKey, distinct.Document.StorageKey)
    Assert.Equal(2, (rows workspace.Path).Length)
    Assert.Equal(2, (originals workspace.Path).Length)
    assertStoredOriginal workspace.Path valid.Document (fixture "valid.pdf")
    assertStoredOriginal workspace.Path distinct.Document (fixture "distinct.pdf")
    Assert.Empty(stagingFiles workspace.Path)

[<Theory; Trait("Task", "T021")>]
[<InlineData("identical", true)>]
[<InlineData("last byte differs", false)>]
[<InlineData("first byte differs", false)>]
[<InlineData("left is shorter", false)>]
[<InlineData("right is shorter", false)>]
[<InlineData("both empty", true)>]
[<InlineData("only left empty", false)>]
let ``byte comparison decides equality on distinct inputs`` (case: string, expected: bool) =
    // Longer than one comparison buffer, so differences past the first chunk count.
    let bytes = Array.init 200_000 (fun index -> byte (index % 251))
    let changed (index: int) = let copy = Array.copy bytes in copy.[index] <- copy.[index] ^^^ 0xFFuy; copy
    let left, right =
        match case with
        | "identical" -> bytes, Array.copy bytes
        | "last byte differs" -> bytes, changed (bytes.Length - 1)
        | "first byte differs" -> bytes, changed 0
        | "left is shorter" -> bytes.[.. bytes.Length - 2], bytes
        | "right is shorter" -> bytes, bytes.[.. bytes.Length - 2]
        | "both empty" -> [||], [||]
        | "only left empty" -> [||], bytes
        | other -> failwithf "Unknown case %s" other
    use leftStream = new MemoryStream(left, false)
    use rightStream = new MemoryStream(right, false)
    Assert.Equal(expected, (contentEquals leftStream rightStream CancellationToken.None).GetAwaiter().GetResult())

let private rejection (expected: string) (limit: int64) =
    match expected with
    | "NotPdf" -> NotPdf
    | "Corrupt" -> Corrupt
    | "Encrypted" -> Encrypted
    | "TooLarge" -> TooLarge limit
    | "TooManyPages" -> TooManyPages (int limit)
    | other -> failwithf "Unknown rejection %s" other

[<Theory; Trait("Task", "T021")>]
[<InlineData("not-a-pdf.txt", 100_000_000L, 2_000, "NotPdf")>]
[<InlineData("corrupt.pdf", 100_000_000L, 2_000, "Corrupt")>]
[<InlineData("encrypted.pdf", 100_000_000L, 2_000, "Encrypted")>]
[<InlineData("distinct.pdf", 752L, 2_000, "TooLarge")>]
[<InlineData("distinct.pdf", 100_000_000L, 1, "TooManyPages")>]
let ``a rejected import leaves rows, originals, associations and staging unchanged`` (name: string, maxBytes: int64, maxPages: int, expected: string) =
    use workspace = new Workspace()
    let existing = importBytes (create (dependencies workspace.Path defaults)) (fixture "valid.pdf") "valid.pdf" |> imported
    associate workspace.Path existing.Document.Id
    let rowsBefore = rows workspace.Path
    let associationsBefore = associations workspace.Path
    let limits = { MaxBytes = maxBytes; MaxPages = maxPages }
    let limit = if expected = "TooManyPages" then int64 maxPages else maxBytes
    let result = importBytes (create (dependencies workspace.Path limits)) (fixture name) name
    assertResult (Error (rejection expected limit)) result
    assertResult rowsBefore (rows workspace.Path)
    Assert.Equal<string list>([ existing.Document.StorageKey ], originals workspace.Path)
    assertStoredOriginal workspace.Path existing.Document (fixture "valid.pdf")
    Assert.Equal<string list>(associationsBefore, associations workspace.Path)
    Assert.Empty(stagingFiles workspace.Path)

[<Theory; Trait("Task", "T021")>]
[<InlineData(752L, 2_000, "TooLarge")>]
[<InlineData(100_000_000L, 1, "TooManyPages")>]
let ``re-importing existing bytes that exceed the current limits is rejected without changes`` (maxBytes: int64, maxPages: int, expected: string) =
    use workspace = new Workspace()
    let existing = importBytes (create (dependencies workspace.Path defaults)) (fixture "valid.pdf") "valid.pdf" |> imported
    associate workspace.Path existing.Document.Id
    let rowsBefore = rows workspace.Path
    let associationsBefore = associations workspace.Path
    let limit = if expected = "TooManyPages" then int64 maxPages else maxBytes
    let result = importBytes (create (dependencies workspace.Path { MaxBytes = maxBytes; MaxPages = maxPages })) (fixture "valid.pdf") "valid.pdf"
    assertResult (Error (rejection expected limit)) result
    assertResult rowsBefore (rows workspace.Path)
    Assert.Equal<string list>([ existing.Document.StorageKey ], originals workspace.Path)
    assertStoredOriginal workspace.Path existing.Document (fixture "valid.pdf")
    Assert.Equal<string list>(associationsBefore, associations workspace.Path)
    Assert.Empty(stagingFiles workspace.Path)

[<Fact; Trait("Task", "T021")>]
let ``an oversized upload is staged only up to MaxBytes plus one bytes`` () =
    use workspace = new Workspace()
    use source = new CountingSource(Array.append (fixture "valid.pdf") (Array.zeroCreate 1_000_000))
    let result = importFrom (create (dependencies workspace.Path { defaults with MaxBytes = 1_000L })) source "large.pdf"
    assertResult (Error (TooLarge 1_000L)) result
    Assert.Equal(1_001L, source.BytesRead)
    Assert.Empty(rows workspace.Path)
    Assert.Empty(originals workspace.Path)
    Assert.Empty(stagingFiles workspace.Path)

[<Fact; Trait("Task", "T021")>]
let ``concurrent imports of identical bytes all return the same single document`` () =
    use workspace = new Workspace()
    let import = create (dependencies workspace.Path defaults)
    let other = importBytes import (fixture "distinct.pdf") "distinct.pdf" |> imported
    associate workspace.Path other.Document.Id
    let associationsBefore = associations workspace.Path
    let bytes = fixture "valid.pdf"
    let results =
        [| for index in 1 .. 8 ->
               let name = if index % 2 = 0 then "valid.pdf" else "valid-renamed.pdf"
               Task.Run<Result<ImportOutcome, PdfRejection>>(fun () ->
                   task {
                       use input = new MemoryStream(bytes, false)
                       return! import input name CancellationToken.None
                   }) |]
        |> Task.WhenAll
        |> fun all -> all.GetAwaiter().GetResult()
    let outcomes = results |> Array.map imported
    let document = outcomes.[0].Document
    Assert.All(outcomes, fun outcome -> assertResult document outcome.Document)
    Assert.Equal(1, outcomes |> Array.filter (fun outcome -> not outcome.Reused) |> Array.length)
    Assert.Equal(2, (rows workspace.Path).Length)
    Assert.Equal(1, rows workspace.Path |> List.filter (fun row -> row.Sha256 = validSha) |> List.length)
    Assert.Equal<string list>(List.sort [ other.Document.StorageKey; document.StorageKey ], originals workspace.Path)
    assertStoredOriginal workspace.Path document bytes
    Assert.Equal<string list>(associationsBefore, associations workspace.Path)
    Assert.Empty(stagingFiles workspace.Path)

[<Fact; Trait("Task", "T021")>]
let ``a failure while validating discards the staged file and propagates`` () =
    use workspace = new Workspace()
    let failing : ValidatePdf = fun _ _ _ -> Task.FromException<Result<ValidatedPdf, PdfRejection>>(IOException("Simulated read failure."))
    let import = create { dependencies workspace.Path defaults with Validate = failing }
    Assert.Throws<IOException>(fun () -> importBytes import (fixture "valid.pdf") "valid.pdf" |> ignore) |> ignore
    Assert.Empty(rows workspace.Path)
    Assert.Empty(originals workspace.Path)
    Assert.Empty(stagingFiles workspace.Path)

[<Fact; Trait("Task", "T021")>]
let ``an import cancelled while staging discards the staged file`` () =
    use workspace = new Workspace()
    use cancellation = new CancellationTokenSource()
    let import = create (dependencies workspace.Path defaults)
    use source = new CancellingSource(fixture "valid.pdf", cancellation)
    Assert.ThrowsAny<OperationCanceledException>(fun () ->
        (import source "valid.pdf" cancellation.Token).GetAwaiter().GetResult() |> ignore) |> ignore
    Assert.Empty(rows workspace.Path)
    Assert.Empty(originals workspace.Path)
    Assert.Empty(stagingFiles workspace.Path)

[<Theory; Trait("Task", "T021")>]
[<InlineData("paper.pdf", "paper.pdf")>]
[<InlineData("../../etc/paper.pdf", "paper.pdf")>]
[<InlineData("C:\\Users\\someone\\paper.pdf", "paper.pdf")>]
[<InlineData("  pa\u0000per\u001f.pdf  ", "paper.pdf")>]
[<InlineData("folder/", "document.pdf")>]
[<InlineData("   ", "document.pdf")>]
[<InlineData("", "document.pdf")>]
let ``the stored filename is a display-only basename`` (fileName: string, expected: string) =
    Assert.Equal(expected, sanitizeFileName fileName)
