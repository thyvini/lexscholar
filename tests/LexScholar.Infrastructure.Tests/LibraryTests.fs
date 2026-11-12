/// Real SQLite and filesystem import tests (T021): staging, validation, the
/// serialized commit, exact-byte reuse and non-mutating rejection.
module LexScholar.Infrastructure.Tests.LibraryTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
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

[<Theory; Trait("Task", "T021"); Trait("Task", "T022")>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``post-promotion insertion errors preserve bytes and startup quarantines only unreferenced originals`` (committedBeforeError: bool) =
    use workspace = new Workspace()
    use directoryLock =
        match DataDirectoryLock.acquire workspace.Path with
        | Ok held -> held
        | Error error -> failwithf "Unable to lock isolated workspace: %A" error
    let existing = importBytes (create (dependencies workspace.Path defaults)) (fixture "distinct.pdf") "distinct.pdf" |> imported
    associate workspace.Path existing.Document.Id
    let rowsBefore = rows workspace.Path
    let associationsBefore = associations workspace.Path
    let mutable promoted = None
    let failure = IOException("Simulated insertion failure after promotion.")
    let failing : InsertDocument = fun document cancellationToken -> task {
        assertStoredOriginal workspace.Path document (fixture "valid.pdf")
        Assert.Empty(stagingFiles workspace.Path)
        promoted <- Some document
        if committedBeforeError then
            do! LibraryRepository.insert workspace.Path document cancellationToken
        return raise failure }
    let import = create { dependencies workspace.Path defaults with Insert = failing }
    let thrown = Assert.Throws<IOException>(fun () -> importBytes import (fixture "valid.pdf") "valid.pdf" |> ignore)
    Assert.Same(failure, thrown)
    let document = promoted |> Option.defaultWith (fun () -> failwith "Insert was not reached.")
    Assert.Empty(stagingFiles workspace.Path)
    assertStoredOriginal workspace.Path document (fixture "valid.pdf")
    let expectedRows = if committedBeforeError then document :: rowsBefore else rowsBefore
    assertResult (Set.ofList expectedRows) (rows workspace.Path |> Set.ofList)
    Assert.Equal<string list>(List.sort [ existing.Document.StorageKey; document.StorageKey ], originals workspace.Path)

    // Exercise the actual startup recovery policy with no import in progress,
    // under the data-directory lock. Never infer absence of a row from an error.
    let report = StorageCleanup.run workspace.Path
    Assert.Equal(0, report.DeletedStagingFiles)
    Assert.Empty(report.Conflicting)
    if committedBeforeError then
        Assert.Empty(report.Quarantined)
        Assert.Empty(files (DocumentStore.quarantineDirectory workspace.Path))
        assertStoredOriginal workspace.Path document (fixture "valid.pdf")
        // The same workflow remains usable (the commit semaphore was released).
        let retry = importBytes import (fixture "valid.pdf") "retry.pdf" |> imported
        Assert.True(retry.Reused)
        assertResult document retry.Document
    else
        Assert.Equal<string list>([ document.StorageKey ], report.Quarantined)
        Assert.Equal<string list>([ existing.Document.StorageKey ], originals workspace.Path)
        let quarantined = Path.Combine(DocumentStore.quarantineDirectory workspace.Path, document.StorageKey)
        Assert.Equal<byte array>(fixture "valid.pdf", File.ReadAllBytes(quarantined))
        Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(quarantined))
    assertResult (Set.ofList expectedRows) (rows workspace.Path |> Set.ofList)
    Assert.Equal<string list>(associationsBefore, associations workspace.Path)
    assertStoredOriginal workspace.Path existing.Document (fixture "distinct.pdf")
    let second = StorageCleanup.run workspace.Path
    Assert.Empty(second.Quarantined)
    Assert.Empty(second.Conflicting)

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
    // The caller owns the input: a cancelled import must not dispose it.
    Assert.True(source.CanRead)
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

// Shared library listing and per-project associations (T030), through the
// Library workflows over the real repository.

let private libraryStore directory : Library.Store =
    { ListDocuments = fun () -> LibraryRepository.list directory
      ListForProject = LibraryRepository.listForProject directory
      Associate = LibraryRepository.associate directory
      Remove = LibraryRepository.removeAssociation directory }

/// Adds one workspace with the given number of projects; returns their ids.
let private addProjects workspace count =
    use connection = openConnection workspace
    let workspaceId = Guid.NewGuid()
    addWorkspace connection workspaceId
    [ for index in 1 .. count do
        let id = Guid.NewGuid()
        addProject connection id workspaceId $"project-{index}"
        yield id ]

let private importDocument workspace name =
    (importBytes (create (dependencies workspace defaults)) (fixture name) name |> imported).Document

let private expectedView (document: LibraryDocument) : Document =
    { Id = document.Id
      OriginalFileName = document.OriginalFileName
      ByteLength = document.ByteLength
      PageCount = document.PageCount
      ImportedUtc = document.ImportedUtc
      ContentUrl = $"/api/documents/{document.Id:D}/content" }

let private associated result =
    match result with
    | Ok view -> view
    | Error error -> failwithf "Expected the association to succeed, got %A." error

[<Fact; Trait("Task", "T030")>]
let ``the library lists every document, including unassociated ones`` () =
    use workspace = new Workspace()
    let store = libraryStore workspace.Path
    let first = importDocument workspace.Path "valid.pdf"
    let second = importDocument workspace.Path "distinct.pdf"
    let project = addProjects workspace.Path 1 |> List.head
    Library.associate store project first.Id FocusedSource |> associated |> ignore
    assertResult [ expectedView first; expectedView second ] (Library.list store)

[<Fact; Trait("Task", "T030")>]
let ``one document associated with two projects keeps each project's own role`` () =
    use workspace = new Workspace()
    let store = libraryStore workspace.Path
    let document = importDocument workspace.Path "valid.pdf"
    let projects = addProjects workspace.Path 2
    let focused = Library.associate store projects[0] document.Id FocusedSource |> associated
    let reference = Library.associate store projects[1] document.Id ReferenceSource |> associated
    assertResult (projects[0], expectedView document, FocusedSource) (focused.ProjectId, focused.Document, focused.Role)
    assertResult (projects[1], expectedView document, ReferenceSource) (reference.ProjectId, reference.Document, reference.Role)
    assertResult (Ok [ focused ]) (Library.listForProject store projects[0])
    assertResult (Ok [ reference ]) (Library.listForProject store projects[1])
    assertResult [ document ] (rows workspace.Path)

[<Fact; Trait("Task", "T030")>]
let ``a duplicate association is rejected and keeps the existing role`` () =
    use workspace = new Workspace()
    let store = libraryStore workspace.Path
    let document = importDocument workspace.Path "valid.pdf"
    let project = addProjects workspace.Path 1 |> List.head
    let existing = Library.associate store project document.Id FocusedSource |> associated
    assertResult (Error Library.AssociationError.DuplicateAssociation) (Library.associate store project document.Id ReferenceSource)
    assertResult (Ok [ existing ]) (Library.listForProject store project)

[<Fact; Trait("Task", "T030")>]
let ``associating an unknown project or document is rejected without changes`` () =
    use workspace = new Workspace()
    let store = libraryStore workspace.Path
    let document = importDocument workspace.Path "valid.pdf"
    let project = addProjects workspace.Path 1 |> List.head
    assertResult (Error Library.AssociationError.ProjectNotFound) (Library.associate store (Guid.NewGuid()) document.Id FocusedSource)
    assertResult (Error Library.AssociationError.DocumentNotFound) (Library.associate store project (Guid.NewGuid()) FocusedSource)
    Assert.Empty(associations workspace.Path)
    assertResult [ document ] (rows workspace.Path)

[<Fact; Trait("Task", "T030")>]
let ``removing one association keeps the document, its original and other projects' associations`` () =
    use workspace = new Workspace()
    let store = libraryStore workspace.Path
    let document = importDocument workspace.Path "valid.pdf"
    let projects = addProjects workspace.Path 2
    Library.associate store projects[0] document.Id FocusedSource |> associated |> ignore
    let kept = Library.associate store projects[1] document.Id ReferenceSource |> associated
    assertResult (Ok ()) (Library.remove store projects[0] document.Id)
    assertResult (Ok []) (Library.listForProject store projects[0])
    assertResult (Ok [ kept ]) (Library.listForProject store projects[1])
    assertResult [ document ] (rows workspace.Path)
    assertStoredOriginal workspace.Path document (fixture "valid.pdf")

[<Fact; Trait("Task", "T030")>]
let ``removing a document's final association keeps its row and stored original`` () =
    use workspace = new Workspace()
    let store = libraryStore workspace.Path
    let document = importDocument workspace.Path "valid.pdf"
    let projects = addProjects workspace.Path 2
    Library.associate store projects[0] document.Id FocusedSource |> associated |> ignore
    assertResult (Ok ()) (Library.remove store projects[0] document.Id)
    Assert.Empty(associations workspace.Path)
    assertResult [ document ] (rows workspace.Path)
    Assert.Equal<string list>([ document.StorageKey ], originals workspace.Path)
    assertStoredOriginal workspace.Path document (fixture "valid.pdf")
    assertResult [ expectedView document ] (Library.list store)
    // Still available for later association with any project.
    let again = Library.associate store projects[1] document.Id ReferenceSource |> associated
    assertResult (Ok [ again ]) (Library.listForProject store projects[1])

[<Fact; Trait("Task", "T030")>]
let ``removing a missing association is rejected and changes nothing`` () =
    use workspace = new Workspace()
    let store = libraryStore workspace.Path
    let document = importDocument workspace.Path "valid.pdf"
    let projects = addProjects workspace.Path 2
    let kept = Library.associate store projects[0] document.Id FocusedSource |> associated
    assertResult (Error Library.RemovalError.AssociationNotFound) (Library.remove store projects[1] document.Id)
    assertResult (Error Library.RemovalError.AssociationNotFound) (Library.remove store projects[0] (Guid.NewGuid()))
    assertResult (Ok [ kept ]) (Library.listForProject store projects[0])
    assertResult [ document ] (rows workspace.Path)

[<Fact; Trait("Task", "T030")>]
let ``listing an unknown project is rejected and an empty project lists nothing`` () =
    use workspace = new Workspace()
    let store = libraryStore workspace.Path
    importDocument workspace.Path "valid.pdf" |> ignore
    let project = addProjects workspace.Path 1 |> List.head
    assertResult (Error Library.AssociationError.ProjectNotFound) (Library.listForProject store (Guid.NewGuid()))
    assertResult (Ok []) (Library.listForProject store project)

// Project-scoped role changes (T036), through the DocumentRoles workflow over
// the real repository.

[<Fact; Trait("Task", "T036")>]
let ``changing a role in one project leaves the other project's role unchanged`` () =
    use workspace = new Workspace()
    let store = libraryStore workspace.Path
    let document = importDocument workspace.Path "valid.pdf"
    let projects = addProjects workspace.Path 2
    let changed = Library.associate store projects[0] document.Id FocusedSource |> associated
    let other = Library.associate store projects[1] document.Id FocusedSource |> associated
    let expected = { changed with Role = ReferenceSource }
    assertResult (Ok expected) (DocumentRoles.changeRole (LibraryRepository.changeRole workspace.Path) projects[0] document.Id ReferenceSource)
    assertResult (Ok [ expected ]) (Library.listForProject store projects[0])
    assertResult (Ok [ other ]) (Library.listForProject store projects[1])
    assertResult [ document ] (rows workspace.Path)

[<Fact; Trait("Task", "T036")>]
let ``changing a role for an unknown project or an unassociated document is rejected without changes`` () =
    use workspace = new Workspace()
    let store = libraryStore workspace.Path
    let document = importDocument workspace.Path "valid.pdf"
    let unassociated = importDocument workspace.Path "distinct.pdf"
    let project = addProjects workspace.Path 1 |> List.head
    Library.associate store project document.Id FocusedSource |> associated |> ignore
    let before = associations workspace.Path
    let changeRole = DocumentRoles.changeRole (LibraryRepository.changeRole workspace.Path)
    assertResult (Error DocumentRoles.RoleChangeError.AssociationNotFound) (changeRole (Guid.NewGuid()) document.Id ReferenceSource)
    assertResult (Error DocumentRoles.RoleChangeError.AssociationNotFound) (changeRole project unassociated.Id ReferenceSource)
    assertResult before (associations workspace.Path)
    assertResult (set [ document; unassociated ]) (rows workspace.Path |> Set.ofList)

/// Every column, including timestamps and active selection, not just roles.
let private roleSnapshot directory =
    use connection = openConnection directory
    [ for table, order in [ "Workspaces", "Id"; "ResearchProjects", "Id"; "LibraryDocuments", "Id"; "ProjectDocuments", "ProjectId, DocumentId" ] do
        use command = connection.CreateCommand()
        command.CommandText <- $"SELECT * FROM {table} ORDER BY {order}"
        use reader = command.ExecuteReader()
        yield table, [ while reader.Read() do yield [ for i in 0 .. reader.FieldCount - 1 -> string (reader.GetValue(i)) ] ] ]

[<Theory; InlineData(false); InlineData(true); Trait("Task", "T036")>]
let ``an association deleted at the role update is not found and preserves unrelated data`` (abortUpdate: bool) =
    use workspace = new Workspace()
    let store = libraryStore workspace.Path
    let document = importDocument workspace.Path "valid.pdf"
    let unrelated = importDocument workspace.Path "distinct.pdf"
    let projects = addProjects workspace.Path 2
    let target = Library.associate store projects[0] document.Id FocusedSource |> associated
    let other = Library.associate store projects[1] document.Id ReferenceSource |> associated
    let sameProject = Library.associate store projects[0] unrelated.Id FocusedSource |> associated
    let before = roleSnapshot workspace.Path
    use connection = openConnection workspace.Path
    // A BEFORE trigger gives deterministic read -> delete -> zero-row UPDATE
    // ordering without a production hook or scheduling a competing request.
    let body =
        if abortUpdate then "SELECT RAISE(ABORT, 'role update denied');"
        else $"DELETE FROM ProjectDocuments WHERE ProjectId = '{projects[0]:D}' AND DocumentId = '{document.Id:D}'; SELECT RAISE(IGNORE);"
    execute connection $"CREATE TRIGGER remove_before_role_update BEFORE UPDATE OF Role ON ProjectDocuments WHEN OLD.ProjectId = '{projects[0]:D}' AND OLD.DocumentId = '{document.Id:D}' BEGIN {body} END"
    let outcome =
        try Choice1Of2 (DocumentRoles.changeRole (LibraryRepository.changeRole workspace.Path) projects[0] document.Id ReferenceSource)
        with error -> Choice2Of2 error
    // Verify deletion actually persisted, even when SaveChanges threw. Do not
    // mistake a rolled-back trigger deletion for the missing-association race.
    assertResult (Ok (if abortUpdate then [ target; sameProject ] |> List.sortBy (fun item -> item.AddedUtc, item.Document.Id) else [ sameProject ])) (Library.listForProject store projects[0])
    assertResult (Ok [ other ]) (Library.listForProject store projects[1])
    let after = roleSnapshot workspace.Path
    assertResult (before |> List.take 3) (after |> List.take 3)
    let expectedAssociations =
        before |> List.last |> snd
        |> List.filter (fun row -> abortUpdate || row[0] <> projects[0].ToString("D") || row[1] <> document.Id.ToString("D"))
    assertResult expectedAssociations (after |> List.last |> snd)
    assertStoredOriginal workspace.Path document (fixture "valid.pdf")
    assertStoredOriginal workspace.Path unrelated (fixture "distinct.pdf")
    if abortUpdate then
        match outcome with
        | Choice2Of2 (:? DbUpdateException as error) ->
            let sqlite = Assert.IsType<SqliteException>(error.InnerException)
            Assert.Equal(19, sqlite.SqliteErrorCode)
            Assert.Contains("role update denied", sqlite.Message)
        | other -> Assert.Fail(sprintf "Expected unrelated database failure to propagate, got %A" other)
    else
        match outcome with
        | Choice1Of2 result -> assertResult (Error DocumentRoles.RoleChangeError.AssociationNotFound) result
        | Choice2Of2 error -> Assert.Fail(sprintf "Expected AssociationNotFound, got %s: %s" (error.GetType().FullName) error.Message)
