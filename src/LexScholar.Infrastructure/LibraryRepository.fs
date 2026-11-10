module LexScholar.Infrastructure.LibraryRepository

open System.Threading.Tasks
open LexScholar.Application

// RED stubs (T021/T022): behavior is implemented in T028.

let findBySha256 (dataDirectory: string) : FindDocumentsBySha256 =
    fun _ _ -> Task.FromResult([])

let insert (dataDirectory: string) : InsertDocument =
    fun _ _ -> Task.FromResult(())

/// Every storage key that a document row references.
let storageKeys (dataDirectory: string) : Set<string> = Set.empty
