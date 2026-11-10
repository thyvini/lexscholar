/// Startup cleanup of import leftovers; the caller holds the data-directory lock
/// and runs it after migrations and before serving requests.
module LexScholar.Infrastructure.StorageCleanup

/// What cleanup changed. Quarantined holds storage keys (file names), never paths.
type CleanupReport = { DeletedStagingFiles: int; Quarantined: string list }

// RED stub (T022): behavior is implemented in T028.
let run (dataDirectory: string) : CleanupReport =
    { DeletedStagingFiles = 0; Quarantined = [] }
