module LexScholar.Application.Tests.SummarizeTests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Domain
open Xunit

let private page number (text: string) : PageText = { Number = number; Text = text }

let private pages : ReadablePages =
    { Readable = [ page 1 "Alpha text of page one."; page 3 "Gamma text of page three." ]
      Unreadable = [ 2 ] }

let private capabilities (answer: Result<string, Summarize.ServiceError>) (sent: List<Summarize.ChatMessage list>) : Summarize.Capabilities =
    { ExtractPages = fun _ -> failwith "not used"
      Complete = fun messages _ -> sent.Add messages; Task.FromResult answer
      Report = fun _ -> Task.FromResult() }

let private run capabilities =
    (Summarize.summarize capabilities pages CancellationToken.None).GetAwaiter().GetResult()

[<Fact; Trait("Task", "T003")>]
let ``the request has instructions and the readable pages under their own page markers`` () =
    let sent = List()
    run (capabilities (Ok """{"points":[]}""") sent) |> ignore
    let messages = Assert.Single(sent)
    Assert.Equal<string list>([ "system"; "user" ], messages |> List.map (fun m -> m.Role))
    let system = messages.[0].Content
    Assert.Contains("\"points\"", system)
    Assert.Contains("pages", system)
    let user = messages.[1].Content
    Assert.Contains("[Page 1]", user)
    Assert.Contains("Alpha text of page one.", user)
    Assert.Contains("[Page 3]", user)
    Assert.DoesNotContain("[Page 2]", user)

[<Fact; Trait("Task", "T003")>]
let ``points citing only sent pages are returned and others omitted`` () =
    let sent = List()
    let answer = """{"points":[{"text":"A","pages":[1,3]},{"text":"B","pages":[2]},{"text":"C","pages":[9]}]}"""
    match run (capabilities (Ok answer) sent) with
    | Ok result ->
        Assert.Equal<SummaryPoint list>([ { Text = "A"; Pages = [ 1; 3 ] } ], result.Points)
        Assert.Equal(2, result.Omitted)
    | Error failure -> failwith $"expected Ok, got {failure}"

[<Fact; Trait("Task", "T003")>]
let ``no valid point is NotEnoughText with the omitted count`` () =
    let sent = List()
    let answer = """{"points":[{"text":"B","pages":[2]},{"text":"C","pages":[9]}]}"""
    Assert.Equal(Error (Summarize.SummaryFailure.NotEnoughText 2), run (capabilities (Ok answer) sent))
    Assert.Equal(Error (Summarize.SummaryFailure.NotEnoughText 0), run (capabilities (Ok """{"points":[]}""") sent))

[<Fact; Trait("Task", "T003")>]
let ``a service error or unparseable output is ServiceFailed`` () =
    let sent = List()
    Assert.Equal(Error (Summarize.SummaryFailure.ServiceFailed Summarize.ServiceError.TimedOut),
                 run (capabilities (Error Summarize.ServiceError.TimedOut) sent))
    Assert.Equal(Error (Summarize.SummaryFailure.ServiceFailed Summarize.ServiceError.UnusableOutput),
                 run (capabilities (Ok "I cannot help") sent))
