module LexScholar.Server.Program

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting.StaticWebAssets
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open LexScholar.Server
open LexScholar.Infrastructure

type StartupError = ConfigurationFailure of ConfigurationError | LockFailure of LockError

let describeStartupError = function
    | ConfigurationFailure (InvalidLimit (name, value)) -> $"Invalid {name} value '{value}': enter a positive whole number within the supported range."
    | ConfigurationFailure (NonPositiveLimit name) -> $"Invalid {name}: the limit must be greater than zero."
    | ConfigurationFailure (InvalidDataDirectory reason) -> $"Invalid {Configuration.DataDirectoryKey}: {reason}"
    | LockFailure (DirectoryAlreadyInUse directory) -> $"Data directory '{directory}' is already in use by another instance. Connect to that instance or choose a different directory."

type private DataDirectoryLease(handle: System.IDisposable) =
    member _.Release() = handle.Dispose()
    interface System.IDisposable with
        member this.Dispose() = this.Release()

let create (builder: WebApplicationBuilder) (serveClient: bool) =
    match Configuration.load builder.Configuration with
    | Error error -> Error (ConfigurationFailure error)
    | Ok configuration ->
        match DataDirectoryLock.acquire configuration.DataDirectory with
        | Error error -> Error (LockFailure error)
        | Ok handle ->
            let lease = new DataDirectoryLease(handle)
            try
                // The resolved factory singleton is disposed with the host even if
                // it is built but never started. ApplicationStopped also releases it
                // after a normal stop; SafeFileHandle disposal is idempotent.
                builder.Services.AddSingleton<DataDirectoryLease>(fun _ -> lease) |> ignore
                let app = builder.Build()
                try
                    app.Services.GetRequiredService<DataDirectoryLease>() |> ignore
                    app.Lifetime.ApplicationStopped.Register(fun () -> lease.Release()) |> ignore
#if DEBUG
                    if serveClient then
                        StaticWebAssetsLoader.UseStaticWebAssets(app.Environment, app.Configuration)
#endif
                    if serveClient then
                        app.MapStaticAssets() |> ignore
                        app.MapGet("/", System.Func<IResult>(fun () -> Results.Redirect("/index.html"))) |> ignore
                    LexScholar.Server.Endpoints.map app
                    Ok app
                with _ ->
                    app.DisposeAsync().AsTask().GetAwaiter().GetResult()
                    reraise()
            with _ ->
                lease.Release()
                reraise()

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
