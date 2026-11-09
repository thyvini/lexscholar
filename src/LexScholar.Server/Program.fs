module LexScholar.Server.Program

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting.StaticWebAssets
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open LexScholar.Server
open LexScholar.Infrastructure

type StartupError = ConfigurationFailure of ConfigurationError | LockFailure of LockError | InvalidPort of string

let describeStartupError = function
    | ConfigurationFailure (InvalidLimit (name, value)) -> $"Invalid {name} value '{value}': enter a positive whole number within the supported range."
    | ConfigurationFailure (NonPositiveLimit name) -> $"Invalid {name}: the limit must be greater than zero."
    | ConfigurationFailure (InvalidDataDirectory reason) -> $"Invalid {Configuration.DataDirectoryKey}: {reason}"
    | LockFailure (DirectoryAlreadyInUse directory) -> $"Data directory '{directory}' is already in use by another instance. Connect to that instance or choose a different directory."
    | InvalidPort value -> $"Invalid LexScholar:Port '{value}': use an integer from 0 to 65535."

type private DataDirectoryLease(handle: System.IDisposable) =
    member _.Release() = handle.Dispose()
    interface System.IDisposable with
        member this.Dispose() = this.Release()

let private createHost (builder: WebApplicationBuilder) (serveClient: bool) port =
    match Configuration.load builder.Configuration with
    | Error error -> Error (ConfigurationFailure error)
    | Ok configuration ->
        match DataDirectoryLock.acquire configuration.DataDirectory with
        | Error error -> Error (LockFailure error)
        | Ok handle ->
            let lease = new DataDirectoryLease(handle)
            try
                WorkspaceDb.initialize configuration.DataDirectory
                HostSecurity.configure builder port configuration.DataDirectory
                // The resolved factory singleton is disposed with the host even if
                // it is built but never started. ApplicationStopped also releases it
                // after a normal stop; SafeFileHandle disposal is idempotent.
                builder.Services.AddSingleton<DataDirectoryLease>(fun _ -> lease) |> ignore
                let app = builder.Build()
                try
                    app.Services.GetRequiredService<DataDirectoryLease>() |> ignore
                    app.Lifetime.ApplicationStopped.Register(fun () -> lease.Release()) |> ignore
                    HostSecurity.useSecurity app configuration.DataDirectory
                    if serveClient then
                        // Built applications use the development asset manifest;
                        // published applications use their physical wwwroot.
                        StaticWebAssetsLoader.UseStaticWebAssets(app.Environment, app.Configuration)
                    if serveClient then
                        app.MapStaticAssets() |> ignore
                        app.MapGet("/", System.Func<IResult>(fun () -> Results.Redirect("/index.html"))) |> ignore
                    LexScholar.Server.Endpoints.map app configuration.DataDirectory
                    Ok app
                with _ ->
                    app.DisposeAsync().AsTask().GetAwaiter().GetResult()
                    reraise()
            with _ ->
                lease.Release()
                reraise()

let create (builder: WebApplicationBuilder) (serveClient: bool) =
    let portValue = builder.Configuration.["LexScholar:Port"]
    let portResult =
        if isNull portValue then Ok 5000
        else
            match System.Int32.TryParse(portValue) with
            | true, port when port >= 0 && port <= 65535 -> Ok port
            | _ -> Error (InvalidPort portValue)
    match portResult with
    | Error error -> Error error
    | Ok port -> createHost builder serveClient port

[<EntryPoint>]
let main args =
    match create (WebApplication.CreateBuilder(args)) true with
    | Ok app ->
        use app = app
        app.Run()
        0
    | Error error ->
        System.Console.Error.WriteLine(describeStartupError error)
        1
