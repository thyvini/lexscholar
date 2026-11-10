/// Private filesystem storage for staged uploads and read-only originals, under
/// the data directory. File names come only from generated UUIDs.
module LexScholar.Infrastructure.DocumentStore

open System
open System.IO
open System.Threading.Tasks
open LexScholar.Application

let stagingDirectory dataDirectory = Path.Combine(dataDirectory, "staging")
let originalsDirectory dataDirectory = Path.Combine(dataDirectory, "originals")
let quarantineDirectory dataDirectory = Path.Combine(dataDirectory, "quarantine")

// RED stubs (T021/T022): behavior is implemented in T026.

let openStagedWrite (dataDirectory: string) : OpenStagedWrite =
    fun _ _ -> Task.FromResult(new MemoryStream() :> Stream)

let openStagedRead (dataDirectory: string) : OpenStagedRead =
    fun _ _ -> Task.FromResult(new MemoryStream() :> Stream)

let promote (dataDirectory: string) : PromoteStagedContent =
    fun _ _ -> Task.FromResult(Guid.NewGuid().ToString("N"))

let discard (dataDirectory: string) : DiscardStagedContent =
    fun _ _ -> Task.FromResult(())

let openOriginal (dataDirectory: string) : OpenStoredOriginal =
    fun _ _ -> Task.FromResult(new MemoryStream() :> Stream)
