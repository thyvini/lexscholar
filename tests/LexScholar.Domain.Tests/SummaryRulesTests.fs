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
