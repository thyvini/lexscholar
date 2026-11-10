/// Startup cleanup after interrupted imports (T022). Post-crash filesystem
/// states are arranged directly; there are no fault-injection hooks.
module LexScholar.Infrastructure.Tests.StorageCleanupTests

open System
open System.IO
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private privateDirectory = UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute

/// A migrated, isolated data directory.
type private Workspace() =
    let directory = new TemporaryDirectory()
    do WorkspaceDb.initialize directory.Path
    member _.Path = directory.Path
    member _.Staging = Path.Combine(directory.Path, "staging")
    member _.Originals = Path.Combine(directory.Path, "originals")
    member _.Quarantine = Path.Combine(directory.Path, "quarantine")
    interface IDisposable with member _.Dispose() = (directory :> IDisposable).Dispose()

let private newKey () = Guid.NewGuid().ToString("N")

let private writeFile directory name (bytes: byte array) (mode: UnixFileMode) =
    Directory.CreateDirectory(directory) |> ignore
    let path = Path.Combine(directory, name)
    File.WriteAllBytes(path, bytes)
    File.SetUnixFileMode(path, mode)
    path

/// A committed document: a row and its read-only original.
let private addDocument (workspace: Workspace) (bytes: byte array) =
    let key = newKey ()
    writeFile workspace.Originals key bytes UnixFileMode.UserRead |> ignore
    use connection = openConnection workspace.Path
    let sha = String('a', 64)
    execute connection $"INSERT INTO LibraryDocuments (Id, Sha256, StorageKey, OriginalFileName, ByteLength, PageCount, ImportedUtc) VALUES ('{Guid.NewGuid():D}', '{sha}', '{key}', 'kept.pdf', {bytes.Length}, 1, '2026-05-17T12:34:56.0000000Z')"
    key

let private documentRows (workspace: Workspace) =
    use connection = openConnection workspace.Path
    use command = connection.CreateCommand()
    command.CommandText <- "SELECT Id || '/' || StorageKey || '/' || ByteLength FROM LibraryDocuments ORDER BY 1"
    use reader = command.ExecuteReader()
    [ while reader.Read() do yield reader.GetString(0) ]

let private entries directory =
    Directory.EnumerateFileSystemEntries(directory) |> Seq.map Path.GetFileName |> Seq.sort |> Seq.toList

[<Fact; Trait("Task", "T022")>]
let ``leftover staging files are deleted`` () =
    use workspace = new Workspace()
    writeFile workspace.Staging (newKey ()) [| 1uy; 2uy |] (UnixFileMode.UserRead ||| UnixFileMode.UserWrite) |> ignore
    // Interrupted after the staged file was made read-only, before the rename.
    writeFile workspace.Staging (newKey ()) [| 3uy |] UnixFileMode.UserRead |> ignore
    let report = StorageCleanup.run workspace.Path
    Assert.Equal(2, report.DeletedStagingFiles)
    Assert.True(Directory.Exists(workspace.Staging))
    Assert.Empty(entries workspace.Staging)

[<Fact; Trait("Task", "T022")>]
let ``an unreferenced original is moved to quarantine and reported, never deleted`` () =
    use workspace = new Workspace()
    let orphan = newKey ()
    let bytes = [| 9uy; 8uy; 7uy |]
    writeFile workspace.Originals orphan bytes UnixFileMode.UserRead |> ignore
    let report = StorageCleanup.run workspace.Path
    Assert.Equal<string list>([ orphan ], report.Quarantined)
    Assert.False(File.Exists(Path.Combine(workspace.Originals, orphan)))
    let quarantined = Path.Combine(workspace.Quarantine, orphan)
    Assert.True(File.Exists(quarantined), "The unreferenced original is not in quarantine.")
    Assert.Equal<byte array>(bytes, File.ReadAllBytes(quarantined))

[<Fact; Trait("Task", "T022")>]
let ``referenced originals and document rows are unchanged`` () =
    use workspace = new Workspace()
    let bytes = [| 4uy; 5uy; 6uy |]
    let kept = addDocument workspace bytes
    let orphan = newKey ()
    writeFile workspace.Originals orphan [| 1uy |] UnixFileMode.UserRead |> ignore
    writeFile workspace.Staging (newKey ()) [| 2uy |] UnixFileMode.UserRead |> ignore
    let rowsBefore = documentRows workspace
    let report = StorageCleanup.run workspace.Path
    Assert.Equal<string list>([ orphan ], report.Quarantined)
    Assert.Equal<string list>([ kept ], entries workspace.Originals)
    let path = Path.Combine(workspace.Originals, kept)
    Assert.Equal<byte array>(bytes, File.ReadAllBytes(path))
    Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(path))
    Assert.Equal<string list>(rowsBefore, documentRows workspace)

[<Fact; Trait("Task", "T022")>]
let ``missing storage directories are created privately`` () =
    use workspace = new Workspace()
    let report = StorageCleanup.run workspace.Path
    Assert.Equal(0, report.DeletedStagingFiles)
    Assert.Empty(report.Quarantined)
    for directory in [ workspace.Staging; workspace.Originals; workspace.Quarantine ] do
        Assert.True(Directory.Exists(directory), $"{directory} was not created.")
        Assert.Equal(privateDirectory, File.GetUnixFileMode(directory))

[<Fact; Trait("Task", "T022")>]
let ``a second cleanup changes nothing`` () =
    use workspace = new Workspace()
    let kept = addDocument workspace [| 4uy |]
    let orphan = newKey ()
    writeFile workspace.Originals orphan [| 1uy |] UnixFileMode.UserRead |> ignore
    writeFile workspace.Staging (newKey ()) [| 2uy |] UnixFileMode.UserRead |> ignore
    StorageCleanup.run workspace.Path |> ignore
    let second = StorageCleanup.run workspace.Path
    Assert.Equal(0, second.DeletedStagingFiles)
    Assert.Empty(second.Quarantined)
    Assert.Equal<string list>([ kept ], entries workspace.Originals)
    Assert.Equal<string list>([ orphan ], entries workspace.Quarantine)
    Assert.Empty(entries workspace.Staging)
