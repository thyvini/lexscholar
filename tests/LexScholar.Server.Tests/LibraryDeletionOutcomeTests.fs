module LexScholar.Server.Tests.LibraryDeletionOutcomeTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open LexScholar.Application
open LexScholar.Infrastructure
open LexScholar.Server
open Xunit

let private post (h: TestHarness) (path: string) body =
    use tokenResponse = h.Client.GetAsync("/api/antiforgery").Result
    use token = JsonDocument.Parse(tokenResponse.Content.ReadAsStringAsync().Result)
    use request = new HttpRequestMessage(HttpMethod.Post, path)
    request.Headers.Add("Origin", h.Client.BaseAddress.GetLeftPart(UriPartial.Authority))
    request.Headers.Add("X-CSRF-TOKEN", token.RootElement.GetProperty("requestToken").GetString())
    request.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    h.Client.Send(request)

let private seed root =
    use db = WorkspaceDb.create root
    let ids = [ Guid.NewGuid(); Guid.NewGuid() ]
    Directory.CreateDirectory(Path.Combine(root, "originals")) |> ignore
    for id in ids do
        File.WriteAllText(Path.Combine(root, "originals", $"{id:N}"), "fixture")
        db.Add(LibraryDocumentRow(Id = id, OriginalFileName = $"{id}.pdf", StorageKey = $"{id:N}",
                                 Sha256 = string id, ByteLength = 7L, PageCount = 1, ImportedUtc = DateTimeOffset.UtcNow)) |> ignore
    db.SaveChanges() |> ignore
    ids

let private confirm (h: TestHarness) operation ids =
    let preview = LibraryDeletion.preview (LibraryDeletionRepository.capture h.Root) ids |> Result.toOption |> Option.get
    JsonSerializer.Serialize({| operationId = operation; documentIds = ids; revision = preview.Revision |})

let private lookup (h: TestHarness) operation =
    use response = h.Client.GetAsync($"/api/library/deletions/{operation}").Result
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    JsonDocument.Parse(response.Content.ReadAsStringAsync().Result)

[<Fact; Trait("Task", "T016")>]
let ``retained outcomes survive restart and acknowledge refuses incomplete work`` () =
    use workspace = TemporaryWorkspace.Create()
    let operation = Guid.NewGuid()
    let mutable body = ""
    let mutable ids = []
    do
        use h = TestHarness.Start(workspace = workspace, configure = fun app ->
            LibraryDeletionEndpoints.mapForTests app app.Configuration["LexScholar:DataDirectory"])
        ids <- seed h.Root
        body <- confirm h operation ids
        // A 404 before admission is unknown, not permission to replace the UUID.
        use absent = h.Client.GetAsync($"/api/library/deletions/{operation}").Result
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode)
        use accepted = post h "/api/library/deletions" body
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode)
        use outcome = lookup h operation
        Assert.Equal("partial", outcome.RootElement.GetProperty("status").GetString())
        let items = outcome.RootElement.GetProperty("items").EnumerateArray() |> Seq.toList
        Assert.Equal(2, items.Length)
        for item in items do
            Assert.Equal("not_completed", item.GetProperty("outcome").GetString())
            Assert.Equal("pending", item.GetProperty("reason").GetString())
        use refused = post h $"/api/library/deletions/{operation}/acknowledge" "{}"
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode)
    use h = TestHarness.Start(workspace = workspace, deletionRecovery = LibraryDeletionRecovery.StartupForTests(ignore), configure = fun app ->
        LibraryDeletionEndpoints.mapForTests app app.Configuration["LexScholar:DataDirectory"])
    use retained = lookup h operation
    use list = h.Client.GetAsync("/api/library/deletions").Result
    Assert.Equal(HttpStatusCode.OK, list.StatusCode)
    use all = JsonDocument.Parse(list.Content.ReadAsStringAsync().Result)
    Assert.Single(all.RootElement.EnumerateArray()) |> ignore
    // Identical confirmation is safe even when its original answer was lost.
    use repeated = post h "/api/library/deletions" body
    Assert.Equal(HttpStatusCode.Accepted, repeated.StatusCode)
    Assert.All(ids, fun id -> Assert.True(File.Exists(Path.Combine(h.Root, "originals", $"{id:N}"))))

[<Fact; Trait("Task", "T017")>]
let ``production confirmation admits a durable operation`` () =
    use h = TestHarness.Start()
    let ids = seed h.Root
    let operation = Guid.NewGuid()
    use admission = post h "/api/library/deletions" (confirm h operation ids)
    Assert.Equal(HttpStatusCode.Accepted, admission.StatusCode)
    let runner = (h.App :> IApplicationBuilder).Properties["LexScholar.LibraryDeletions"] :?> LibraryDeletion.Operations
    (runner.Settled operation).GetAwaiter().GetResult()
    use db = WorkspaceDb.create h.Root
    Assert.Single(db.Set<DeletionOperationRow>()) |> ignore

[<Fact; Trait("Task", "T017")>]
let ``production retry accepts an already retained intent`` () =
    use h = TestHarness.Start()
    let ids = seed h.Root
    let operation = Guid.NewGuid()
    let coordination = h.App.Services.GetRequiredService<LibraryCoordination>()
    let preview = LibraryDeletion.preview (LibraryDeletionRepository.capture h.Root) ids |> Result.toOption |> Option.get
    Assert.Equal(Ok operation, LibraryDeletionRepository.admit coordination h.Root operation ids preview.Revision)
    use retry = post h $"/api/library/deletions/{operation}/retry" "{}"
    Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode)
    let runner = (h.App :> IApplicationBuilder).Properties["LexScholar.LibraryDeletions"] :?> LibraryDeletion.Operations
    (runner.Settled operation).GetAwaiter().GetResult()

[<Fact; Trait("Task", "T017")>]
let ``production deletion POST routes retain Host Origin and CSRF protections`` () =
    use h = TestHarness.Start()
    use tokenResponse = h.Client.GetAsync("/api/antiforgery").Result
    use token = JsonDocument.Parse(tokenResponse.Content.ReadAsStringAsync().Result)
    let operation = Guid.NewGuid()
    for path in ["/api/library/deletions"; "/api/library/deletions/preview"
                 $"/api/library/deletions/{operation}/retry"; $"/api/library/deletions/{operation}/acknowledge"] do
        for invalid in ["csrf"; "origin"; "host"] do
            use request = new HttpRequestMessage(HttpMethod.Post, path)
            if invalid <> "csrf" then request.Headers.Add("X-CSRF-TOKEN", token.RootElement.GetProperty("requestToken").GetString())
            request.Headers.Add("Origin", if invalid = "origin" then "https://example.invalid" else h.Client.BaseAddress.GetLeftPart(UriPartial.Authority))
            if invalid = "host" then request.Headers.Host <- "example.invalid"
            request.Content <- new StringContent("{}", Encoding.UTF8, "application/json")
            use response = h.Client.Send(request)
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)
    use db = WorkspaceDb.create h.Root
    Assert.Empty(db.Set<DeletionOperationRow>())

[<Fact; Trait("Task", "T016")>]
let ``detached processing reports running then partial and restart retry completes before acknowledgement`` () = task {
    use workspace = TemporaryWorkspace.Create()
    let operation = Guid.NewGuid()
    let arrived = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let release = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable runner = Unchecked.defaultof<LibraryDeletion.Operations>
    let mutable failedDocument = Guid.Empty
    let mutable calls = 0
    let mutable ids = []
    let mutable body = ""
    let! () = task {
        use h = TestHarness.Start(workspace = workspace, configure = fun app ->
            let root = app.Configuration["LexScholar:DataDirectory"]
            let coordination = app.Services.GetRequiredService<LibraryCoordination>()
            let jobs = (app :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
            let runItem op document = task {
                calls <- calls + 1
                if calls = 1 then
                    failedDocument <- document
                    arrived.SetResult()
                    do! release.Task
                let finish = LibraryDeletionRepository.finishOriginalWith ignore (fun key ->
                    if document = failedDocument then raise (IOException("sensitive storage path"))
                    DocumentStore.removeOriginal root key) coordination root
                return! LibraryDeletion.processItem coordination jobs
                             (LibraryDeletionRepository.hasItem root)
                             (LibraryDeletionRepository.removeMetadata coordination root)
                             finish op document app.Lifetime.ApplicationStopping }
            runner <- LibraryDeletionEndpoints.mapWithProcessingForTests app root runItem)
        ids <- seed h.Root
        body <- confirm h operation ids
        use admitted = post h "/api/library/deletions" body
        Assert.Equal(HttpStatusCode.Accepted, admitted.StatusCode)
        do! arrived.Task.WaitAsync(TimeSpan.FromSeconds 10.0)
        use running = lookup h operation
        Assert.Equal("running", running.RootElement.GetProperty("status").GetString())
        use duplicate = post h "/api/library/deletions" body
        use retryRunning = post h $"/api/library/deletions/{operation}/retry" "{}"
        Assert.Equal(HttpStatusCode.Accepted, retryRunning.StatusCode)
        Assert.Equal(1, calls)
        use refused = post h $"/api/library/deletions/{operation}/acknowledge" "{}"
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode)
        let settled = runner.Settled operation
        release.SetResult()
        do! settled.WaitAsync(TimeSpan.FromSeconds 10.0)
        Assert.Equal(2, calls)
        use partial = lookup h operation
        Assert.Equal("partial", partial.RootElement.GetProperty("status").GetString())
        let items = partial.RootElement.GetProperty("items").EnumerateArray() |> Seq.toList
        for item in items do
            let failed = item.GetProperty("documentId").GetGuid() = failedDocument
            Assert.Equal((if failed then "not_completed" else "deleted"), item.GetProperty("outcome").GetString())
            Assert.Equal((if failed then "storage_error" else null), item.GetProperty("reason").GetString())
        Assert.DoesNotContain("sensitive", partial.RootElement.GetRawText())
        Assert.DoesNotContain("storageKey", partial.RootElement.GetRawText())
        Assert.True(File.Exists(Path.Combine(h.Root, "originals", $"{failedDocument:N}"))) }
    use h = TestHarness.Start(workspace = workspace, deletionRecovery = LibraryDeletionRecovery.StartupForTests(ignore), configure = fun app ->
        let root = app.Configuration["LexScholar:DataDirectory"]
        let coordination = app.Services.GetRequiredService<LibraryCoordination>()
        let jobs = (app :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
        let runItem op document = task {
            return! LibraryDeletion.processItem coordination jobs
                         (LibraryDeletionRepository.hasItem root)
                         (LibraryDeletionRepository.removeMetadata coordination root)
                         (LibraryDeletionRepository.finishOriginal coordination root)
                         op document app.Lifetime.ApplicationStopping }
        runner <- LibraryDeletionEndpoints.mapWithProcessingForTests app root runItem)
    use partial = lookup h operation
    Assert.Equal("partial", partial.RootElement.GetProperty("status").GetString())
    use refused = post h $"/api/library/deletions/{operation}/acknowledge" "{}"
    Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode)
    use retry = post h $"/api/library/deletions/{operation}/retry" "{}"
    Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode)
    do! (runner.Settled operation).WaitAsync(TimeSpan.FromSeconds 10.0)
    use complete = lookup h operation
    Assert.Equal("complete", complete.RootElement.GetProperty("status").GetString())
    Assert.All(complete.RootElement.GetProperty("items").EnumerateArray(), fun item ->
        Assert.Equal("deleted", item.GetProperty("outcome").GetString())
        Assert.Equal(JsonValueKind.Null, item.GetProperty("reason").ValueKind))
    for id in ids do
        Assert.False(File.Exists(Path.Combine(h.Root, "originals", $"{id:N}")))
    // The lost-answer request remains idempotent after all source rows vanished.
    use repeated = post h "/api/library/deletions" body
    Assert.Equal(HttpStatusCode.Accepted, repeated.StatusCode)
    use conflict = post h "/api/library/deletions"
                        (JsonSerializer.Serialize({| operationId = operation; documentIds = [ids.Head]; revision = "old" |}))
    Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode)
    use acknowledged = post h $"/api/library/deletions/{operation}/acknowledge" "{}"
    Assert.Equal(HttpStatusCode.NoContent, acknowledged.StatusCode)
    use absent = h.Client.GetAsync($"/api/library/deletions/{operation}").Result
    Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode)
    use unknownRetry = post h $"/api/library/deletions/{operation}/retry" "{}"
    Assert.Equal(HttpStatusCode.NotFound, unknownRetry.StatusCode)
    use db = WorkspaceDb.create h.Root
    Assert.Empty(db.Set<DeletionOperationRow>())
    Assert.Empty(db.Set<DeletionItemRow>())
}

[<Fact; Trait("Task", "T016")>]
let ``intent lookup failure is reported as storage error rather than pending`` () = task {
    let mutable runner = Unchecked.defaultof<LibraryDeletion.Operations>
    use h = TestHarness.Start(configure = fun app ->
        let root = app.Configuration["LexScholar:DataDirectory"]
        let coordination = app.Services.GetRequiredService<LibraryCoordination>()
        let jobs = (app :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
        let runItem op document = task {
            return! LibraryDeletion.processItem coordination jobs
                         (fun _ _ -> raise (IOException("private intent lookup failure")))
                         (LibraryDeletionRepository.removeMetadata coordination root)
                         (LibraryDeletionRepository.finishOriginal coordination root)
                         op document app.Lifetime.ApplicationStopping }
        runner <- LibraryDeletionEndpoints.mapWithProcessingForTests app root runItem)
    let ids = seed h.Root
    let operation = Guid.NewGuid()
    use admitted = post h "/api/library/deletions" (confirm h operation ids)
    Assert.Equal(HttpStatusCode.Accepted, admitted.StatusCode)
    do! (runner.Settled operation).WaitAsync(TimeSpan.FromSeconds 10.0)
    use outcome = lookup h operation
    Assert.Equal("partial", outcome.RootElement.GetProperty("status").GetString())
    for item in outcome.RootElement.GetProperty("items").EnumerateArray() do
        Assert.Equal("not_completed", item.GetProperty("outcome").GetString())
        Assert.Equal("storage_error", item.GetProperty("reason").GetString())
    use db = WorkspaceDb.create h.Root
    Assert.All(db.Set<DeletionItemRow>(), fun item ->
        Assert.Equal("incomplete", item.Status)
        Assert.Equal("pending", item.Phase)
        Assert.Equal("storage_error", item.Reason))
}

[<Fact; Trait("Task", "T016")>]
let ``completed markers remain running until processor exits and acknowledgement is allowed`` () = task {
    let arrived = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let release = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable runner = Unchecked.defaultof<LibraryDeletion.Operations>
    use h = TestHarness.Start(configure = fun app ->
        let root = app.Configuration["LexScholar:DataDirectory"]
        let coordination = app.Services.GetRequiredService<LibraryCoordination>()
        let jobs = (app :> IApplicationBuilder).Properties["LexScholar.SummaryJobs"] :?> SummaryJobs
        let runItem op document = task {
            let! result = LibraryDeletion.processItem coordination jobs
                              (LibraryDeletionRepository.hasItem root)
                              (LibraryDeletionRepository.removeMetadata coordination root)
                              (LibraryDeletionRepository.finishOriginal coordination root)
                              op document app.Lifetime.ApplicationStopping
            Assert.Equal(Ok (), result)
            if LibraryDeletionRepository.results root |> List.forall (fun operation -> operation.Status = "complete") then
                arrived.SetResult()
                do! release.Task
            return result }
        runner <- LibraryDeletionEndpoints.mapWithProcessingForTests app root runItem)
    let ids = seed h.Root
    let operation = Guid.NewGuid()
    use admitted = post h "/api/library/deletions" (confirm h operation ids)
    Assert.Equal(HttpStatusCode.Accepted, admitted.StatusCode)
    do! arrived.Task.WaitAsync(TimeSpan.FromSeconds 10.0)
    try
        use outcome = lookup h operation
        Assert.Equal("running", outcome.RootElement.GetProperty("status").GetString())
        use refused = post h $"/api/library/deletions/{operation}/acknowledge" "{}"
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode)
    finally release.TrySetResult() |> ignore
    do! (runner.Settled operation).WaitAsync(TimeSpan.FromSeconds 10.0)
    use outcome = lookup h operation
    Assert.Equal("complete", outcome.RootElement.GetProperty("status").GetString())
    LibraryDeletionRepository.recordFailure h.Root operation ids.Head
    Assert.Equal("complete", (LibraryDeletionRepository.results h.Root |> List.exactlyOne).Status)
    use accepted = post h $"/api/library/deletions/{operation}/acknowledge" "{}"
    Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode)
}

[<Fact; Trait("Task", "T016")>]
let ``unwritable failure marker cannot yield a successful pending snapshot`` () = task {
    use h = TestHarness.Start()
    let ids = seed h.Root
    let operation = Guid.NewGuid()
    let coordination = h.App.Services.GetRequiredService<LibraryCoordination>()
    let preview = LibraryDeletion.preview (LibraryDeletionRepository.capture h.Root) ids |> Result.toOption |> Option.get
    Assert.Equal(Ok operation, LibraryDeletionRepository.admit coordination h.Root operation ids preview.Revision)
    let mutable unavailable = true
    let recordFailure op document =
        if unavailable then raise (IOException("failure marker unavailable"))
        LibraryDeletionRepository.recordFailure h.Root op document
    let runner = LibraryDeletion.Operations(
        coordination, (fun () -> LibraryDeletionRepository.results h.Root),
        LibraryDeletionRepository.acknowledge h.Root, recordFailure,
        (fun _ _ -> Task.FromResult(Error LibraryDeletion.MetadataError.StorageError)))
    Assert.True(runner.Start operation)
    do! (runner.Settled operation).WaitAsync(TimeSpan.FromSeconds 10.0)
    Assert.Throws<IOException>(fun () -> runner.Find operation |> ignore) |> ignore
    Assert.Throws<IOException>(fun () -> runner.List() |> ignore) |> ignore
    Assert.Throws<IOException>(fun () -> runner.Acknowledge operation |> ignore) |> ignore
    unavailable <- false
    let result = runner.Find operation |> Option.get
    Assert.Equal("partial", result.Status)
    Assert.All(result.Items, fun item -> Assert.Equal("storage_error", item.Reason))
    // The safe reason is durable, not merely a transient response overlay.
    let persisted = LibraryDeletionRepository.results h.Root |> List.exactlyOne
    Assert.All(persisted.Items, fun item -> Assert.Equal("storage_error", item.Reason))
}
