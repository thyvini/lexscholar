/// HTTP contract of the summary endpoint (F003, contracts/summary-api.yaml).
module LexScholar.Server.Tests.SummaryContractTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
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

[<Fact; Trait("Task", "T001")>]
let ``with a configured service the stub answers 501 and sends nothing`` () =
    let fake = new FakeChatService(Status 500)
    use harness =
        TestHarness.Start(
            settings = [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ],
            summaryHandler = fake)
    let status, _ = requestSummary harness.Client """{"confirmSend":false}"""
    Assert.Equal(HttpStatusCode.NotImplemented, status)
    Assert.Empty(fake.Requests)
