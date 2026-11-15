/// F004 T011: the rules before anything is sent are unchanged by background summaries (FR-006).
/// Every refusal answers as in F003/F005, creates no job and no stored summary, and the fakes see no request at all.
module LexScholar.Server.Tests.PreSendRegressionTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Xunit
open LexScholar.Server.Tests.ChatGptContractTests

let private elsewhere = [ "LexScholar:Summary:Endpoint", "http://summary.example.test:8080/v1"; "LexScholar:Summary:Model", "gpt" ]
let private local = [ "LexScholar:Summary:Endpoint", "http://127.0.0.1:9/v1"; "LexScholar:Summary:Model", "m" ]

let private get (harness: TestHarness) (path: string) =
    use response = harness.Client.GetAsync(path).GetAwaiter().GetResult()
    response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

/// No job and no stored summary for the pair, and no entry in the status list.
let private expectNoJob (harness: TestHarness) (path: string) =
    Assert.Equal(HttpStatusCode.NotFound, fst (get harness path))
    let status, text = get harness "/api/summaries"
    Assert.Equal(HttpStatusCode.OK, status)
    use json = JsonDocument.Parse text
    Assert.Equal(0, json.RootElement.GetArrayLength())

let private expectCode (status: HttpStatusCode) (code: string) (actual: HttpStatusCode, text: string) =
    Assert.Equal(status, actual)
    Assert.Equal(code, problemCode text)

let private unprocessable = enum<HttpStatusCode> 422

[<Theory; Trait("Task", "T011")>]
[<InlineData("too-long.pdf", "too_many_pages")>]
[<InlineData("scanned.pdf", "no_text")>]
[<InlineData("little-text.pdf", "not_enough_text")>]
let ``a document that cannot be summarized is refused as in F003, creates no job and sends nothing`` (fixture: string, code: string) =
    // Service elsewhere, no confirmation: the local check still comes first.
    let chat = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = elsewhere, summaryHandler = chat)
    let path = documentPathFor harness fixture
    expectCode unprocessable code (sendJson harness path """{"confirmSend":false}""")
    expectNoJob harness path
    Assert.Empty(chat.Requests)

[<Theory; Trait("Task", "T011")>]
[<InlineData("too-long.pdf", "too_many_pages")>]
[<InlineData("scanned.pdf", "no_text")>]
[<InlineData("little-text.pdf", "not_enough_text")>]
let ``the same refusals through the plan create no job and reach the OpenAI API with nothing`` (fixture: string, code: string) =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPathFor harness fixture
    signInThrough harness fake "" |> ignore
    expectCode unprocessable code (sendJson harness path """{"confirmSend":true}""")
    expectNoJob harness path
    Assert.Empty(openAiRequests fake)

[<Theory; Trait("Task", "T011")>]
[<InlineData("""{"confirmSend":false}""")>]
[<InlineData("""{}""")>]
[<InlineData("""{"confirmSend":true}""")>]
[<InlineData("""{"confirmSend":true,"confirmedHost":"other.example.test:8080","confirmedModel":"gpt"}""")>]
[<InlineData("""{"confirmSend":true,"confirmedHost":"summary.example.test:8080","confirmedModel":"other"}""")>]
let ``a service elsewhere without confirming this host and model is 409, creates no job and receives nothing`` (body: string) =
    let chat = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = elsewhere, summaryHandler = chat)
    let path = documentPath harness
    expectCode HttpStatusCode.Conflict "confirmation_required" (sendJson harness path body)
    expectNoJob harness path
    Assert.Empty(chat.Requests)

[<Fact; Trait("Task", "T011")>]
let ``the plan without a confirmation is 409, creates no job and the OpenAI API receives nothing`` () =
    let fake = new FakeOpenAi(ResponsesScript.Completed answer)
    use harness = TestHarness.Start(summaryHandler = fake)
    let path = documentPath harness
    signInThrough harness fake "" |> ignore
    expectCode HttpStatusCode.Conflict "confirmation_required" (sendJson harness path """{"confirmSend":false}""")
    expectNoJob harness path
    Assert.Empty(openAiRequests fake)

[<Fact; Trait("Task", "T011")>]
let ``a local service needs no confirmation: the same body that is refused elsewhere starts the job`` () =
    let chat = new FakeChatService(Reply answer)
    use harness = TestHarness.Start(settings = local, summaryHandler = chat)
    let path = documentPath harness
    let status, text = sendSummary harness path """{"confirmSend":false}"""
    Assert.Equal(HttpStatusCode.OK, status)
    Assert.Equal("summary", (Assert.Single(events text)).GetProperty("type").GetString())
    Assert.Single(chat.Requests) |> ignore
