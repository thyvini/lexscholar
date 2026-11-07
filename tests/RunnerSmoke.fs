module LexScholar.RunnerSmoke

open System.Runtime.InteropServices
open Xunit

[<Fact>]
let ``test runner executes on Linux`` () =
    Assert.True(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
