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

/// Each instance owns a private directory, database, server, and HTTP client.
type TestHarness private (root: string, databasePath: string, app: WebApplication, client: HttpClient) =
    member _.Root = root
    member _.DatabasePath = databasePath
    member _.App = app
    member _.Client = client

    static member Start() =
        let root = Path.Combine(Path.GetTempPath(), "lexscholar-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        let databasePath = Path.Combine(root, "workspace.sqlite")
        let mutable app: WebApplication option = None
        try
            let cs = SqliteConnectionStringBuilder(DataSource = databasePath, Pooling = false)
            use connection = new SqliteConnection(cs.ToString())
            connection.Open()

            let options = WebApplicationOptions(ContentRootPath = root, EnvironmentName = "Testing")
            let builder = WebApplication.CreateBuilder(options)
            builder.WebHost.UseUrls("http://127.0.0.1:0") |> ignore
            let settings = Dictionary<string, string>()
            settings.Add("LexScholar:DataDirectory", root)
            settings.Add("LexScholar:DatabasePath", databasePath)
            builder.Configuration.AddInMemoryCollection(settings) |> ignore
            let host = Program.create builder false
            app <- Some host
            host.StartAsync().GetAwaiter().GetResult()
            let server = host.Services.GetRequiredService<IServer>()
            let addresses = server.Features.Get<IServerAddressesFeature>().Addresses
            let address = addresses |> Seq.exactlyOne
            let client = new HttpClient(BaseAddress = Uri(address))
            new TestHarness(root, databasePath, host, client)
        with _ ->
            match app with
            | Some host -> host.DisposeAsync().AsTask().GetAwaiter().GetResult()
            | None -> ()
            Directory.Delete(root, true)
            reraise()

    interface IDisposable with
        member _.Dispose() =
            client.Dispose()
            try
                app.StopAsync().GetAwaiter().GetResult()
            finally
                try app.DisposeAsync().AsTask().GetAwaiter().GetResult()
                finally Directory.Delete(root, true)
