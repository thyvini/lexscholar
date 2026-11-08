module LexScholar.Server.Tests.HarnessSmoke

open System
open System.IO
open System.Net
open System.Net.Http
open Microsoft.Data.Sqlite
open Xunit

[<Fact>]
let ``isolated host starts serves stub routes and cleans its database`` () =
    let harness = TestHarness.Start()
    let root = harness.Root
    let database = harness.DatabasePath
    let address = harness.Client.BaseAddress
    try
        Assert.True(Directory.Exists(root))
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root))
        Assert.False(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Environment.CurrentDirectory) + string Path.DirectorySeparatorChar, StringComparison.Ordinal))
        Assert.True(File.Exists(database))
        Assert.Equal(root, harness.App.Configuration.["LexScholar:DataDirectory"])
        Assert.Equal(database, harness.App.Configuration.["LexScholar:DatabasePath"])
        Assert.True(address.IsLoopback)
        let marker = Path.Combine(root, "test-only.txt")
        File.WriteAllText(marker, "isolated")
        use connection = new SqliteConnection(SqliteConnectionStringBuilder(DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false).ToString())
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT 1"
        Assert.Equal(1L, command.ExecuteScalar() :?> int64)
        use response = harness.Client.GetAsync("/api/projects").GetAwaiter().GetResult()
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)
    finally
        (harness :> IDisposable).Dispose()
    Assert.False(Directory.Exists(root))
    use probe = new HttpClient(Timeout = TimeSpan.FromSeconds(2.0))
    Assert.ThrowsAny<HttpRequestException>(fun () -> probe.GetAsync(address).GetAwaiter().GetResult() |> ignore) |> ignore

[<Fact>]
let ``concurrent harnesses own distinct isolated environments`` () =
    use first = TestHarness.Start()
    use second = TestHarness.Start()
    Assert.True(first.Root <> second.Root)
    Assert.True(first.DatabasePath <> second.DatabasePath)
    Assert.NotEqual(first.Client.BaseAddress, second.Client.BaseAddress)

[<Fact>]
let ``all contract routes are reachable and report unimplemented`` () =
    use harness = TestHarness.Start()
    let project = Guid.NewGuid().ToString()
    let document = Guid.NewGuid().ToString()
    let routes =
        [ HttpMethod.Get, "/api/projects"
          HttpMethod.Post, "/api/projects"
          HttpMethod.Put, $"/api/projects/{project}/active"
          HttpMethod.Get, $"/api/projects/{project}/documents"
          HttpMethod.Post, $"/api/projects/{project}/documents"
          HttpMethod.Patch, $"/api/projects/{project}/documents/{document}"
          HttpMethod.Delete, $"/api/projects/{project}/documents/{document}"
          HttpMethod.Get, "/api/documents"
          HttpMethod.Post, "/api/documents"
          HttpMethod.Get, $"/api/documents/{document}/content" ]
    for method, path in routes do
        use request = new HttpRequestMessage(method, path)
        use response = harness.Client.Send(request)
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)
