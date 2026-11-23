module LexScholar.Server.Tests.LibraryCoordinationContractTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open LexScholar.Application
open LexScholar.Infrastructure
open LexScholar.Domain
open Xunit

[<Fact; Trait("Task", "T003")>]
let ``host import shares the file lifetime boundary`` () = task {
    let validated = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let validate : ValidatePdf =
        fun stream limits token -> task {
            let! result = PdfValidator.validate stream limits token
            validated.TrySetResult() |> ignore
            return result }
    use host = TestHarness.Start(pdfValidation = validate)
    let coordination = host.App.Services.GetRequiredService<LibraryCoordination>()
    use! lease = coordination.FileLease(CancellationToken.None)
    use tokenResponse = host.Client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    use tokenJson = JsonDocument.Parse(tokenResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    let token = tokenJson.RootElement.GetProperty("requestToken").GetString()
    use form = new MultipartFormDataContent()
    let bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
    form.Add(new ByteArrayContent(bytes), "file", "valid.pdf")
    use request = new HttpRequestMessage(HttpMethod.Post, "/api/documents")
    request.Headers.TryAddWithoutValidation("Origin", host.Client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token) |> ignore
    request.Content <- form
    let upload = host.Client.SendAsync(request)
    do! validated.Task
    Assert.Empty(LibraryRepository.list host.Root)
    Assert.False(upload.IsCompleted)
    lease.Dispose()
    use! response = upload
    Assert.Equal(HttpStatusCode.Created, response.StatusCode)
    Assert.Single(LibraryRepository.list host.Root) |> ignore
}

[<Fact; Trait("Task", "T003")>]
let ``registry database completion proceeds while simulated deletion waits for files`` () = task {
    use host = TestHarness.Start()
    let coordination = host.App.Services.GetRequiredService<LibraryCoordination>()
    let workspace, project, document = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    let now = DateTimeOffset.UtcNow
    do
        use db = WorkspaceDb.create host.Root
        db.Add(WorkspaceRow(Id = workspace, CreatedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = "One", NormalizedName = "ONE", CreatedUtc = now)) |> ignore
        db.Add(LibraryDocumentRow(Id = document, Sha256 = "hash", StorageKey = "key", OriginalFileName = "one.pdf", ByteLength = 1L, PageCount = 1, ImportedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
        db.Add(ProjectDocumentRow(ProjectId = project, DocumentId = document, Role = "FocusedSource", AddedUtc = now)) |> ignore
        db.SaveChanges() |> ignore
    let key = { Project = project; Document = document }
    let handle = TaskCompletionSource<JobHandle>(TaskCreationOptions.RunContinuationsAsynchronously)
    let done' = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable savedInsideBoundary = false
    let jobs =
        SummaryJobs(
            { Find = SummaryRepository.find host.Root
              Save = fun k summary ->
                  savedInsideBoundary <- coordination.IsMutationHeld
                  SummaryRepository.save host.Root k summary
              Delete = SummaryRepository.delete host.Root
              AssociationExists = fun _ -> true }, coordination = coordination)
    let run given = task { handle.SetResult(given); do! done'.Task }
    use! exportLease = coordination.FileLease(CancellationToken.None)
    let deleting = coordination.FileLease(CancellationToken.None)
    Assert.False(deleting.IsCompleted)
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, run))
    let! job = handle.Task
    let summary =
        { DocumentId = document; Points = []; PagesCovered = []; UnreadablePages = []
          OmittedPoints = 0; GeneratedAt = now; ServiceHost = "local"; Model = "m" }
    Assert.True(jobs.Complete(key, job.Id, summary))
    Assert.True(savedInsideBoundary)
    Assert.True((SummaryRepository.find host.Root key).IsSome)
    exportLease.Dispose()
    use! deletionLease = deleting
    let result =
        coordination.Mutation(fun () ->
            jobs.RemoveAssociation(key, fun () -> LibraryRepository.removeAssociation host.Root project document))
    Assert.True(Result.isOk result)
    Assert.True((SummaryRepository.find host.Root key).IsNone)
    Assert.Single(LibraryRepository.list host.Root) |> ignore
    done'.SetResult()
}
