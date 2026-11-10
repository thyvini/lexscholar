/// Private filesystem storage for staged uploads and read-only originals, under
/// the data directory. File names come only from generated UUIDs, never from
/// client filenames.
module LexScholar.Infrastructure.DocumentStore

open System
open System.IO
open LexScholar.Application

let private privateDirectory = UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute

let stagingDirectory dataDirectory = Path.Combine(dataDirectory, "staging")
let originalsDirectory dataDirectory = Path.Combine(dataDirectory, "originals")
let quarantineDirectory dataDirectory = Path.Combine(dataDirectory, "quarantine")

/// Creates the storage directories, private to the current user, if missing.
let ensureDirectories dataDirectory =
    for directory in [ stagingDirectory dataDirectory; originalsDirectory dataDirectory; quarantineDirectory dataDirectory ] do
        Directory.CreateDirectory(directory, privateDirectory) |> ignore

let private stagedPath dataDirectory (StagingKey id) =
    Path.Combine(stagingDirectory dataDirectory, id.ToString("N"))

let private isStorageKey (value: string) =
    not (isNull value) && value.Length = 32
    && value |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

let private openRead (path: string) : Stream =
    new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.Asynchronous)

let openStagedWrite (dataDirectory: string) : OpenStagedWrite =
    fun key cancellationToken -> task {
        cancellationToken.ThrowIfCancellationRequested()
        Directory.CreateDirectory(stagingDirectory dataDirectory, privateDirectory) |> ignore
        let options =
            FileStreamOptions(
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.Asynchronous,
                UnixCreateMode = Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite))
        return new FileStream(stagedPath dataDirectory key, options) :> Stream }

let openStagedRead (dataDirectory: string) : OpenStagedRead =
    fun key cancellationToken -> task {
        cancellationToken.ThrowIfCancellationRequested()
        return openRead (stagedPath dataDirectory key) }

/// Flushes the staged bytes to disk, makes the file read-only, then moves it to
/// a new generated key without overwriting. On Linux, File.Move without
/// overwrite links the new name and unlinks the old one, so an existing
/// destination is never replaced. The containing directory is not synced
/// (see research.md, "Import staging and startup cleanup").
let promote (dataDirectory: string) : PromoteStagedContent =
    fun key cancellationToken -> task {
        cancellationToken.ThrowIfCancellationRequested()
        let staged = stagedPath dataDirectory key
        do
            // Opening for writing without truncating, so Flush(true) issues fsync.
            use stream = new FileStream(staged, FileMode.Open, FileAccess.Write, FileShare.None)
            stream.Flush(true)
        File.SetUnixFileMode(staged, UnixFileMode.UserRead)
        Directory.CreateDirectory(originalsDirectory dataDirectory, privateDirectory) |> ignore
        let storageKey = Guid.NewGuid().ToString("N")
        File.Move(staged, Path.Combine(originalsDirectory dataDirectory, storageKey), false)
        return storageKey }

let discard (dataDirectory: string) : DiscardStagedContent =
    fun key _ -> task {
        let path = stagedPath dataDirectory key
        if File.Exists(path) then File.Delete(path) }

let openOriginal (dataDirectory: string) : OpenStoredOriginal =
    fun storageKey cancellationToken -> task {
        cancellationToken.ThrowIfCancellationRequested()
        if not (isStorageKey storageKey) then
            invalidArg (nameof storageKey) "A storage key must be a generated 32-character lowercase hex key."
        return openRead (Path.Combine(originalsDirectory dataDirectory, storageKey)) }
