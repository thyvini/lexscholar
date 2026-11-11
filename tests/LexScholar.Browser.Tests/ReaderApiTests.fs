module LexScholar.Browser.Tests.ReaderApiTests

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Xunit
open LexScholar.Client
open LexScholar.Domain

type private Handler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request, _) = Task.FromResult(respond request)

[<Theory; InlineData(503, "document_unavailable"); InlineData(404, "document_not_found");
  Trait("Task", "T039")>]
let ``availability checks request one byte and distinguish unavailable and unknown documents`` (status: int, code: string) = task {
    use handler = new Handler(fun request ->
        Assert.Equal(HttpMethod.Get, request.Method)
        Assert.Equal("http://localhost/api/documents/paper/content", request.RequestUri.AbsoluteUri)
        Assert.Equal("bytes=0-0", request.Headers.Range.ToString())
        let response = new HttpResponseMessage(enum<HttpStatusCode> status)
        response.Content <- new StringContent($"{{\"code\":\"{code}\"}}")
        response)
    use http = new HttpClient(handler, BaseAddress = Uri("http://localhost/"))
    let! result = Api.checkContent http "/api/documents/paper/content" |> Async.StartAsTask
    Assert.True((result = Error (if status = 503 then Reader.Unavailable else Reader.DocumentNotFound))) }

[<Fact; Trait("Task", "T039")>]
let ``network failures do not claim the stored original is unavailable`` () = task {
    use handler = new Handler(fun _ -> raise (HttpRequestException("private internal detail")))
    use http = new HttpClient(handler, BaseAddress = Uri("http://localhost/"))
    let! result = Api.checkContent http "/api/documents/paper/content" |> Async.StartAsTask
    Assert.True((result = Error Reader.RequestFailed)) }

[<Fact; Trait("Task", "T039")>]
let ``role changes send project scoped PATCH with antiforgery and the exact role`` () = task {
    let project = Guid.NewGuid()
    let document = Guid.NewGuid()
    let mutable calls = 0
    use handler = new Handler(fun request ->
        calls <- calls + 1
        Assert.Equal("localhost", request.RequestUri.Host)
        if calls = 1 then
            Assert.Equal("/api/antiforgery", request.RequestUri.AbsolutePath)
            Assert.Equal(HttpMethod.Get, request.Method)
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent("{\"requestToken\":\"test-token\"}")
            response
        else
            Assert.Equal(HttpMethod.Patch, request.Method)
            Assert.Equal($"/api/projects/{project:D}/documents/{document:D}", request.RequestUri.AbsolutePath)
            Assert.Equal("test-token", request.Headers.GetValues("X-CSRF-TOKEN") |> Seq.exactlyOne)
            Assert.Equal("{\"role\":\"ReferenceSource\"}", request.Content.ReadAsStringAsync().GetAwaiter().GetResult())
            new HttpResponseMessage(HttpStatusCode.OK))
    use http = new HttpClient(handler, BaseAddress = Uri("http://localhost/"))
    let! result = Api.changeRole http project document ReferenceSource |> Async.StartAsTask
    Assert.True((result = Ok ()))
    Assert.Equal(2, calls) }
