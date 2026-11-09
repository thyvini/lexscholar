module LexScholar.Server.Tests.ProcessHarnessSmoke

open System
open System.Net
open LexScholar.Server.Tests.ProcessHarness
open Xunit

[<Fact>]
let ``subprocess handshake HTTP and graceful shutdown work`` () =
    use workspace = TemporaryWorkspace.Create()
    use host = HostProcess.Start(workspace.Root, [])
    Assert.Equal("ready", host.Startup.Kind)
    Assert.NotEqual(Environment.ProcessId, host.Id)
    use browser = client host
    use response = browser.GetAsync("/api/projects").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    host.Stop()
    Assert.True(host.HasExited)

[<Fact>]
let ``subprocess termination is reaped before environment cleanup`` () =
    use workspace = TemporaryWorkspace.Create()
    use host = HostProcess.Start(workspace.Root, [])
    Assert.Equal("ready", host.Startup.Kind)
    host.Terminate()
    Assert.True(host.HasExited)
