module LexScholar.Server.Tests.LibraryControlsSecurityTests

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open LexScholar.Application
open LexScholar.Infrastructure
open LexScholar.Server
open Xunit

type private Lines() =
    let lines = System.Collections.Concurrent.ConcurrentQueue<string>()
    member _.All = String.concat "\n" lines
    member _.Clear() = lines.Clear()
    interface ILoggerProvider with
        member _.CreateLogger(_) =
            { new ILogger with
                member _.BeginScope(_) = null
                member _.IsEnabled(_) = true
                member _.Log(_, _, state, error, formatter) =
                    lines.Enqueue(formatter.Invoke(state, error) + (if isNull error then "" else string error)) }
        member _.Dispose() = ()

type private NoOutgoing() =
    inherit HttpMessageHandler()
    let calls = System.Collections.Concurrent.ConcurrentQueue<string>()
    member _.Calls = calls.ToArray()
    override _.SendAsync(request, _) =
        calls.Enqueue(string request.RequestUri)
        Task.FromException<HttpResponseMessage>(InvalidOperationException("Unexpected outgoing request"))

let private token (host: TestHarness) =
    use response = host.Client.GetAsync("/api/antiforgery").Result
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result)
    json.RootElement.GetProperty("requestToken").GetString()

let private post (host: TestHarness) (path: string) body =
    use request = new HttpRequestMessage(HttpMethod.Post, path)
    request.Headers.Add("Origin", host.Client.BaseAddress.GetLeftPart(UriPartial.Authority))
    request.Headers.Add("X-CSRF-TOKEN", token host)
    request.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    host.Client.Send(request)

let private seed root =
    let document, project, workspace = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    let now = DateTimeOffset.UtcNow
    let bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf"))
    DocumentStore.ensureDirectories root
    File.WriteAllBytes(Path.Combine(DocumentStore.originalsDirectory root, $"{document:N}"), bytes)
    use db = WorkspaceDb.create root
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = now)) |> ignore
    db.SaveChanges() |> ignore
    db.Add(ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = "PRIVATE-PROJECT-T018",
                             NormalizedName = "PRIVATE-PROJECT-T018", CreatedUtc = now)) |> ignore
    db.Add(LibraryDocumentRow(Id = document, OriginalFileName = "fixture.pdf", StorageKey = $"{document:N}",
        Sha256 = "fixture-hash", ByteLength = int64 bytes.Length, PageCount = 1, ImportedUtc = now)) |> ignore
    db.SaveChanges() |> ignore
    db.Add(ProjectDocumentRow(ProjectId = project, DocumentId = document, Role = "FocusedSource", AddedUtc = now)) |> ignore
    db.SaveChanges() |> ignore
    SummaryRepository.save root { Project = project; Document = document }
        { DocumentId = document; Points = [{ Text = "PRIVATE-SUMMARY-T018"; Pages = [1] }]
          PagesCovered = [1]; UnreadablePages = []; OmittedPoints = 0; GeneratedAt = now
          ServiceHost = "local"; Model = "fixture" }
    document, bytes

[<Theory; Trait("Task", "T018")>]
[<InlineData("/api/library/export", 409)>]
[<InlineData("/api/library/deletions/preview", 400)>]
[<InlineData("/api/library/deletions", 400)>]
[<InlineData("/api/library/deletions/00000000-0000-0000-0000-000000000001/retry", 404)>]
[<InlineData("/api/library/deletions/00000000-0000-0000-0000-000000000001/acknowledge", 404)>]
let ``every public control POST rejects forged Host Origin and CSRF before reaching its handler`` (path: string) allowedStatus =
    use host = TestHarness.Start()
    let csrf = token host
    for attack, expectedCode in
        [ "host", "invalid_host"; "origin", "invalid_origin"; "missing", "invalid_antiforgery_token"
          "invalid", "invalid_antiforgery_token"; "cookie", "invalid_antiforgery_token" ] do
        use anonymous = new HttpClient(BaseAddress = host.Client.BaseAddress)
        use request = new HttpRequestMessage(HttpMethod.Post, path)
        let origin =
            if attack = "origin" then "https://foreign.invalid"
            elif attack = "host" then "http://foreign.invalid"
            else host.Client.BaseAddress.GetLeftPart(UriPartial.Authority)
        request.Headers.Add("Origin", origin)
        if attack = "host" then request.Headers.Host <- "foreign.invalid"
        if attack <> "missing" then request.Headers.Add("X-CSRF-TOKEN", if attack = "invalid" then "invalid" else csrf)
        request.Content <- new StringContent("{}", Encoding.UTF8, "application/json")
        use response = (if attack = "cookie" then anonymous else host.Client).Send(request)
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)
        use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result)
        Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString())
    use allowed = post host path "{}"
    Assert.Equal(allowedStatus, int allowed.StatusCode)
    use db = WorkspaceDb.create host.Root
    Assert.Empty(db.Set<DeletionOperationRow>())

[<Fact; Trait("Task", "T018")>]
let ``export and deletion keep secrets local and do not log document contents or private paths`` () = task {
    use outgoing = new NoOutgoing()
    use lines = new Lines()
    use host = TestHarness.Start(summaryHandler = outgoing,
        settings = [ "LexScholar:Summary:Endpoint", "https://service.invalid/v1"
                     "LexScholar:Summary:ApiKey", "PRIVATE-APIKEY-T018"; "LexScholar:Summary:Model", "fixture" ],
        configure = fun app -> app.Services.GetRequiredService<ILoggerFactory>().AddProvider(lines))
    let document, bytes = seed host.Root
    let secrets = ["PRIVATE-CREDENTIAL-T018"; "PRIVATE-SETTINGS-T018"; "PRIVATE-APIKEY-T018"; host.Root]
    File.WriteAllText(Path.Combine(host.Root, "chatgpt-signin.json"), secrets.[0])
    File.WriteAllText(Path.Combine(host.Root, "appsettings.json"), secrets.[1])
    // TestHarness makes its isolated data root the ASP.NET content root too;
    // the standard startup banner prints that content root. Observe controls,
    // not this harness-specific startup banner.
    lines.Clear()
    use exported = post host "/api/library/export" "{}"
    Assert.Equal(HttpStatusCode.OK, exported.StatusCode)
    use! stream = exported.Content.ReadAsStreamAsync()
    use zip = new ZipArchive(stream, ZipArchiveMode.Read)
    for entry in zip.Entries do
        use reader = new StreamReader(entry.Open())
        let content = reader.ReadToEnd()
        for secret in secrets do Assert.DoesNotContain(secret, entry.FullName + content)
    use original = zip.GetEntry($"pdf/{document:D}.pdf").Open()
    use actual = new MemoryStream()
    original.CopyTo(actual)
    Assert.Equal<byte>(bytes, actual.ToArray())
    let selection = JsonSerializer.Serialize({| documentIds = [document] |})
    use preview = post host "/api/library/deletions/preview" selection
    Assert.Equal(HttpStatusCode.OK, preview.StatusCode)
    let previewText = preview.Content.ReadAsStringAsync().Result
    Assert.DoesNotContain("PRIVATE-SUMMARY-T018", previewText)
    Assert.DoesNotContain(host.Root, previewText)
    use impact = JsonDocument.Parse(previewText)
    let operation = Guid.NewGuid()
    let confirmation =
        JsonSerializer.Serialize({| operationId = operation; documentIds = [document]
                                    revision = impact.RootElement.GetProperty("revision").GetString() |})
    use admitted = post host "/api/library/deletions" confirmation
    Assert.Equal(HttpStatusCode.Accepted, admitted.StatusCode)
    let runner = (host.App :> IApplicationBuilder).Properties["LexScholar.LibraryDeletions"] :?> LibraryDeletion.Operations
    do! (runner.Settled operation).WaitAsync(TimeSpan.FromSeconds 10.)
    use retry = post host $"/api/library/deletions/{operation}/retry" "{}"
    Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode)
    do! (runner.Settled operation).WaitAsync(TimeSpan.FromSeconds 10.)
    let! outcome = host.Client.GetStringAsync($"/api/library/deletions/{operation}")
    use result = JsonDocument.Parse(outcome)
    Assert.Equal("complete", result.RootElement.GetProperty("status").GetString())
    use acknowledged = post host $"/api/library/deletions/{operation}/acknowledge" "{}"
    Assert.Equal(HttpStatusCode.NoContent, acknowledged.StatusCode)
    for secret in secrets @ ["PRIVATE-SUMMARY-T018"; "PRIVATE-PROJECT-T018"] do
        Assert.DoesNotContain(secret, outcome)
        Assert.DoesNotContain(secret, lines.All)
    Assert.False(File.Exists(Path.Combine(DocumentStore.originalsDirectory host.Root, $"{document:N}")))
    Assert.Empty(outgoing.Calls)
}

[<Fact; Trait("Task", "T018")>]
let ``storage failures and malformed inputs expose no sensitive error prose or paths in responses and logs`` () = task {
    use lines = new Lines()
    use outgoing = new NoOutgoing()
    let sensitive = "PRIVATE-ERROR-CONTENT-T018"
    let mutable runner: LibraryDeletion.Operations option = None
    use host = TestHarness.Start(summaryHandler = outgoing, configure = fun app ->
        app.Services.GetRequiredService<ILoggerFactory>().AddProvider(lines)
        let coordination = app.Services.GetRequiredService<LibraryCoordination>()
        LibraryControlEndpoints.mapWithBuildForTests app coordination
            (LibraryExport.buildWith
                { OpenOriginal = fun path -> raise (IOException(sensitive + app.Configuration["LexScholar:DataDirectory"] + path))
                  CreateArchive = fun _ -> new MemoryStream() :> Stream }
                app.Configuration["LexScholar:DataDirectory"])
        runner <- Some (LibraryDeletionEndpoints.mapWithProcessingForTests app app.Configuration["LexScholar:DataDirectory"]
            (fun _ _ -> Task.FromException<Result<unit, LibraryDeletion.MetadataError>>(IOException(sensitive + app.Configuration["LexScholar:DataDirectory"])))))
    let document, _ = seed host.Root
    lines.Clear()
    let check (response: HttpResponseMessage) =
        let body = response.Content.ReadAsStringAsync().Result
        for secret in [sensitive; host.Root; "PRIVATE-SUMMARY-T018"; "PRIVATE-PROJECT-T018"] do
            Assert.DoesNotContain(secret, body)
        body
    use failedExport = post host "/api/library/export" "{}"
    Assert.Equal(HttpStatusCode.Conflict, failedExport.StatusCode)
    check failedExport |> ignore
    use malformed = post host "/api/library/deletions" ("{\"private\":\"" + sensitive + "\"")
    Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode)
    check malformed |> ignore
    let preview = LibraryDeletion.preview (LibraryDeletionRepository.capture host.Root) [document] |> Result.toOption |> Option.get
    let operation = Guid.NewGuid()
    let confirmation = JsonSerializer.Serialize({| operationId = operation; documentIds = [document]; revision = preview.Revision |})
    use admitted = post host "/api/library/deletions" confirmation
    Assert.Equal(HttpStatusCode.Accepted, admitted.StatusCode)
    do! (runner.Value.Settled operation).WaitAsync(TimeSpan.FromSeconds 10.)
    use outcome = host.Client.GetAsync($"/api/library/deletions/{operation}").Result
    use json = JsonDocument.Parse(check outcome)
    Assert.Equal("partial", json.RootElement.GetProperty("status").GetString())
    Assert.Equal("storage_error", json.RootElement.GetProperty("items").[0].GetProperty("reason").GetString())
    for secret in [sensitive; host.Root; "PRIVATE-SUMMARY-T018"; "PRIVATE-PROJECT-T018"] do
        Assert.DoesNotContain(secret, lines.All)
    Assert.Empty(outgoing.Calls)
}
