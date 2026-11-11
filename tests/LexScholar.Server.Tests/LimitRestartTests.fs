/// Import limits across restarts (T041; FR-003, FR-011, SC-008): lowering
/// both limits below an imported PDF's size and page count keeps that PDF
/// readable and unchanged, while new imports over either limit are rejected,
/// including re-imports of bytes already in the library.
module LexScholar.Server.Tests.LimitRestartTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text.Json
open LexScholar.Infrastructure
open Xunit

let private fixture name = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))

let private upload (client: HttpClient) name =
    use tokenResponse = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    use tokenJson = JsonDocument.Parse(tokenResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    let content = new MultipartFormDataContent()
    content.Add(new ByteArrayContent(fixture name), "file", name)
    use message = new HttpRequestMessage(HttpMethod.Post, "/api/documents", Content = content)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString()) |> ignore
    client.Send(message)

let private body (response: HttpResponseMessage) = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

let private expectProblem (status: HttpStatusCode) (code: string) (response: HttpResponseMessage) =
    let text = body response
    Assert.True((status = response.StatusCode), $"Expected {status}, got {response.StatusCode}: {text}")
    use problem = JsonDocument.Parse(text)
    Assert.Equal(code, problem.RootElement.GetProperty("code").GetString())

/// Every library row, as stored.
let private rows directory =
    use db = WorkspaceDb.create directory
    [ for row in db.Set<LibraryDocumentRow>() -> row.Id, row.StorageKey, row.Sha256, row.ByteLength, row.PageCount, row.ImportedUtc ]

let private originals directory =
    Directory.GetFiles(Path.Combine(directory, "originals")) |> Array.map Path.GetFileName |> Array.sort |> Array.toList

[<Fact; Trait("Task", "T041")>]
let ``lowered limits keep an imported PDF readable and reject new over-limit imports`` () =
    use workspace = TemporaryWorkspace.Create()
    let original = fixture "valid.pdf"
    let id, listing =
        use harness = TestHarness.Start(workspace = workspace)
        use response = upload harness.Client "valid.pdf"
        Assert.Equal(HttpStatusCode.Created, response.StatusCode)
        use document = JsonDocument.Parse(body response)
        Assert.Equal(753L, document.RootElement.GetProperty("byteLength").GetInt64())
        Assert.Equal(2, document.RootElement.GetProperty("pageCount").GetInt32())
        document.RootElement.GetProperty("id").GetGuid(), harness.Client.GetStringAsync("/api/documents").GetAwaiter().GetResult()
    let storedRows = rows workspace.Root
    let storedFiles = originals workspace.Root
    // Both positive limits below the imported PDF's 753 bytes and 2 pages.
    do
        use harness = TestHarness.Start([ "LexScholar:ImportLimits:MaxBytes", "700"; "LexScholar:ImportLimits:MaxPages", "1" ], workspace = workspace)
        use content = harness.Client.GetAsync($"/api/documents/{id}/content").GetAwaiter().GetResult()
        Assert.Equal(HttpStatusCode.OK, content.StatusCode)
        Assert.Equal<byte array>(original, content.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
        Assert.Equal(listing, harness.Client.GetStringAsync("/api/documents").GetAwaiter().GetResult())
        use tooLarge = upload harness.Client "distinct.pdf"
        expectProblem HttpStatusCode.RequestEntityTooLarge "upload_too_large" tooLarge
        Assert.Equal<byte array>(original, harness.Client.GetByteArrayAsync($"/api/documents/{id}/content").GetAwaiter().GetResult())
    Assert.True((storedRows = rows workspace.Root), "Library rows changed.")
    Assert.Equal<string list>(storedFiles, originals workspace.Root)
    // Default byte limit, page limit still 1: limits are checked on every
    // import, including bytes already in the library.
    do
        use harness = TestHarness.Start([ "LexScholar:ImportLimits:MaxPages", "1" ], workspace = workspace)
        use reimport = upload harness.Client "valid.pdf"
        expectProblem HttpStatusCode.UnprocessableEntity "page_limit_exceeded" reimport
        use content = harness.Client.GetAsync($"/api/documents/{id}/content").GetAwaiter().GetResult()
        Assert.Equal(HttpStatusCode.OK, content.StatusCode)
        Assert.Equal<byte array>(original, content.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
    Assert.True((storedRows = rows workspace.Root), "Library rows changed.")
    Assert.Equal<string list>(storedFiles, originals workspace.Root)
