module LexScholar.Server.Tests.ProcessHarness

open System
open System.Diagnostics
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open LexScholar.Server
open LexScholar.Infrastructure

// A single flushed protocol message distinguishes actual startup from rejection.
// No test-side validation or lock acquisition is injected into Program.create.
type Startup =
    { Kind: string
      Pid: int
      Address: string
      ErrorType: string
      Message: string }

let private deadline = TimeSpan.FromSeconds(10.0)

let private emit kind address errorType message =
    let result =
        { Kind = kind; Pid = Environment.ProcessId; Address = address
          ErrorType = errorType; Message = message }
    Console.WriteLine(JsonSerializer.Serialize(result))
    Console.Out.Flush()

let private startupErrorCode = function
    | Program.ConfigurationFailure (InvalidLimit (name, _)) -> "InvalidLimit:" + name
    | Program.ConfigurationFailure (NonPositiveLimit name) -> "NonPositiveLimit:" + name
    | Program.ConfigurationFailure (InvalidDataDirectory _) -> "InvalidDataDirectory"
    | Program.LockFailure (DirectoryAlreadyInUse _) -> "DirectoryAlreadyInUse"
    | Program.InvalidPort _ -> "InvalidPort"

/// Entry point mode of the existing test executable, not another project.
let runChild (args: string array) =
    let root = args.[0]
    let overrides =
        args.[1..]
        |> Array.map (fun item ->
            let parts = item.Split('=', 2)
            parts.[0], parts.[1])
        |> Array.toList
    let builder = HostSetup.builder root overrides
    builder.Logging.ClearProviders() |> ignore
    let mutable started = false
    try
        match Program.create builder false with
        | Error error ->
            emit "rejected" "" (startupErrorCode error) (Program.describeStartupError error)
            2
        | Ok app ->
            use app = app
            app.StartAsync().WaitAsync(deadline).GetAwaiter().GetResult()
            started <- true
            let server = app.Services.GetRequiredService<IServer>()
            let address = server.Features.Get<IServerAddressesFeature>().Addresses |> Seq.exactlyOne
            emit "ready" address "" ""
            // EOF also stops the child if its parent disappears. Watchdog bounds an
            // abandoned pipe; no sleeps or port-readiness polling are used.
            Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(45.0)).GetAwaiter().GetResult() |> ignore
            app.StopAsync().WaitAsync(deadline).GetAwaiter().GetResult()
            0
    with error ->
        if started then
            Console.Error.WriteLine(error)
        else
            emit "rejected" "" (error.GetType().FullName) error.Message
        2

/// Process and pipe ownership are kept at the test infrastructure boundary.
type HostProcess private (child: Process, startup: Startup, errors: Task<string>) =
    member _.Startup = startup
    member _.Id = child.Id
    member _.HasExited = child.HasExited
    member _.ExitCode = child.ExitCode

    member _.Stop() =
        if not child.HasExited then
            child.StandardInput.WriteLine("stop")
            child.StandardInput.Flush()
        if not (child.WaitForExit(10_000)) then
            failwith "Test child did not stop within 10 seconds."
        if startup.Kind = "ready" && child.ExitCode <> 0 then
            failwithf "Test child shutdown failed: %s" (errors.WaitAsync(deadline).GetAwaiter().GetResult())

    member _.Terminate() =
        if not child.HasExited then child.Kill(entireProcessTree = true)
        if not (child.WaitForExit(10_000)) then
            failwith "Test child could not be reaped after termination."

    static member Start(root: string, overrides: (string * string) list) =
        let info = ProcessStartInfo("dotnet", UseShellExecute = false, RedirectStandardInput = true,
                                    RedirectStandardOutput = true, RedirectStandardError = true,
                                    WorkingDirectory = root)
        info.ArgumentList.Add(typeof<TemporaryWorkspace>.Assembly.Location)
        info.ArgumentList.Add("--foundation-host")
        info.ArgumentList.Add(root)
        for key, value in overrides do info.ArgumentList.Add($"{key}={value}")
        let child = new Process(StartInfo = info)
        let mutable launched = false
        try
            launched <- child.Start()
            if not launched then failwith "Could not launch test child."
            let errors = child.StandardError.ReadToEndAsync()
            let line = child.StandardOutput.ReadLineAsync().WaitAsync(deadline).GetAwaiter().GetResult()
            if isNull line then
                failwithf "Test child exited without a startup message: %s" (errors.WaitAsync(deadline).GetAwaiter().GetResult())
            let startup = JsonSerializer.Deserialize<Startup>(line)
            if startup.Pid <> child.Id || not (List.contains startup.Kind [ "ready"; "rejected" ]) then
                failwithf "Invalid child startup message: %s" line
            if startup.Kind = "rejected" && not (child.WaitForExit(10_000)) then
                failwith "Rejected test child did not exit."
            new HostProcess(child, startup, errors)
        with _ ->
            if launched && not child.HasExited then
                child.Kill(entireProcessTree = true)
                child.WaitForExit(10_000) |> ignore
            child.Dispose()
            reraise()

    interface IDisposable with
        member this.Dispose() =
            try
                if not child.HasExited then this.Stop()
            finally
                try
                    if not child.HasExited then this.Terminate()
                finally child.Dispose()

let client (host: HostProcess) =
    new HttpClient(BaseAddress = Uri(host.Startup.Address), Timeout = deadline)
