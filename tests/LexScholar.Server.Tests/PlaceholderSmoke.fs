module LexScholar.Server.Tests.PlaceholderSmoke

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open LexScholar.Application
open LexScholar.Infrastructure
open LexScholar.Server
open Xunit

[<Fact>]
let ``functional capabilities fault explicitly without performing domain behavior`` () =
    task {
        let projects = Projects.unimplemented
        let library = Library.unimplemented
        let journal = ImportJournal.unimplemented
        let ct = CancellationToken.None
        let pending =
            { Id = Guid.Empty
              Status = Uploading
              StagingKey = "unused-stage"
              DestinationKey = "unused-destination"
              Sha256 = None
              ByteLength = None
              OriginalFileName = "unused.pdf"
              PageCount = None
              CreatedUtc = DateTimeOffset.MinValue }
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
              (fun () -> PdfValidator.validate Stream.Null { MaxBytes = 1L; MaxPages = 1 } ct :> Task)
              (fun () -> journal.Create pending ct :> Task)
              (fun () -> journal.Update pending ct :> Task)
              (fun () -> journal.Remove Guid.Empty ct :> Task)
              (fun () -> journal.Recover ct :> Task)
              (fun () -> VerifiedDocumentContent.openRead Guid.Empty ct :> Task) ]
        for invoke in operations do
            let! error = Assert.ThrowsAsync<NotImplementedException>(Func<Task>(invoke))
            Assert.Contains("not implemented", error.Message)
    }

[<Fact>]
let ``configuration and lock functions remain unimplemented`` () =
    let configuration = ConfigurationBuilder().Build()
    Assert.Throws<NotImplementedException>(fun () -> Configuration.load configuration |> ignore) |> ignore
    let directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Assert.Throws<NotImplementedException>(fun () -> DataDirectoryLock.acquire directory |> ignore) |> ignore
    Assert.False(Directory.Exists(directory))
