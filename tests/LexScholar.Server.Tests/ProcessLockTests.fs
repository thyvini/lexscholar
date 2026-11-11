module LexScholar.Server.Tests.ProcessLockTests

open System.Net
open System.IO
open System.Threading.Tasks
open LexScholar.Infrastructure
open LexScholar.Server
open LexScholar.Server.Tests.ProcessHarness
open Xunit

let private assertReady (host: HostProcess) =
    Assert.True(host.Startup.Kind = "ready",
                $"Host setup must succeed before ownership assertions: {host.Startup.ErrorType}: {host.Startup.Message}")

let private assertRefused (host: HostProcess) =
    Assert.True(host.Startup.Kind = "rejected",
                "T006 / plan process exclusivity: a second OS process served requests while the same data directory already had an owner.")
    Assert.Equal<string>("DirectoryAlreadyInUse", host.Startup.ErrorType)
    Assert.False(System.String.IsNullOrWhiteSpace(host.Startup.Message))
    Assert.Contains("already in use", host.Startup.Message)
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
let ``functional lock boundary reports only demonstrated contention`` () =
    use workspace = TemporaryWorkspace.Create()
    use owner = HostProcess.Start(workspace.Root, [])
    assertReady owner
    let result =
        try Some (DataDirectoryLock.acquire workspace.Root)
        with :? System.NotImplementedException -> None
    match result with
    | Some (Error (DirectoryAlreadyInUse directory)) -> Assert.Equal(workspace.Root, directory)
    | Some (Ok handle) ->
        handle.Dispose()
        Assert.Fail("T006: another process owns this data directory; acquire returned success.")
    | None -> Assert.Fail("T006: contention requires a typed lock error; acquire is unimplemented.")

[<Fact; Trait("Task", "T006")>]
let ``filesystem errors are not reported as lock contention`` () =
    use workspace = TemporaryWorkspace.Create()
    let file = Path.Combine(workspace.Root, "not-a-directory")
    File.WriteAllText(file, "test")
    let error = Record.Exception(fun () ->
        match DataDirectoryLock.acquire file with
        | Ok handle -> handle.Dispose()
        | Error lockError -> Assert.Fail($"Filesystem failure misclassified as contention: {lockError}"))
    Assert.NotNull(error)
    Assert.True(error :? IOException, $"T006: expected unexpected filesystem IOException, got {error.GetType().FullName}.")

[<Fact; Trait("Task", "T006")>]
let ``an unopenable lock file is not reported as contention`` () =
    use workspace = TemporaryWorkspace.Create()
    Directory.CreateDirectory(Path.Combine(workspace.Root, ".lexscholar.lock")) |> ignore
    let error = Record.Exception(fun () ->
        match DataDirectoryLock.acquire workspace.Root with
        | Ok handle ->
            handle.Dispose()
            Assert.Fail("An unopenable lock file must not be treated as acquired.")
        | Error lockError -> Assert.Fail($"Open failure misclassified as contention: {lockError}"))
    Assert.NotNull(error)
    Assert.IsType<IOException>(error) |> ignore

[<Fact; Trait("Task", "T006")>]
let ``unexpected startup IO failure is not reported as lock contention`` () =
    use workspace = TemporaryWorkspace.Create()
    Directory.CreateDirectory(Path.Combine(workspace.Root, ".lexscholar.lock")) |> ignore
    use host = HostProcess.Start(workspace.Root, [])
    Assert.Equal<string>("rejected", host.Startup.Kind)
    Assert.Equal<string>(typeof<IOException>.FullName, host.Startup.ErrorType)
    Assert.NotEqual(0, host.ExitCode)

[<Fact; Trait("Task", "T006")>]
let ``lock file symlinks are rejected rather than followed or classified as contention`` () =
    use workspace = TemporaryWorkspace.Create()
    let target = Path.Combine(workspace.Root, "unrelated")
    File.WriteAllText(target, "preserve")
    File.CreateSymbolicLink(Path.Combine(workspace.Root, ".lexscholar.lock"), target) |> ignore
    let error = Record.Exception(fun () ->
        match DataDirectoryLock.acquire workspace.Root with
        | Ok handle ->
            handle.Dispose()
            Assert.Fail("A lock-file symlink must not be followed.")
        | Error lockError -> Assert.Fail($"Symlink open failure misclassified as contention: {lockError}"))
    Assert.IsType<IOException>(error) |> ignore
    Assert.Equal("preserve", File.ReadAllText(target))

[<Fact; Trait("Task", "T006")>]
let ``same-process attempts cannot release or bypass an existing lock`` () =
    use workspace = TemporaryWorkspace.Create()
    let first = DataDirectoryLock.acquire workspace.Root
    match first with
    | Error error -> Assert.Fail($"First lock rejected: {error}")
    | Ok handle ->
        use handle = handle
        let second = DataDirectoryLock.acquire workspace.Root
        match second with
        | Error (DirectoryAlreadyInUse directory) -> Assert.Equal(workspace.Root, directory)
        | Ok other ->
            other.Dispose()
            Assert.Fail("A second handle in the same process bypassed exclusive ownership.")
        // A failed same-process attempt must not release the first lock.
        use contender = HostProcess.Start(workspace.Root, [])
        assertRefused contender

[<Fact; Trait("Task", "T006")>]
let ``new directory and lock file use private permissions and persist after release`` () =
    use workspace = TemporaryWorkspace.Create()
    let directory = Path.Combine(workspace.Root, "new-private-directory")
    match DataDirectoryLock.acquire directory with
    | Error error -> Assert.Fail($"New directory unexpectedly rejected: {error}")
    | Ok handle ->
        use handle = handle
        let privateMode = UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
        let fileMode = UnixFileMode.UserRead ||| UnixFileMode.UserWrite
        Assert.Equal(privateMode, File.GetUnixFileMode(directory))
        Assert.Equal(fileMode, File.GetUnixFileMode(Path.Combine(directory, ".lexscholar.lock")))
        handle.Dispose()
        Assert.True(File.Exists(Path.Combine(directory, ".lexscholar.lock")))
        match DataDirectoryLock.acquire directory with
        | Ok next -> next.Dispose()
        | Error error -> Assert.Fail($"Released lock could not be reacquired: {error}")

[<Fact; Trait("Task", "T006")>]
let ``disposing a constructed but unstarted host releases ownership`` () =
    use workspace = TemporaryWorkspace.Create()
    let builder = HostSetup.builder workspace.Root []
    match Program.create builder false with
    | Error error -> Assert.Fail($"Valid host unexpectedly rejected: {error}")
    | Ok app ->
        use app = app
        use contender = HostProcess.Start(workspace.Root, [])
        assertRefused contender
        app.DisposeAsync().AsTask().GetAwaiter().GetResult()
        use successor = HostProcess.Start(workspace.Root, [])
        assertReady successor

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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.False(owner.HasExited)
    use contender = HostProcess.Start(workspace.Root, [])
    assertRefused contender
