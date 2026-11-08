module LexScholar.Server.Tests.ProcessLockTests

open System.Net
open System.Threading.Tasks
open LexScholar.Server.Tests.ProcessHarness
open Xunit

let private assertReady (host: HostProcess) =
    Assert.True(host.Startup.Kind = "ready",
                $"Host setup must succeed before ownership assertions: {host.Startup.ErrorType}: {host.Startup.Message}")

let private assertRefused (host: HostProcess) =
    Assert.True(host.Startup.Kind = "rejected",
                "T006 / plan process exclusivity: a second OS process served requests while the same data directory already had an owner.")
    Assert.NotEqual<string>(typeof<System.NotImplementedException>.FullName, host.Startup.ErrorType)
    Assert.False(System.String.IsNullOrWhiteSpace(host.Startup.Message))
    Assert.True(host.HasExited)
    Assert.NotEqual(0, host.ExitCode)

[<Fact; Trait("Task", "T006")>]
let ``a second operating system process cannot own the same directory`` () =
    use workspace = TemporaryWorkspace.Create()
    use owner = HostProcess.Start(workspace.Root, [])
    assertReady owner
    use contender = HostProcess.Start(workspace.Root, [])
    Assert.NotEqual(owner.Id, contender.Id)
    Assert.False(owner.HasExited)
    assertRefused contender

[<Fact; Trait("Task", "T006")>]
let ``normal owner exit releases actual exclusive ownership`` () =
    use workspace = TemporaryWorkspace.Create()
    use owner = HostProcess.Start(workspace.Root, [])
    assertReady owner
    use contender = HostProcess.Start(workspace.Root, [])
    // Finish the exit/restart sequence even in RED. A later successful startup
    // alone cannot pass this test if there was never any exclusive ownership.
    contender.Stop()
    owner.Stop()
    Assert.True(owner.HasExited)
    use successor = HostProcess.Start(workspace.Root, [])
    assertReady successor
    assertRefused contender

[<Fact; Trait("Task", "T006")>]
let ``termination releases actual exclusive ownership without deleting lock artifacts`` () =
    use workspace = TemporaryWorkspace.Create()
    use owner = HostProcess.Start(workspace.Root, [])
    assertReady owner
    use contender = HostProcess.Start(workspace.Root, [])
    contender.Stop()
    owner.Terminate()
    Assert.True(owner.HasExited)
    // Reuse the directory untouched: stale lock files must not block restart.
    use successor = HostProcess.Start(workspace.Root, [])
    assertReady successor
    assertRefused contender

[<Fact; Trait("Task", "T006")>]
let ``independent directories have independent exclusive owners`` () =
    use first = TemporaryWorkspace.Create()
    use second = TemporaryWorkspace.Create()
    use firstOwner = HostProcess.Start(first.Root, [])
    use secondOwner = HostProcess.Start(second.Root, [])
    assertReady firstOwner
    assertReady secondOwner
    use firstContender = HostProcess.Start(first.Root, [])
    use secondContender = HostProcess.Start(second.Root, [])
    Assert.False(firstOwner.HasExited)
    Assert.False(secondOwner.HasExited)
    assertRefused firstContender
    assertRefused secondContender

[<Fact; Trait("Task", "T006")>]
let ``two tab clients share the running owner without acquiring another lock`` () =
    use workspace = TemporaryWorkspace.Create()
    use owner = HostProcess.Start(workspace.Root, [])
    assertReady owner
    use firstTab = client owner
    use secondTab = client owner
    let responses =
        Task.WhenAll(firstTab.GetAsync("/api/projects"), secondTab.GetAsync("/api/projects"))
            .GetAwaiter().GetResult()
    for response in responses do
        use response = response
        // Domain routes deliberately remain T045 stubs; this tests transport,
        // not working project behavior or browser UI.
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)
    Assert.False(owner.HasExited)
    use contender = HostProcess.Start(workspace.Root, [])
    assertRefused contender
