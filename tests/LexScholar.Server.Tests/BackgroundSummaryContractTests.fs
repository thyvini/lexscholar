/// HTTP contract tests for background summaries (F004), against contracts/summary-api.yaml.
module LexScholar.Server.Tests.BackgroundSummaryContractTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Xunit

let private token (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    json.RootElement.GetProperty("requestToken").GetString()

let private send (client: HttpClient) (method: HttpMethod) (path: string) (csrf: string option) =
    use message = new HttpRequestMessage(method, path)
    message.Headers.TryAddWithoutValidation("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)) |> ignore
    csrf |> Option.iter (fun value -> message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", value) |> ignore)
    client.Send(message)

let private summaryPath () = $"/api/projects/{Guid.NewGuid():D}/documents/{Guid.NewGuid():D}/summary"

[<Fact; Trait("Task", "T001")>]
let ``the summary state and status routes exist`` () =
    use harness = TestHarness.Start()
    for path in [ summaryPath (); "/api/summaries" ] do
        use response = send harness.Client HttpMethod.Get path None
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)

[<Fact; Trait("Task", "T001")>]
let ``deleting a summary exists and needs the antiforgery token`` () =
    use harness = TestHarness.Start()
    let path = summaryPath ()
    use refused = send harness.Client HttpMethod.Delete path None
    Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode)
    use answered = send harness.Client HttpMethod.Delete path (Some (token harness.Client))
    Assert.Equal(HttpStatusCode.NotImplemented, answered.StatusCode)
