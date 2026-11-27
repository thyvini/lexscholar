module LexScholar.Browser.Tests.PublishedWorkspaceJourneyTests

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Net.Http
open System.Runtime.InteropServices
open System.Threading.Tasks
open Microsoft.Playwright
open Xunit
open LexScholar.Client
open LexScholar.Browser.Tests.Ui

let private repo = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))
let private deadline = TimeSpan.FromSeconds(60.)

[<DllImport("libc", EntryPoint = "kill", SetLastError = true)>]
extern int private signalProcess(int pid, int signal)

let private run (directory: string) (program: string) (args: string list) =
    let info = ProcessStartInfo(program, WorkingDirectory = directory, UseShellExecute = false)
    for arg in args do info.ArgumentList.Add(arg)
    use child = Process.Start(info)
    if not (child.WaitForExit(300_000)) then
        child.Kill(true)
        failwithf "%s timed out" program
    if child.ExitCode <> 0 then failwithf "%s failed with exit code %d" program child.ExitCode

let private candidate supplied local =
    match supplied with
    | Some path ->
        let path = Path.GetFullPath(path)
        if not (File.Exists(path)) then failwithf "Candidate archive does not exist: %s" path
        path
    | None ->
        run repo "bash" [ "scripts/package-workspace.sh"; local ]
        local

type PublishedWorkspace(archive: string, root: string) =
    let publish = Path.Combine(root, "publish")
    let workspace = Path.Combine(root, "workspace")
    let mutable child: Process = null
    do
        Directory.CreateDirectory(publish) |> ignore
        Directory.CreateDirectory(workspace) |> ignore
        run root "tar" [ "-xzf"; archive; "-C"; publish ]
    member _.Publish = publish
    member _.Workspace = workspace
    member _.Stop() =
        if not (isNull child) then
            if not child.HasExited then
                if signalProcess(child.Id, 15) <> 0 then failwith "Could not stop published host"
                if not (child.WaitForExit(10_000)) then
                    child.Kill(true)
                    failwith "Published host did not stop on SIGTERM"
            child.Dispose()
            child <- null
    member _.RefuseSecond() = task {
        let info = ProcessStartInfo("dotnet", WorkingDirectory = publish, UseShellExecute = false,
                                    RedirectStandardOutput = true, RedirectStandardError = true)
        info.ArgumentList.Add("LexScholar.Server.dll")
        info.ArgumentList.Add("--LexScholar:Port=0")
        info.Environment["LexScholar__DataDirectory"] <- workspace
        use other = Process.Start(info)
        let output = other.StandardOutput.ReadToEndAsync()
        let errors = other.StandardError.ReadToEndAsync()
        try
            do! other.WaitForExitAsync().WaitAsync(deadline)
            let! stdout = output
            let! stderr = errors
            Assert.NotEqual(0, other.ExitCode)
            Assert.DoesNotContain("address already in use", stdout + stderr, StringComparison.OrdinalIgnoreCase)
            Assert.Contains("already in use by another instance", stdout + stderr, StringComparison.OrdinalIgnoreCase)
        finally
            if not other.HasExited then other.Kill(true)
    }
    member _.Start() = task {
        let info = ProcessStartInfo("dotnet", WorkingDirectory = publish, UseShellExecute = false,
                                    RedirectStandardOutput = true, RedirectStandardError = true)
        info.ArgumentList.Add("LexScholar.Server.dll")
        info.ArgumentList.Add("--LexScholar:Port=0")
        info.Environment["LexScholar__DataDirectory"] <- workspace
        child <- Process.Start(info)
        let errors = child.StandardError.ReadToEndAsync()
        let mutable address = None
        while address.IsNone do
            let! line = child.StandardOutput.ReadLineAsync().WaitAsync(deadline)
            if isNull line then
                let! error = errors
                failwithf "Published host exited before listening: %s" error
            let marker = "Now listening on: "
            let index = line.IndexOf(marker, StringComparison.Ordinal)
            if index >= 0 then address <- Some (line.Substring(index + marker.Length).Trim())
        // Continue draining output so the host cannot block on a full pipe.
        child.StandardOutput.ReadToEndAsync() |> ignore
        return address.Value
    }
    interface IDisposable with
        member this.Dispose() = this.Stop()

[<Fact; Trait("Task", "T001")>]
let ``package clean and publish use the same isolated source without checkout outputs`` () =
    let root = Path.Combine(Path.GetTempPath(), "lexscholar-package-test-" + Guid.NewGuid().ToString("N"))
    let checkout = Path.Combine(root, "checkout")
    let scripts = Path.Combine(checkout, "scripts")
    let server = Path.Combine(checkout, "src", "LexScholar.Server")
    let tools = Path.Combine(root, "tools")
    let log = Path.Combine(root, "calls")
    let scriptPath = Path.Combine(scripts, "package-workspace.sh")
    Directory.CreateDirectory(scripts) |> ignore
    Directory.CreateDirectory(tools) |> ignore
    Directory.CreateDirectory(Path.Combine(server, "bin")) |> ignore
    Directory.CreateDirectory(Path.Combine(server, "obj")) |> ignore
    try
        for name in [ "global.json"; "Directory.Build.props"; "Directory.Packages.props" ] do
            File.WriteAllText(Path.Combine(checkout, name), "fixture")
        File.WriteAllText(Path.Combine(server, "LexScholar.Server.fsproj"), "source marker")
        File.WriteAllText(Path.Combine(server, "bin", "stale"), "stale")
        File.WriteAllText(Path.Combine(server, "obj", "stale"), "stale")
        let fakeDotnet = Path.Combine(tools, "dotnet")
        File.WriteAllText(fakeDotnet, String.concat "\n" [
            "#!/usr/bin/env bash"
            "set -eu"
            "project=\"$2\""
            "dir=\"$(dirname \"$project\")\""
            "source=missing; bin=absent; obj=absent"
            "[ ! -f \"$project\" ] || source=\"$(cat \"$project\")\""
            "[ ! -e \"$dir/bin/stale\" ] || bin=present"
            "[ ! -e \"$dir/obj/stale\" ] || obj=present"
            "printf '%s|%s|%s|%s|%s\\n' \"$1\" \"$project\" \"$source\" \"$bin\" \"$obj\" >> \"$CALL_LOG\""
            "" ])
        File.SetUnixFileMode(fakeDotnet, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        let original = File.ReadAllText(Path.Combine(repo, "scripts/package-workspace.sh"))
        let exercise (script: string) =
            File.WriteAllText(scriptPath, script)
            if File.Exists(log) then File.Delete(log)
            let info = ProcessStartInfo("bash", WorkingDirectory = checkout, UseShellExecute = false)
            info.ArgumentList.Add(scriptPath)
            info.ArgumentList.Add(Path.Combine(root, "candidate.tar.gz"))
            info.Environment["PATH"] <- tools + ":" + Environment.GetEnvironmentVariable("PATH")
            info.Environment["CALL_LOG"] <- log
            info.Environment["TMPDIR"] <- root
            use child = Process.Start(info)
            Assert.True(child.WaitForExit(30_000), "Package builder must finish")
            Assert.Equal(0, child.ExitCode)
            let calls = File.ReadAllLines(log) |> Array.map (fun line -> line.Split('|'))
            Assert.Equal(2, calls.Length)
            Assert.Equal("clean", calls[0][0])
            Assert.Equal("publish", calls[1][0])
            Assert.Equal(calls[0][1], calls[1][1])
            for call in calls do
                Assert.False(call[1].StartsWith(checkout + "/", StringComparison.Ordinal), "Build must not target checkout")
                Assert.Equal("source marker", call[2])
                Assert.Equal("absent", call[3])
                Assert.Equal("absent", call[4])
        exercise original
        // Sensitivity: independently redirect each command back to the checkout.
        for command in [ "clean"; "publish" ] do
            let needle = $"dotnet {command} \"$build/source/"
            Assert.Contains(needle, original)
            let mutated = original.Replace(needle, $"dotnet {command} \"$root/")
            Assert.ThrowsAny<Exception>(fun () -> exercise mutated) |> ignore
    finally
        Directory.Delete(root, true)

[<Theory; InlineData "chromium"; InlineData "firefox"; Trait("Task", "T007"); Trait("Category", "Browser")>]
let ``published candidate preserves the researcher journey through restart and deletion`` browserName =
    let root = Path.Combine(Path.GetTempPath(), "lexscholar-journey-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(root, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let supplied = Environment.GetEnvironmentVariable("LEXSCHOLAR_CANDIDATE_ARCHIVE") |> Option.ofObj
        let archive = candidate supplied (Path.Combine(root, "candidate.tar.gz"))
        let fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.pdf")
        let expected = File.ReadAllBytes(fixture)
        let exported = Path.Combine(root, "export.zip")
        use host = new PublishedWorkspace(archive, root)
        (withBrowser browserName (fun browser -> task {
            let! address = host.Start()
            let! page = browser.NewPageAsync()
            let! _ = page.GotoAsync(address)
            do! enterWorkspace page
            do! page.GetByLabel(Texts.projectName).FillAsync("Candidate project")
            do! click (button (page.Locator("body")) Texts.createProject)
            let project = page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = "Candidate project"))
            do! click (button project Texts.select)
            do! click (button (page.Locator("body")) Texts.library)
            do! page.GetByLabel(Texts.importPdf).SetInputFilesAsync(fixture)
            do! Assertions.Expect(page.GetByText(Texts.imported "valid.pdf")).ToBeVisibleAsync()
            do! click (button (page.Locator("ul.documents li")) Texts.addFocused)
            let source = page.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "valid.pdf"))
            do! click (button source Texts.openDocument)
            let verifyOriginal () = task {
                let main = page.GetByTestId("main")
                do! Assertions.Expect(main.Locator(".summary-request")).ToBeVisibleAsync()
                let pdf = link main Texts.openPdf
                let! href = pdf.GetAttributeAsync("href")
                let requested = TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)
                let handler = EventHandler<IRequest>(fun _ request ->
                    if request.IsNavigationRequest && request.Url.Contains("/api/documents/") then
                        requested.TrySetResult(request.Url) |> ignore)
                page.Context.Request.AddHandler(handler)
                let! popup = page.Context.RunAndWaitForPageAsync(fun () -> pdf.ClickAsync())
                let! url = requested.Task.WaitAsync(deadline)
                page.Context.Request.RemoveHandler(handler)
                Assert.Equal(Uri(Uri(address), href).AbsoluteUri, url)
                use http = new HttpClient()
                let! bytes = http.GetByteArrayAsync(url)
                Assert.Equal<byte>(expected, bytes)
                do! popup.CloseAsync()
            }
            do! verifyOriginal ()
            do! host.RefuseSecond()
            do! verifyOriginal ()
            do! page.CloseAsync()
            host.Stop()
            let! restarted = host.Start()
            let! page2 = browser.NewPageAsync()
            let! _ = page2.GotoAsync(restarted)
            do! enterWorkspace page2
            let project2 = page2.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = "Candidate project"))
            do! Assertions.Expect(project2).ToBeVisibleAsync()
            let source2 = page2.Locator("ul.project-documents li").Filter(LocatorFilterOptions(HasText = "valid.pdf"))
            do! click (button source2 Texts.openDocument)
            let pdf2 = link (page2.GetByTestId("main")) Texts.openPdf
            let! href2 = pdf2.GetAttributeAsync("href")
            use http = new HttpClient()
            let! bytes2 = http.GetByteArrayAsync(Uri(Uri(restarted), href2))
            Assert.Equal<byte>(expected, bytes2)
            do! click (button (page2.Locator("body")) Texts.library)
            let downloadTask = page2.WaitForDownloadAsync()
            do! click (button (page2.Locator("body")) "Exportar biblioteca")
            let! download = downloadTask
            do! download.SaveAsAsync(exported)
            let checkExport () =
                use zip = ZipFile.OpenRead(exported)
                let entries = zip.Entries |> Seq.filter (fun e -> e.FullName.StartsWith("pdf/") && e.FullName.EndsWith(".pdf")) |> Seq.toArray
                Assert.Single(entries) |> ignore
                use stream = entries[0].Open()
                use memory = new MemoryStream()
                stream.CopyTo(memory)
                Assert.Equal<byte>(expected, memory.ToArray())
            checkExport ()
            do! page2.GetByRole(AriaRole.Checkbox, PageGetByRoleOptions(Name = $"{Texts.deletionSelection}: valid.pdf", Exact = true)).CheckAsync()
            do! click (button (page2.Locator("body")) Texts.deleteSelection)
            do! click (button (page2.Locator("body")) Texts.deletionConfirm)
            do! Assertions.Expect(page2.GetByText(Texts.deletionComplete, PageGetByTextOptions(Exact = true))).ToBeVisibleAsync()
            do! page2.CloseAsync()
            host.Stop()
            let! last = host.Start()
            let! page3 = browser.NewPageAsync()
            let! _ = page3.GotoAsync(last)
            do! enterWorkspace page3
            do! Assertions.Expect(page3.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = "Candidate project"))).ToBeVisibleAsync()
            do! Assertions.Expect(page3.Locator("ul.project-documents li")).ToHaveCountAsync(0)
            do! click (button (page3.Locator("body")) Texts.library)
            do! Assertions.Expect(page3.GetByText(Texts.emptyLibrary)).ToBeVisibleAsync()
            checkExport ()
        })).GetAwaiter().GetResult()
    finally
        Directory.Delete(root, true)

[<Fact; Trait("Task", "T001")>]
let ``explicitly missing candidate is rejected rather than rebuilt`` () =
    let missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing.tar.gz")
    let error = Assert.ThrowsAny<Exception>(fun () -> candidate (Some missing) missing |> ignore)
    Assert.Contains("Candidate archive does not exist", error.Message)

[<Fact; Trait("Task", "T001"); Trait("Category", "Browser")>]
let ``published Linux candidate serves an interactive workspace`` () =
    let root = Path.Combine(Path.GetTempPath(), "lexscholar-published-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(root) |> ignore
    try
        let supplied = Environment.GetEnvironmentVariable("LEXSCHOLAR_CANDIDATE_ARCHIVE") |> Option.ofObj
        let archive = candidate supplied (Path.Combine(root, "candidate.tar.gz"))
        use host = new PublishedWorkspace(archive, root)
        (task {
            let! address = host.Start()
            do! withBrowser "chromium" (fun browser -> task {
                let! page = browser.NewPageAsync()
                let! _ = page.GotoAsync(address)
                do! enterWorkspace page
                do! page.GetByLabel(Texts.projectName).FillAsync("Published candidate project")
                do! click (button (page.Locator("body")) Texts.createProject)
                do! Assertions.Expect(page.Locator("ul.projects li").Filter(LocatorFilterOptions(HasText = "Published candidate project"))).ToBeVisibleAsync()
                // An API-only host is not a passing candidate: suppress the WASM assets.
                let! broken = browser.NewPageAsync()
                let assetRequested = TaskCompletionSource<unit>()
                use! _ = broken.RouteAsync("**/_framework/**", Func<IRoute, Task>(fun route ->
                    assetRequested.TrySetResult(()) |> ignore
                    route.AbortAsync()))
                let! _ = broken.GotoAsync(address)
                do! assetRequested.Task.WaitAsync(deadline)
                use api = new HttpClient()
                let! response = api.GetAsync(address + "/api/projects")
                Assert.True(response.IsSuccessStatusCode)
                do! Assertions.Expect(broken.Locator(".workspace[data-ready='True']")).Not.ToBeVisibleAsync(LocatorAssertionsToBeVisibleOptions(Timeout = 3000.f))
            })
        }).GetAwaiter().GetResult()
    finally
        Directory.Delete(root, true)
