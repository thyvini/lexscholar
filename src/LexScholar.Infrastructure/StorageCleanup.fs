/// Startup cleanup of import leftovers; the caller holds the data-directory lock
/// and runs it after migrations and before serving requests.
module LexScholar.Infrastructure.StorageCleanup

open System.IO
open System.Threading
open LexScholar.Application

/// What cleanup changed. Quarantined and Conflicting hold storage keys (file
/// names), never paths. Conflicting keys are unreferenced originals left in place
/// because quarantine already holds different bytes under the same key.
type CleanupReport = { DeletedStagingFiles: int; Quarantined: string list; Conflicting: string list }

let private sameContent (left: string) (right: string) =
    use leftStream = File.OpenRead(left)
    use rightStream = File.OpenRead(right)
    (ImportDocument.contentEquals leftStream rightStream CancellationToken.None).GetAwaiter().GetResult()

/// Deletes all staging contents (no import is in progress at startup) and moves
/// stored originals that no document row references to quarantine/, without
/// overwriting: when quarantine already holds different bytes under the same
/// key, both files are left in place and the key is reported as Conflicting.
/// Referenced originals and rows are never touched.
let run (dataDirectory: string) : CleanupReport =
    DocumentStore.ensureDirectories dataDirectory
    let staging = DocumentStore.stagingDirectory dataDirectory
    let originals = DocumentStore.originalsDirectory dataDirectory
    let quarantine = DocumentStore.quarantineDirectory dataDirectory
    let leftovers = Directory.GetFileSystemEntries(staging)
    for entry in leftovers do
        if Directory.Exists(entry) then Directory.Delete(entry, true) else File.Delete(entry)
    let referenced = LibraryRepository.storageKeys dataDirectory
    let moves =
        [ for path in Directory.GetFiles(originals) |> Array.sort do
            let key = Path.GetFileName(path)
            if not (referenced.Contains key) then
                let destination = Path.Combine(quarantine, key)
                if not (File.Exists(destination)) then
                    File.Move(path, destination, false)
                    yield Ok key
                // A move interrupted after linking the quarantine name leaves the
                // same bytes under both names; removing the original completes it.
                elif sameContent path destination then
                    File.Delete(path)
                    yield Ok key
                else
                    yield Error key ]
    { DeletedStagingFiles = leftovers.Length
      Quarantined = moves |> List.choose (function Ok key -> Some key | Error _ -> None)
      Conflicting = moves |> List.choose (function Error key -> Some key | Ok _ -> None) }
