/// The stored summary repository (F004 T006, research §5).
module LexScholar.Infrastructure.Tests.SummaryRepositoryTests

open System
open System.IO
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private sha = "0fcd00dc1ad92711575dfde326cda6c56b8fd83b0a01462cab6bfd012dcbe93b"
let private time = "2026-11-12T12:00:00.0000000Z"

/// A migrated workspace with one project and one associated document; returns the key.
let private seed (directory: string) (storageKey: string) =
    let workspace, project, document = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    use connection = openConnection directory
    addWorkspace connection workspace
    addProject connection project workspace "ONE"
    execute connection $"INSERT INTO LibraryDocuments (Id, Sha256, StorageKey, OriginalFileName, ByteLength, PageCount, ImportedUtc) VALUES ('{document:D}', '{sha}', '{storageKey}', 'valid.pdf', 753, 2, '{time}')"
    execute connection $"INSERT INTO ProjectDocuments (ProjectId, DocumentId, Role, AddedUtc) VALUES ('{project:D}', '{document:D}', 'FocusedSource', '{time}')"
    { Project = project; Document = document }

let private summaryOf (key: SummaryKey) : StoredSummary =
    { DocumentId = key.Document
      Points = [ { Text = "Main idea"; Pages = [ 1; 2 ] }; { Text = "Second, with \"quotes\" and ünïcode"; Pages = [ 2 ] } ]
      PagesCovered = [ 1; 2; 4 ]
      UnreadablePages = [ 3 ]
      OmittedPoints = 2
      GeneratedAt = DateTimeOffset(2026, 11, 12, 12, 30, 15, 123, TimeSpan.Zero)
      ServiceHost = "service.example:443"
      Model = "model-1" }

let private migrated () =
    let directory = new TemporaryDirectory()
    WorkspaceDb.initialize directory.Path
    directory

[<Fact; Trait("Task", "T006")>]
let ``a saved summary is found equal to the original`` () =
    use directory = migrated ()
    let key = seed directory.Path "k-1"
    let summary = summaryOf key
    SummaryRepository.save directory.Path key summary
    Assert.Equal(Some summary, SummaryRepository.find directory.Path key)

[<Fact; Trait("Task", "T006")>]
let ``saving again replaces the stored summary`` () =
    use directory = migrated ()
    let key = seed directory.Path "k-1"
    SummaryRepository.save directory.Path key (summaryOf key)
    let newer = { summaryOf key with Model = "model-2"; OmittedPoints = 0 }
    SummaryRepository.save directory.Path key newer
    Assert.Equal(Some newer, SummaryRepository.find directory.Path key)

[<Fact; Trait("Task", "T006")>]
let ``an unknown content version is rejected`` () =
    use directory = migrated ()
    let key = seed directory.Path "k-1"
    use connection = openConnection directory.Path
    execute connection $"INSERT INTO DocumentSummaries (ProjectId, DocumentId, Content, GeneratedAt) VALUES ('{key.Project:D}', '{key.Document:D}', '{{\"version\":2}}', '{time}')"
    Assert.Throws<InvalidDataException>(fun () -> SummaryRepository.find directory.Path key |> ignore) |> ignore

[<Fact; Trait("Task", "T006")>]
let ``delete removes the row and listKeys lists what is stored`` () =
    use directory = migrated ()
    let first = seed directory.Path "k-1"
    let second = seed directory.Path "k-2"
    SummaryRepository.save directory.Path first (summaryOf first)
    SummaryRepository.save directory.Path second (summaryOf second)
    Assert.Equal<SummaryKey list>([ first; second ] |> List.sort, SummaryRepository.listKeys directory.Path |> List.sort)
    SummaryRepository.delete directory.Path first
    Assert.Equal(None, SummaryRepository.find directory.Path first)
    Assert.Equal<SummaryKey list>([ second ], SummaryRepository.listKeys directory.Path)
    // Deleting what is not there is not an error.
    SummaryRepository.delete directory.Path first
