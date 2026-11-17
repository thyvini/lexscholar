module LexScholar.Server.Tests.FakeOpenAiHoldTests

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Xunit
open LexScholar.Server.Tests

[<Fact; Trait("Task", "T001")>]
let ``held response waits for release and returns its text`` () : Task = task {
    use fake = new FakeOpenAi(ResponsesScript.Hold "hello")
    use client = new HttpClient(fake)
    let pending = client.PostAsync("https://api.openai.com/v1/responses", new StringContent("{}"))
    do! fake.Waiting.WaitAsync(TimeSpan.FromSeconds 10.)
    Assert.False(pending.IsCompleted)
    fake.Release()
    use! response = pending
    let! body = response.Content.ReadAsStringAsync()
    Assert.Contains("response.completed", body)
    Assert.Contains("\"delta\":\"he\"", body)
    Assert.Contains("\"delta\":\"llo\"", body)
}

[<Fact; Trait("Task", "T001")>]
let ``cancelling a held response signals cancellation`` () : Task = task {
    use fake = new FakeOpenAi(ResponsesScript.Hold "hello")
    use client = new HttpClient(fake)
    use cancellation = new CancellationTokenSource()
    let pending = client.PostAsync("https://api.openai.com/v1/responses", new StringContent("{}"), cancellation.Token)
    do! fake.Waiting.WaitAsync(TimeSpan.FromSeconds 10.)
    cancellation.Cancel()
    do! fake.Cancelled.WaitAsync(TimeSpan.FromSeconds 10.)
    let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(fun () -> pending :> Task)
    ()
}
