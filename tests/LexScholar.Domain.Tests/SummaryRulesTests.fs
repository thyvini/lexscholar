/// Pure readability rules of summaries (F003 T002, research.md section 3).
module LexScholar.Domain.Tests.SummaryRulesTests

open LexScholar.Domain
open Xunit

let private page number (text: string) : PageText = { Number = number; Text = text }
let private long = System.String('a', 250)

[<Fact; Trait("Task", "T002")>]
let ``pages with letters or digits are readable and the rest are listed as unreadable`` () =
    let result = Summaries.readability [ page 1 long; page 2 "  - . · "; page 3 "12"; page 4 "" ]
    match result with
    | Ok pages ->
        Assert.Equal<int list>([ 1; 3 ], pages.Readable |> List.map (fun p -> p.Number))
        Assert.Equal<int list>([ 2; 4 ], pages.Unreadable)
    | Error reason -> failwith $"expected Ok, got {reason}"

[<Fact; Trait("Task", "T002")>]
let ``no readable page is NoReadableText`` () =
    Assert.Equal(Error NoReadableText, Summaries.readability [ page 1 ""; page 2 " ... " ])
    Assert.Equal(Error NoReadableText, Summaries.readability [])

[<Fact; Trait("Task", "T002")>]
let ``fewer than 200 readable characters is NotEnoughText, exactly 200 is enough`` () =
    Assert.Equal(Error NotEnoughText, Summaries.readability [ page 1 (System.String('a', 199)) ])
    Assert.Equal(Error NotEnoughText, Summaries.readability [ page 1 (System.String('a', 100)); page 2 "!!!"; page 3 (System.String('b', 99)) ])
    Assert.True(Result.isOk (Summaries.readability [ page 1 (System.String('a', 100)); page 2 (System.String('b', 100)) ]))

let private allowed = Set.ofList [ 1; 2; 3; 4 ]

let private valid (output: string) =
    match Summaries.parsePoints allowed output with
    | Ok points -> points
    | Error _ -> failwith "expected Ok"

[<Fact; Trait("Task", "T003")>]
let ``plain JSON points are parsed with sorted, de-duplicated pages`` () =
    let result = valid """{"points":[{"text":" First ","pages":[3,1,3]},{"text":"Second","pages":[2]}]}"""
    Assert.Equal(0, result.Omitted)
    Assert.Equal<SummaryPoint list>([ { Text = "First"; Pages = [ 1; 3 ] }; { Text = "Second"; Pages = [ 2 ] } ], result.Points)

[<Fact; Trait("Task", "T003")>]
let ``think blocks and code fences around the JSON are ignored`` () =
    let result = valid "<think>maybe {\"points\": []} here</think>\n```json\n{\"points\":[{\"text\":\"A\",\"pages\":[1]}]}\n```"
    Assert.Equal<SummaryPoint list>([ { Text = "A"; Pages = [ 1 ] } ], result.Points)

[<Theory; Trait("Task", "T003")>]
[<InlineData("""{"points":[{"text":"A","pages":[0]}]}""")>]
[<InlineData("""{"points":[{"text":"A","pages":[-1]}]}""")>]
[<InlineData("""{"points":[{"text":"A","pages":[1.5]}]}""")>]
[<InlineData("""{"points":[{"text":"A","pages":["1"]}]}""")>]
[<InlineData("""{"points":[{"text":"A","pages":[]}]}""")>]
[<InlineData("""{"points":[{"text":"A"}]}""")>]
[<InlineData("""{"points":[{"text":"","pages":[1]}]}""")>]
[<InlineData("""{"points":[{"text":"   ","pages":[1]}]}""")>]
[<InlineData("""{"points":[{"text":7,"pages":[1]}]}""")>]
[<InlineData("""{"points":[{"text":"A","pages":[1,99]}]}""")>]
[<InlineData("""{"points":["not an object"]}""")>]
let ``a point with an invalid or out-of-set citation, or empty text, is omitted and counted`` (output: string) =
    let result = valid output
    Assert.Empty(result.Points)
    Assert.Equal(1, result.Omitted)

[<Fact; Trait("Task", "T003")>]
let ``text over 1000 characters is omitted`` () =
    let output = $"""{{"points":[{{"text":"{System.String('x', 1001)}","pages":[1]}},{{"text":"{System.String('x', 1000)}","pages":[1]}}]}}"""
    let result = valid output
    Assert.Equal(1, result.Points.Length)
    Assert.Equal(1, result.Omitted)

[<Theory; Trait("Task", "T003")>]
[<InlineData("")>]
[<InlineData("sorry, I cannot")>]
[<InlineData("""{"points": "none"}""")>]
[<InlineData("""{"other": []}""")>]
[<InlineData("""{"points": [""")>]
[<InlineData("""[1,2]""")>]
let ``output that is not the requested JSON is Unparseable`` (output: string) =
    Assert.Equal(Error Unparseable, Summaries.parsePoints allowed output)

[<Fact; Trait("Task", "T003")>]
let ``an empty points array is Ok with nothing`` () =
    let result = valid """{"points":[]}"""
    Assert.Empty(result.Points)
    Assert.Equal(0, result.Omitted)

[<Fact; Trait("Task", "T003")>]
let ``fences and think tags inside a point's text are preserved`` () =
    let result = valid "{\"points\":[{\"text\":\"Use ```code``` and <think>x</think> as written.\",\"pages\":[1]}]}"
    Assert.Equal("Use ```code``` and <think>x</think> as written.", result.Points.Head.Text)
