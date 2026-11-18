module LexScholar.Browser.Tests.RoleApiTests

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
