module LexScholar.Server.Tests.PlaceholderSmoke

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Server
open Xunit

[<Fact>]
let ``functional capabilities fault explicitly without performing domain behavior`` () =
    task {
        let projects = Projects.unimplemented
        let library = Library.unimplemented
        let ct = CancellationToken.None
        let operations : (unit -> Task) list =
            [ (fun () -> projects.List ct :> Task)
              (fun () -> projects.Create "unused" ct :> Task)
              (fun () -> projects.Select Guid.Empty ct :> Task)
              (fun () -> library.List ct :> Task)
              (fun () -> library.Import Stream.Null "unused.pdf" ct :> Task)
              (fun () -> library.ListForProject Guid.Empty ct :> Task)
              (fun () -> library.Associate Guid.Empty Guid.Empty FocusedSource ct :> Task)
              (fun () -> library.ChangeRole Guid.Empty Guid.Empty ReferenceSource ct :> Task)
              (fun () -> library.RemoveAssociation Guid.Empty Guid.Empty ct :> Task)
              (fun () -> VerifiedDocumentContent.openRead Guid.Empty ct :> Task) ]
        for invoke in operations do
            let! error = Assert.ThrowsAsync<NotImplementedException>(Func<Task>(invoke))
            Assert.Contains("not implemented", error.Message)
    }

[<Fact>]
let ``configuration and locking are implemented independently of remaining stubs`` () =
    use workspace = TemporaryWorkspace.Create()
    let configuration = HostSetup.builder workspace.Root []
    match Configuration.load configuration.Configuration with
    | Error error -> Assert.Fail($"Valid configuration was rejected: {error}")
    | Ok settings ->
        Assert.Equal(100_000_000L, settings.ImportLimits.MaxBytes)
        match DataDirectoryLock.acquire settings.DataDirectory with
        | Ok handle -> handle.Dispose()
        | Error error -> Assert.Fail($"Unowned test directory was rejected: {error}")
