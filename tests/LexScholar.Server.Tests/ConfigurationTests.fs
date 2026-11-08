module LexScholar.Server.Tests.ConfigurationTests

open System
open LexScholar.Server
open LexScholar.Server.Tests.ProcessHarness
open Xunit

// Test-facing configuration names; the approved model specifies limits but not
// key spellings. They mirror WorkspaceConfiguration.ImportLimits.
[<Literal>]
let private maxBytes = "LexScholar:ImportLimits:MaxBytes"
[<Literal>]
let private maxPages = "LexScholar:ImportLimits:MaxPages"

let private assertLimits expectedBytes expectedPages overrides =
    use workspace = TemporaryWorkspace.Create()
    let builder = HostSetup.builder workspace.Root overrides
    let loaded =
        try Some (Configuration.load builder.Configuration)
        with :? NotImplementedException -> None
    match loaded with
    | Some actual ->
        Assert.Equal(expectedBytes, actual.ImportLimits.MaxBytes)
        Assert.Equal(expectedPages, actual.ImportLimits.MaxPages)
        Assert.Equal(workspace.Root, actual.DataDirectory)
    | None ->
        Assert.Fail("T005 / FR-003, SC-008: Configuration.load is unimplemented; expected configured limits, not a placeholder failure.")

[<Fact; Trait("Task", "T005")>]
let ``absent limits default to 100000000 bytes and 2000 pages`` () =
    assertLimits 100_000_000L 2_000 []

[<Theory; Trait("Task", "T005")>]
[<InlineData("1", "1", 1L, 1)>]
[<InlineData("250000000", "5000", 250000000L, 5000)>]
[<InlineData("9000000000", "7", 9000000000L, 7)>]
[<InlineData(null, "3", 100000000L, 3)>]
[<InlineData("42", null, 42L, 2000)>]
let ``positive overrides are honored and unspecified limits keep defaults`` (bytes: string) (pages: string) expectedBytes expectedPages =
    let overrides =
        [ if not (isNull bytes) then maxBytes, bytes
          if not (isNull pages) then maxPages, pages ]
    assertLimits expectedBytes expectedPages overrides

[<Theory; Trait("Task", "T005")>]
[<InlineData(maxBytes, "abc")>]
[<InlineData(maxBytes, "1.5")>]
[<InlineData(maxBytes, "")>]
[<InlineData(maxBytes, "9223372036854775808")>]
[<InlineData(maxBytes, "0")>]
[<InlineData(maxBytes, "-1")>]
[<InlineData(maxPages, "abc")>]
[<InlineData(maxPages, "1.5")>]
[<InlineData(maxPages, "")>]
[<InlineData(maxPages, "2147483648")>]
[<InlineData(maxPages, "0")>]
[<InlineData(maxPages, "-1")>]
let ``invalid limits produce a validation failure rather than a placeholder`` key value =
    use workspace = TemporaryWorkspace.Create()
    let builder = HostSetup.builder workspace.Root [ key, value ]
    let error = Record.Exception(fun () -> Configuration.load builder.Configuration |> ignore)
    Assert.NotNull(error)
    Assert.False(error :? NotImplementedException,
                 $"T005 / plan constraints: {key}='{value}' must be rejected as invalid configuration, not NotImplementedException.")
    Assert.False(String.IsNullOrWhiteSpace(error.Message))

[<Theory; Trait("Task", "T005")>]
[<InlineData(maxBytes, "abc")>]
[<InlineData(maxBytes, "1.5")>]
[<InlineData(maxBytes, "")>]
[<InlineData(maxBytes, "9223372036854775808")>]
[<InlineData(maxBytes, "0")>]
[<InlineData(maxBytes, "-1")>]
[<InlineData(maxPages, "abc")>]
[<InlineData(maxPages, "1.5")>]
[<InlineData(maxPages, "")>]
[<InlineData(maxPages, "2147483648")>]
[<InlineData(maxPages, "0")>]
[<InlineData(maxPages, "-1")>]
let ``invalid configuration prevents the application from starting`` key value =
    use workspace = TemporaryWorkspace.Create()
    use host = HostProcess.Start(workspace.Root, [ key, value ])
    Assert.True(host.Startup.Kind = "rejected",
                $"T005 / plan constraints: {key}='{value}' must prevent startup, but the child reported {host.Startup.Kind} at {host.Startup.Address}.")
    Assert.NotEqual<string>(typeof<NotImplementedException>.FullName, host.Startup.ErrorType)
    Assert.False(String.IsNullOrWhiteSpace(host.Startup.Message))
    Assert.NotEqual(0, host.ExitCode)
