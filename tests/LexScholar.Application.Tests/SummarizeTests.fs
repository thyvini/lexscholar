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
    (Summarize.summarize capabilities 12000 pages CancellationToken.None).GetAwaiter().GetResult()

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

// ---- T006: long documents ----

let private text n = String('x', n)

/// 6 readable pages of 150 characters; with a 250 limit every chunk holds one page.
let private longPages : ReadablePages =
    { Readable = [ for n in 1..6 -> page n (text 150) ]
      Unreadable = [ 7 ] }

let private isCombine (messages: Summarize.ChatMessage list) = messages.[1].Content.StartsWith "- (pages "

/// Pages named by the "[Page n]" markers, or by the "(pages a, b)" prefixes of combined points.
let private citedIn (messages: Summarize.ChatMessage list) =
    let content = messages.[1].Content
    let markers = Text.RegularExpressions.Regex.Matches(content, @"\[Page (\d+)\]") |> Seq.map (fun m -> int m.Groups.[1].Value)
    let lists =
        Text.RegularExpressions.Regex.Matches(content, @"^- \(pages ([\d, ]+)\)", Text.RegularExpressions.RegexOptions.Multiline)
        |> Seq.collect (fun m -> m.Groups.[1].Value.Split(',') |> Seq.map (fun p -> int (p.Trim())))
    Seq.append markers lists |> Seq.distinct |> Seq.toList

let private pointsJson (points: (string * int list) list) =
    let items = points |> List.map (fun (t, pages) -> $"""{{"text":"{t}","pages":[{String.Join(",", pages)}]}}""")
    $"""{{"points":[{String.Join(",", items)}]}}"""

let private runLong (reply: Summarize.ChatMessage list -> string) (sent: List<Summarize.ChatMessage list>) (reports: List<Summarize.Progress>) =
    let capabilities : Summarize.Capabilities =
        { ExtractPages = fun _ -> failwith "not used"
          Complete = fun messages _ -> sent.Add messages; Task.FromResult (Ok (reply messages))
          Report = fun progress -> reports.Add progress; Task.FromResult() }
    (Summarize.summarize capabilities 250 longPages CancellationToken.None).GetAwaiter().GetResult()

[<Fact; Trait("Task", "T006")>]
let ``a long document is mapped chunk by chunk, then combined, with progress`` () =
    let sent, reports = List(), List()
    // Map: each chunk gives one 60-character point; the combine call merges what it is given into one.
    let reply messages =
        if isCombine messages then pointsJson [ "merged", citedIn messages ]
        else pointsJson [ text 60, citedIn messages ]
    match runLong reply sent reports with
    | Ok result ->
        Assert.Equal<SummaryPoint list>([ { Text = "merged"; Pages = [ 1; 2; 3; 4; 5; 6 ] } ], result.Points)
    | Error failure -> failwith $"expected Ok, got {failure}"
    Assert.Equal(6, sent |> Seq.filter (isCombine >> not) |> Seq.length)
    Assert.True(sent |> Seq.exists isCombine)
    // Unreadable page 7 counts from the start; each chunk then adds its page.
    let counted = reports |> Seq.filter (fun r -> r <> Summarize.Progress.Combining) |> Seq.toList
    Assert.Equal<Summarize.Progress list>([ for processed in 1..7 -> Summarize.Progress.PagesProcessed (processed, 7) ], counted)
    Assert.Equal(Summarize.Progress.Combining, reports.[reports.Count - 1])

[<Fact; Trait("Task", "T006")>]
let ``a combine round that does not shrink the points is service_failed and calls nothing more`` () =
    let sent, reports = List(), List()
    // Map: one 100-character point per chunk; combine echoes its points, so no round ever shrinks.
    let reply messages =
        if isCombine messages then
            let points =
                messages.[1].Content.Split('\n')
                |> Array.toList
                |> List.map (fun line ->
                    let m = Text.RegularExpressions.Regex.Match(line, @"^- \(pages ([\d, ]+)\) (.*)$")
                    m.Groups.[2].Value, m.Groups.[1].Value.Split(',') |> Array.map (fun p -> int (p.Trim())) |> Array.toList)
            pointsJson points
        else pointsJson [ text 100, citedIn messages ]
    match runLong reply sent reports with
    | Error (Summarize.SummaryFailure.ServiceFailed _) -> ()
    | other -> failwith $"expected service failure, got {other}"
    let calls = sent.Count
    Assert.Equal(6 + 3, calls) // six maps, then one round of three groups
    Assert.Equal(3, sent |> Seq.filter isCombine |> Seq.length)

[<Fact; Trait("Task", "T006")>]
let ``a combined point citing a page outside its group is omitted`` () =
    let sent, reports = List(), List()
    let reply messages =
        if isCombine messages then
            pointsJson [ "good", [ (citedIn messages).Head ]; "bad", [ 99 ] ]
        else pointsJson [ text 20, citedIn messages ]
    match runLong reply sent reports with
    | Ok result ->
        Assert.DoesNotContain(result.Points, fun p -> p.Pages |> List.contains 99)
        Assert.True(result.Omitted >= 1)
    | Error failure -> failwith $"expected Ok, got {failure}"

[<Fact; Trait("Task", "T006")>]
let ``points keep their order and omitted counts add up across several replies and combine rounds`` () =
    let sent, reports = List(), List()
    let padding = text 80
    let bad = "bad|", [ 99 ] // cites a page that was never sent, so it is omitted every time
    let idsOf (messages: Summarize.ChatMessage list) =
        messages.[1].Content.Split('\n')
        |> Array.map (fun line -> Text.RegularExpressions.Regex.Match(line, @"^- \(pages [\d, ]+\) ([^|]*)\|").Groups.[1].Value)
        |> Array.toList
    // Map: two distinct points per chunk (1a, 1b, 2a, ...) and one invalid one. Combine: one point
    // whose text lists the ids it was given in order, so any reordering shows in the result.
    let reply messages =
        if isCombine messages then
            pointsJson [ String.Join("+", idsOf messages) + "|" + padding, citedIn messages; bad ]
        else
            let n = (citedIn messages).Head
            pointsJson [ $"{n}a|{padding}", [ n ]; $"{n}b|{padding}", [ n ]; bad ]
    match runLong reply sent reports with
    | Ok result ->
        let ids = [ for n in 1..6 do yield $"{n}a"; yield $"{n}b" ] |> String.concat "+"
        Assert.Equal<SummaryPoint list>([ { Text = $"{ids}|{padding}"; Pages = [ 1; 2; 3; 4; 5; 6 ] } ], result.Points)
        Assert.Equal(6 + 6 + 3 + 2 + 1, result.Omitted) // six maps, then four combine rounds of 6, 3, 2 and 1 calls
    | Error failure -> failwith $"expected Ok, got {failure}"
    // 12 map points in groups of two, then 6 combined points in groups of two, then 3 as [2; 1], then the last two.
    let combines = sent |> Seq.filter isCombine |> Seq.map idsOf |> Seq.toList
    Assert.Equal<string list list>(
        [ [ "1a"; "1b" ]; [ "2a"; "2b" ]; [ "3a"; "3b" ]; [ "4a"; "4b" ]; [ "5a"; "5b" ]; [ "6a"; "6b" ]
          [ "1a+1b"; "2a+2b" ]; [ "3a+3b"; "4a+4b" ]; [ "5a+5b"; "6a+6b" ]
          [ "1a+1b+2a+2b"; "3a+3b+4a+4b" ]; [ "5a+5b+6a+6b" ]
          [ "1a+1b+2a+2b+3a+3b+4a+4b"; "5a+5b+6a+6b" ] ],
        combines)

[<Fact; Trait("Task", "T008")>]
let ``a request cancelled between service calls starts no further call`` () =
    let sent = List()
    use source = new CancellationTokenSource()
    let capabilities : Summarize.Capabilities =
        { ExtractPages = fun _ -> failwith "not used"
          // The first call answers validly and abandons the request; the capability itself ignores the token.
          Complete = fun messages _ -> sent.Add messages; source.Cancel(); Task.FromResult (Ok (pointsJson [ "point", [ 1 ] ]))
          Report = fun _ -> Task.FromResult() }
    let run () = (Summarize.summarize capabilities 250 longPages source.Token).GetAwaiter().GetResult()
    Assert.ThrowsAny<OperationCanceledException>(fun () -> run () |> ignore) |> ignore
    Assert.Equal(1, sent.Count)

[<Fact; Trait("Task", "T010")>]
let ``a rejected request names the settings to check and no failure detail carries service text`` () =
    let rejected = Summarize.failureDetail (Summarize.ServiceError.ErrorStatus 400)
    for setting in [ "Model"; "Temperature"; "MaxOutputTokens" ] do Assert.Contains(setting, rejected)
    let others =
        [ Summarize.ServiceError.Unreachable; Summarize.ServiceError.ErrorStatus 500; Summarize.ServiceError.Redirected
          Summarize.ServiceError.TimedOut; Summarize.ServiceError.UnusableOutput ]
        |> List.map Summarize.failureDetail
    // Each failure kind is explained in its own words, and only a 400 points at the settings.
    Assert.Equal(others.Length, (others |> List.distinct |> List.length))
    for detail in others do
        Assert.DoesNotContain("MaxOutputTokens", detail)
        Assert.Contains("Try again", detail)
