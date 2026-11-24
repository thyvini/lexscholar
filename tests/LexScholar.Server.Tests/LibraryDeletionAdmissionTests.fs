module LexScholar.Server.Tests.LibraryDeletionAdmissionTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open LexScholar.Infrastructure
open LexScholar.Server
open Microsoft.EntityFrameworkCore
open Xunit

let private start () =
    TestHarness.Start(configure = fun app ->
        LibraryDeletionEndpoints.mapForTests app app.Configuration["LexScholar:DataDirectory"])

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
    let now = DateTimeOffset.UtcNow
    let workspace, project, shared, alone = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = now)) |> ignore
    db.Add(ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = "Projeto", NormalizedName = "PROJETO", CreatedUtc = now)) |> ignore
    for id in [shared; alone] do
        Directory.CreateDirectory(Path.Combine(root, "originals")) |> ignore
        File.WriteAllText(Path.Combine(root, "originals", $"{id:N}.pdf"), "fixture-only")
        db.Add(LibraryDocumentRow(Id = id, OriginalFileName = "Fonte.pdf", StorageKey = $"{id:N}.pdf",
                                 Sha256 = string id, ByteLength = 12L, PageCount = 1, ImportedUtc = now)) |> ignore
    db.Add(ProjectDocumentRow(ProjectId = project, DocumentId = shared, Role = "FocusedSource", AddedUtc = now)) |> ignore
    db.Add(DocumentSummaryRow(ProjectId = project, DocumentId = shared, Content = """{"version":1}""", GeneratedAt = now)) |> ignore
    db.SaveChanges() |> ignore
    project, shared, alone

let private revision h ids =
    use response = post h "/api/library/deletions/preview" (JsonSerializer.Serialize({| documentIds = ids |}))
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result)
    json.RootElement.GetProperty("revision").GetString()

let private confirm h operation ids rev =
    post h "/api/library/deletions" (JsonSerializer.Serialize({| operationId = operation; documentIds = ids; revision = rev |}))

let private counts root operations items =
    use db = WorkspaceDb.create root
    Assert.Equal(operations, db.Set<DeletionOperationRow>() |> Seq.length)
    Assert.Equal(items, db.Set<DeletionItemRow>() |> Seq.length)

let private code expected (response: HttpResponseMessage) =
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result)
    Assert.Equal(expected, json.RootElement.GetProperty("code").GetString())

[<Fact; Trait("Task", "T010")>]
let ``admission journals every item atomically without deleting and retries survive restart`` () =
    use workspace = TemporaryWorkspace.Create()
    let operation = Guid.NewGuid()
    let mutable selected = []
    let mutable rev = ""
    do
        use h = TestHarness.Start(workspace = workspace, configure = fun app ->
            LibraryDeletionEndpoints.mapForTests app app.Configuration["LexScholar:DataDirectory"])
        let _, shared, alone = seed h.Root
        selected <- [shared; alone]
        rev <- revision h selected
        use response = confirm h operation selected rev
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode)
        use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result)
        Assert.Equal(operation, json.RootElement.GetProperty("operationId").GetGuid())
        counts h.Root 1 2
        use db = WorkspaceDb.create h.Root
        Assert.Equal(2, db.Set<LibraryDocumentRow>() |> Seq.length)
        Assert.Single(db.Set<ProjectDocumentRow>()) |> ignore
        Assert.Single(db.Set<DocumentSummaryRow>()) |> ignore
        for item in db.Set<DeletionItemRow>() do
            Assert.Equal("pending", item.Status)
            Assert.Equal("pending", item.Phase)
            Assert.Equal($"{item.DocumentId:N}.pdf", item.StorageKey)
            Assert.Equal("fixture-only", File.ReadAllText(Path.Combine(h.Root, "originals", item.StorageKey)))
    use restarted = TestHarness.Start(workspace = workspace, configure = fun app ->
        LibraryDeletionEndpoints.mapForTests app app.Configuration["LexScholar:DataDirectory"])
    // Same selection is identity, not ordering or a revision which can now be stale.
    use retry = confirm restarted operation (List.rev selected) "old"
    Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode)
    counts restarted.Root 1 2
    use conflict = confirm restarted operation [selected.Head] rev
    Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode)
    code "operation_conflict" conflict

[<Theory; Trait("Task", "T010")>]
[<InlineData("project")>]
[<InlineData("role")>]
[<InlineData("summary")>]
[<InlineData("version")>]
[<InlineData("generatedAt")>]
[<InlineData("association")>]
let ``changed impact returns fresh preview and no intent`` field =
    use h = start ()
    let project, shared, alone = seed h.Root
    let before = revision h [shared; alone]
    do
        use db = WorkspaceDb.create h.Root
        match field with
        | "project" -> db.Set<ResearchProjectRow>().Find(project).Name <- "Novo"
        | "role" -> db.Set<ProjectDocumentRow>().Find(project, shared).Role <- "ReferenceSource"
        | "summary" -> db.Set<DocumentSummaryRow>().Find(project, shared).Content <- """{"version":1,"changed":true}"""
        | "version" -> db.Set<DocumentSummaryRow>().Find(project, shared).Content <- """{"version":2}"""
        | "generatedAt" -> db.Set<DocumentSummaryRow>().Find(project, shared).GeneratedAt <- DateTimeOffset.UtcNow.AddDays(1)
        | _ -> db.Add(ProjectDocumentRow(ProjectId = project, DocumentId = alone, Role = "FocusedSource", AddedUtc = DateTimeOffset.UtcNow)) |> ignore
        db.SaveChanges() |> ignore
    use response = confirm h (Guid.NewGuid()) [shared; alone] before
    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)
    code "impact_changed" response
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result)
    Assert.Equal(revision h [shared; alone], json.RootElement.GetProperty("preview").GetProperty("revision").GetString())
    counts h.Root 0 0

[<Fact; Trait("Task", "T010")>]
let ``overlap rejects the entire new selection and concurrent tabs admit only once`` () =
    use h = start ()
    let _, shared, alone = seed h.Root
    let rev = revision h [shared]
    let submissions =
        [1; 2] |> List.map (fun _ -> Task.Run(fun () -> confirm h (Guid.NewGuid()) [shared] rev))
        |> List.toArray
    let responses = Task.WhenAll(submissions).Result
    try
        Assert.Equal<HttpStatusCode>([|HttpStatusCode.Accepted; HttpStatusCode.Conflict|], responses |> Array.map _.StatusCode |> Array.sort)
    finally
        for response in responses do response.Dispose()
    use overlap = confirm h (Guid.NewGuid()) [shared; alone] (revision h [shared; alone])
    Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode)
    code "deletion_in_progress" overlap
    counts h.Root 1 1

[<Fact; Trait("Task", "T010")>]
let ``concurrent identical retries share one durable operation`` () =
    use h = start ()
    let _, shared, alone = seed h.Root
    let operation = Guid.NewGuid()
    let rev = revision h [shared; alone]
    let submissions =
        [ [shared; alone]; [alone; shared] ]
        |> List.map (fun ids -> Task.Run(fun () -> confirm h operation ids rev))
        |> List.toArray
    for response in Task.WhenAll(submissions).Result do
        use response = response
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode)
    counts h.Root 1 2

[<Fact; Trait("Task", "T010")>]
let ``invalid unknown or mismatched selections never journal`` () =
    use h = start ()
    let _, shared, alone = seed h.Root
    let rev = revision h [shared]
    for ids, status in [[], HttpStatusCode.BadRequest; [shared; shared], HttpStatusCode.BadRequest
                        [Guid.Empty], HttpStatusCode.BadRequest; [shared; Guid.NewGuid()], HttpStatusCode.NotFound
                        [shared; alone], HttpStatusCode.Conflict] do
        use response = confirm h (Guid.NewGuid()) ids rev
        Assert.Equal(status, response.StatusCode)
        counts h.Root 0 0
    for body in ["{}"; "null"; "{"; """{"operationId":"bad","documentIds":[]}""";
                 JsonSerializer.Serialize({| operationId = Guid.Empty; documentIds = [shared]; revision = rev |})] do
        use response = post h "/api/library/deletions" body
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        counts h.Root 0 0

[<Fact; Trait("Task", "T010")>]
let ``production confirmation and retry remain fail closed`` () =
    use h = TestHarness.Start()
    let _, shared, _ = seed h.Root
    let rev = LexScholar.Application.LibraryDeletion.preview (LibraryDeletionRepository.capture h.Root) [shared] |> Result.toOption |> Option.get
    use response = confirm h (Guid.NewGuid()) [shared] rev.Revision
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
    use retry = post h $"/api/library/deletions/{Guid.NewGuid()}/retry" "{}"
    Assert.Equal(HttpStatusCode.NotFound, retry.StatusCode)
    counts h.Root 0 0

[<Fact; Trait("Task", "T010")>]
let ``pending items block associations roles removals and summary deletion but not unrelated documents`` () =
    use h = start ()
    let project, shared, alone = seed h.Root
    let other = Guid.NewGuid()
    do
        use db = WorkspaceDb.create h.Root
        let workspace = db.Set<ResearchProjectRow>().Find(project).WorkspaceId
        db.Add(ResearchProjectRow(Id = other, WorkspaceId = workspace, Name = "Outro", NormalizedName = "OUTRO",
                                 CreatedUtc = DateTimeOffset.UtcNow)) |> ignore
        db.SaveChanges() |> ignore
    use admitted = confirm h (Guid.NewGuid()) [shared] (revision h [shared])
    Assert.Equal(HttpStatusCode.Accepted, admitted.StatusCode)
    let association document =
        post h $"/api/projects/{other}/documents"
            (JsonSerializer.Serialize({| documentId = document; role = "ReferenceSource" |}))
    use blocked = association shared
    Assert.False(blocked.IsSuccessStatusCode)
    use allowed = association alone
    Assert.True(allowed.IsSuccessStatusCode)
    let role = LibraryRepository.changeRole h.Root project shared LexScholar.Domain.ReferenceSource
    Assert.True(Result.isError role)
    let removed = LibraryRepository.removeAssociation h.Root project shared
    Assert.True(Result.isError removed)
    // The ordinary summary DELETE must not mutate a durable version after intent.
    use tokenResponse = h.Client.GetAsync("/api/antiforgery").Result
    use token = JsonDocument.Parse(tokenResponse.Content.ReadAsStringAsync().Result)
    use request = new HttpRequestMessage(HttpMethod.Delete, $"/api/projects/{project}/documents/{shared}/summary")
    request.Headers.Add("Origin", h.Client.BaseAddress.GetLeftPart(UriPartial.Authority))
    request.Headers.Add("X-CSRF-TOKEN", token.RootElement.GetProperty("requestToken").GetString())
    use response = h.Client.Send(request)
    Assert.False(response.IsSuccessStatusCode)
    use db = WorkspaceDb.create h.Root
    Assert.Single(db.Set<DocumentSummaryRow>()) |> ignore
    Assert.Equal("FocusedSource", db.Set<ProjectDocumentRow>().Find(project, shared).Role)

[<Fact; Trait("Task", "T010")>]
let ``failed item persistence rolls back operation and all items`` () =
    use h = start ()
    let _, shared, alone = seed h.Root
    let rev = revision h [shared; alone]
    do
        use db = WorkspaceDb.create h.Root
        // Fail the second item, after the operation and first item were inserted.
        db.Database.ExecuteSqlRaw("""
            CREATE TRIGGER FailSecondDeletionItem BEFORE INSERT ON DeletionItems
            WHEN (SELECT COUNT(*) FROM DeletionItems) = 1
            BEGIN SELECT RAISE(ABORT, 'injected journal failure'); END;
            """) |> ignore
    use response = confirm h (Guid.NewGuid()) [shared; alone] rev
    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode)
    counts h.Root 0 0
    use db = WorkspaceDb.create h.Root
    Assert.Equal(2, db.Set<LibraryDocumentRow>() |> Seq.length)
    Assert.Single(db.Set<DocumentSummaryRow>()) |> ignore
