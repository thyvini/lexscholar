/// Startup cleanup of import leftovers; the caller holds the data-directory lock
/// and runs it after migrations and before serving requests.
module LexScholar.Infrastructure.StorageCleanup

open System.IO

/// What cleanup changed. Quarantined holds storage keys (file names), never paths.
type CleanupReport = { DeletedStagingFiles: int; Quarantined: string list }

/// Deletes all staging contents (no import is in progress at startup) and moves
/// stored originals that no document row references to quarantine/, without
/// overwriting. Referenced originals and rows are never touched.
let run (dataDirectory: string) : CleanupReport =
    DocumentStore.ensureDirectories dataDirectory
    let staging = DocumentStore.stagingDirectory dataDirectory
    let originals = DocumentStore.originalsDirectory dataDirectory
    let quarantine = DocumentStore.quarantineDirectory dataDirectory
    let leftovers = Directory.GetFileSystemEntries(staging)
    for entry in leftovers do
        if Directory.Exists(entry) then Directory.Delete(entry, true) else File.Delete(entry)
    let referenced = LibraryRepository.storageKeys dataDirectory
    let quarantined =
        [ for path in Directory.GetFiles(originals) |> Array.sort do
            let key = Path.GetFileName(path)
            if not (referenced.Contains key) then
                File.Move(path, Path.Combine(quarantine, key), false)
                yield key ]
    { DeletedStagingFiles = leftovers.Length; Quarantined = quarantined }
