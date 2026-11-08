namespace LexScholar.Server.Tests

open System
open System.Collections.Generic
open System.IO
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open LexScholar.Server

/// Owns test data independently of a host so multiple processes can target it.
type TemporaryWorkspace private (root: string) =
    member _.Root = root
    member _.DatabasePath = Path.Combine(root, "workspace.sqlite")

    static member Create() =
        let root = Path.Combine(Path.GetTempPath(), "lexscholar-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        new TemporaryWorkspace(root)

    interface IDisposable with
        member _.Dispose() = Directory.Delete(root, true)

module HostSetup =
    let builder root overrides =
        let options = WebApplicationOptions(ContentRootPath = root, EnvironmentName = "Testing")
        let builder = WebApplication.CreateBuilder(options)
        // Do not inherit developer configuration or environment-supplied paths.
        builder.Configuration.Sources.Clear()
        let settings = Dictionary<string, string>()
        settings.Add("LexScholar:DataDirectory", root)
        settings.Add("LexScholar:DatabasePath", Path.Combine(root, "workspace.sqlite"))
        for key, value in overrides do
            settings.[key] <- value
        builder.Configuration.AddInMemoryCollection(settings) |> ignore
        builder.WebHost.UseUrls("http://127.0.0.1:0") |> ignore
        builder

/// Each instance owns a private directory, database, server, and HTTP client.
type TestHarness private (workspace: TemporaryWorkspace, app: WebApplication, client: HttpClient) =
    member _.Root = workspace.Root
    member _.DatabasePath = workspace.DatabasePath
    member _.App = app
    member _.Client = client

    static member Start() =
        let workspace = TemporaryWorkspace.Create()
        let mutable app: WebApplication option = None
        try
            let cs = SqliteConnectionStringBuilder(DataSource = workspace.DatabasePath, Pooling = false)
            use connection = new SqliteConnection(cs.ToString())
            connection.Open()

            let builder = HostSetup.builder workspace.Root []
            let host = Program.create builder false
            app <- Some host
            host.StartAsync().GetAwaiter().GetResult()
            let server = host.Services.GetRequiredService<IServer>()
            let addresses = server.Features.Get<IServerAddressesFeature>().Addresses
            let address = addresses |> Seq.exactlyOne
            let client = new HttpClient(BaseAddress = Uri(address))
            new TestHarness(workspace, host, client)
        with _ ->
            match app with
            | Some host -> host.DisposeAsync().AsTask().GetAwaiter().GetResult()
            | None -> ()
            (workspace :> IDisposable).Dispose()
            reraise()

    interface IDisposable with
        member _.Dispose() =
            client.Dispose()
            try
                app.StopAsync().GetAwaiter().GetResult()
            finally
                try app.DisposeAsync().AsTask().GetAwaiter().GetResult()
                finally (workspace :> IDisposable).Dispose()
