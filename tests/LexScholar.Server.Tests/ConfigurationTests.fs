module LexScholar.Server.Tests.ConfigurationTests

open System
open System.IO
open Microsoft.Extensions.Configuration
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
    | Some (Ok actual) ->
        Assert.Equal(expectedBytes, actual.ImportLimits.MaxBytes)
        Assert.Equal(expectedPages, actual.ImportLimits.MaxPages)
        Assert.Equal(workspace.Root, actual.DataDirectory)
    | Some (Error error) -> Assert.Fail($"Valid configuration was rejected: {error}")
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
    let result =
        try Some (Configuration.load builder.Configuration)
        with :? NotImplementedException -> None
    let expected =
        if value = "0" || value = "-1" then NonPositiveLimit key
        else InvalidLimit (key, value)
    match result with
    | Some (Error error) -> Assert.Equal(expected, error)
    | Some (Ok _) -> Assert.Fail($"T005: {key}='{value}' was accepted rather than rejected.")
    | None -> Assert.Fail($"T005: {key}='{value}' requires typed validation, but Configuration.load is unimplemented.")

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
    let expected = (if value = "0" || value = "-1" then "NonPositiveLimit:" else "InvalidLimit:") + key
    Assert.Equal<string>(expected, host.Startup.ErrorType)
    Assert.False(String.IsNullOrWhiteSpace(host.Startup.Message))
    Assert.NotEqual(0, host.ExitCode)

[<Theory; Trait("Task", "T005")>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("relative/path")>]
[<InlineData("/tmp/invalid\u0000path")>]
let ``invalid data-directory paths return a typed configuration error`` path =
    use workspace = TemporaryWorkspace.Create()
    let builder = HostSetup.builder workspace.Root [ "LexScholar:DataDirectory", path ]
    let result =
        try Some (Configuration.load builder.Configuration)
        with :? NotImplementedException -> None
    match result with
    | Some (Error (InvalidDataDirectory reason)) -> Assert.False(String.IsNullOrWhiteSpace(reason))
    | Some other -> Assert.Fail($"Expected InvalidDataDirectory; got {other}")
    | None -> Assert.Fail("T005: invalid data directories require a typed error, but Configuration.load is unimplemented.")

[<Fact; Trait("Task", "T005")>]
let ``a data-directory path referring to a file is rejected`` () =
    use workspace = TemporaryWorkspace.Create()
    let file = System.IO.Path.Combine(workspace.Root, "not-a-directory")
    System.IO.File.WriteAllText(file, "test")
    let builder = HostSetup.builder workspace.Root [ "LexScholar:DataDirectory", file ]
    let result =
        try Some (Configuration.load builder.Configuration)
        with :? NotImplementedException -> None
    match result with
    | Some (Error (InvalidDataDirectory reason)) -> Assert.False(String.IsNullOrWhiteSpace(reason))
    | Some other -> Assert.Fail($"Expected InvalidDataDirectory; got {other}")
    | None -> Assert.Fail("T005: a data directory that is a file must be rejected; configuration is unimplemented.")

[<Fact; Trait("Task", "T005")>]
let ``invalid directory prevents startup with typed diagnostic`` () =
    use workspace = TemporaryWorkspace.Create()
    use host = HostProcess.Start(workspace.Root, [ "LexScholar:DataDirectory", "relative/path" ])
    Assert.Equal<string>("rejected", host.Startup.Kind)
    Assert.Equal<string>("InvalidDataDirectory", host.Startup.ErrorType)
    Assert.Contains("LexScholar:DataDirectory", host.Startup.Message)
    Assert.Contains("absolute", host.Startup.Message)
    Assert.NotEqual(0, host.ExitCode)
    Assert.False(File.Exists(Path.Combine(workspace.Root, ".lexscholar.lock")))

[<Fact; Trait("Task", "T005")>]
let ``invalid limits are rejected before opening the data-directory lock`` () =
    use workspace = TemporaryWorkspace.Create()
    use host = HostProcess.Start(workspace.Root, [ maxBytes, "0" ])
    Assert.Equal<string>("rejected", host.Startup.Kind)
    Assert.Equal<string>("NonPositiveLimit:" + maxBytes, host.Startup.ErrorType)
    Assert.Contains(maxBytes, host.Startup.Message)
    Assert.False(File.Exists(Path.Combine(workspace.Root, ".lexscholar.lock")))

[<Fact; Trait("Task", "T005")>]
let ``omitted data directory resolves to an absolute per-user path`` () =
    let config = ConfigurationBuilder().Build()
    match Configuration.load config with
    | Error error -> Assert.Fail($"Default user configuration rejected: {error}")
    | Ok result ->
        Assert.True(Path.IsPathFullyQualified(result.DataDirectory))
        Assert.Equal("lexscholar", Path.GetFileName(result.DataDirectory))
