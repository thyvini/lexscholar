module LexScholar.Application.LibraryExport

open System
open System.IO
open System.Threading

[<RequireQualifiedAccess>]
type Failure =
    | EmptyLibrary
    | DocumentUnavailable of Guid * string
    | ExportFailed

/// The builder returns a complete, rewound archive; disposing it removes staging.
type Build = CancellationToken -> Result<Stream, Failure>

let create (coordination: LibraryCoordination) (build: Build) cancellationToken = task {
    use! lease = coordination.FileLease cancellationToken
    return build cancellationToken
}
