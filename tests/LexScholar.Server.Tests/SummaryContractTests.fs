/// HTTP contract of the summary endpoint (F003, contracts/summary-api.yaml).
module LexScholar.Server.Tests.SummaryContractTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Net.Http.Headers
open System.IO
open System.Text.Json
open LexScholar.Infrastructure
open Xunit

let private token (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    json.RootElement.GetProperty("requestToken").GetString()

let private requestSummary (client: HttpClient) (body: string) =
    let csrf = token client
    use message = new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/summary")
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf) |> ignore
    message.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    let response = client.Send(message)
    let text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    response.StatusCode, text

[<Fact; Trait("Task", "T001")>]
let ``without a configured service the endpoint answers 503 summary_service_not_configured`` () =
    use harness = TestHarness.Start()
    let status, text = requestSummary harness.Client """{"confirmSend":false}"""
    Assert.Equal(HttpStatusCode.ServiceUnavailable, status)
    use json = JsonDocument.Parse(text)
    let problem = json.RootElement
    Assert.Equal("summary_service_not_configured", problem.GetProperty("code").GetString())
    Assert.Contains("LexScholar:Summary:Endpoint", problem.GetProperty("detail").GetString())

let private settings = [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ]

let private fixture name = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summary", name))

let private send (client: HttpClient) (method: HttpMethod) (path: string) (content: HttpContent option) =
    use message = new HttpRequestMessage(method, path)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token client) |> ignore
    content |> Option.iter (fun body -> message.Content <- body)
    let response = client.Send(message)
    response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

let private seedProject directory =
    use db = WorkspaceDb.create directory
    let time = DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero)
    let workspace = Guid.NewGuid()
    let project = Guid.NewGuid()
    db.Add(WorkspaceRow(Id = workspace, CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    db.Add(ResearchProjectRow(Id = project, WorkspaceId = workspace, Name = "P", NormalizedName = "P", CreatedUtc = time)) |> ignore
    db.SaveChanges() |> ignore
    project

/// Imports a summary fixture through HTTP; returns its document id.
let private import (client: HttpClient) (name: string) =
    let part = new ByteArrayContent(fixture name)
    part.Headers.ContentType <- MediaTypeHeaderValue("application/pdf")
    let form = new MultipartFormDataContent()
    form.Add(part, "file", name)
    let status, text = send client HttpMethod.Post "/api/documents" (Some (form :> HttpContent))
    Assert.True(status = HttpStatusCode.Created || status = HttpStatusCode.OK, text)
    use json = JsonDocument.Parse(text)
    json.RootElement.GetProperty("id").GetGuid()

let private associate (client: HttpClient) (project: Guid) (document: Guid) =
    let body = JsonSerializer.Serialize({| documentId = string document; role = "FocusedSource" |})
    let status, text = send client HttpMethod.Post $"/api/projects/{project}/documents" (Some (new StringContent(body, Encoding.UTF8, "application/json")))
    Assert.Equal(HttpStatusCode.Created, status)

let private summarize (client: HttpClient) (project: Guid) (document: Guid) =
    send client HttpMethod.Post $"/api/projects/{project}/documents/{document}/summary"
        (Some (new StringContent("""{"confirmSend":false}""", Encoding.UTF8, "application/json")))

let private expectProblem (fake: FakeChatService) (root: string) status code (actual: HttpStatusCode, text: string) =
    Assert.Equal(status, actual)
    use json = JsonDocument.Parse(text)
    Assert.Equal(code, json.RootElement.GetProperty("code").GetString())
    Assert.DoesNotContain(root, text)
    Assert.Empty(fake.Requests)

/// Runs `action` with a started host, an associated fixture document and the fake service.
let private withDocument fixtureName (action: TestHarness -> FakeChatService -> Guid -> Guid -> unit) =
    let fake = new FakeChatService(Status 500)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = import harness.Client fixtureName
    associate harness.Client project document
    action harness fake project document

[<Fact; Trait("Task", "T002")>]
let ``a readable document passes every local check and reaches the stub`` () =
    withDocument "article-en.pdf" (fun harness fake project document ->
        let status, _ = summarize harness.Client project document
        Assert.Equal(HttpStatusCode.NotImplemented, status)
        Assert.Empty(fake.Requests))

[<Fact; Trait("Task", "T002")>]
let ``a document that is not associated with the project is 404 not_found`` () =
    let fake = new FakeChatService(Status 500)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    let document = import harness.Client "article-en.pdf"
    expectProblem fake harness.Root HttpStatusCode.NotFound "not_found" (summarize harness.Client project document)
    expectProblem fake harness.Root HttpStatusCode.NotFound "not_found" (summarize harness.Client (Guid.NewGuid()) document)

[<Fact; Trait("Task", "T002")>]
let ``an unknown document is 404 document_not_found`` () =
    let fake = new FakeChatService(Status 500)
    use harness = TestHarness.Start(settings = settings, summaryHandler = fake)
    let project = seedProject harness.Root
    expectProblem fake harness.Root HttpStatusCode.NotFound "document_not_found" (summarize harness.Client project (Guid.NewGuid()))

[<Fact; Trait("Task", "T002")>]
let ``a missing original is 503 document_unavailable`` () =
    withDocument "article-en.pdf" (fun harness fake project document ->
        for file in Directory.GetFiles(DocumentStore.originalsDirectory harness.Root) do
            File.SetUnixFileMode(file, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            File.Delete(file)
        expectProblem fake harness.Root HttpStatusCode.ServiceUnavailable "document_unavailable" (summarize harness.Client project document))

[<Theory; Trait("Task", "T002")>]
[<InlineData("too-long.pdf", "too_many_pages")>]
[<InlineData("scanned.pdf", "no_text")>]
[<InlineData("little-text.pdf", "not_enough_text")>]
let ``documents that cannot be summarized are 422 and nothing is sent`` (fixtureName: string, code: string) =
    withDocument fixtureName (fun harness fake project document ->
        expectProblem fake harness.Root (enum<HttpStatusCode> 422) code (summarize harness.Client project document))
