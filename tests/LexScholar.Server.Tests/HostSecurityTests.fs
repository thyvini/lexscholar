module LexScholar.Server.Tests.HostSecurityTests

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Net.NetworkInformation
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open LexScholar.Server
open LexScholar.Server.Tests.ProcessHarness
open Xunit

let private origin (client: HttpClient) = client.BaseAddress.GetLeftPart(UriPartial.Authority)

let private send (client: HttpClient) (method: HttpMethod) (path: string) (hostHeader: string option) (originHeader: string option) (token: string option) (content: HttpContent option) =
    use request = new HttpRequestMessage(method, path)
    hostHeader |> Option.iter (fun value -> request.Headers.Host <- value)
    originHeader |> Option.iter (fun value -> request.Headers.TryAddWithoutValidation("Origin", value) |> ignore)
    token |> Option.iter (fun value -> request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", value) |> ignore)
    content |> Option.iter (fun value -> request.Content <- value)
    client.Send(request)

let private getToken (client: HttpClient) =
    use response = client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    Assert.True(response.StatusCode = HttpStatusCode.OK, $"Expected same-origin antiforgery token endpoint; got {response.StatusCode}.")
    let body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    use json = JsonDocument.Parse(body)
    json.RootElement.GetProperty("requestToken").GetString()

let private assertNoNonLoopbackConnection port =
    let nonLoopback =
        NetworkInterface.GetAllNetworkInterfaces()
        |> Seq.collect (fun nic -> nic.GetIPProperties().UnicastAddresses |> Seq.cast<UnicastIPAddressInformation>)
        |> Seq.map (fun entry -> entry.Address)
        |> Seq.tryFind (fun candidate -> candidate.AddressFamily = AddressFamily.InterNetwork && not (IPAddress.IsLoopback(candidate)))
    match nonLoopback with
    | Some candidate ->
        use socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        let attempt = Record.Exception(Action(fun () -> socket.ConnectAsync(candidate, port).WaitAsync(TimeSpan.FromSeconds(3.0)).GetAwaiter().GetResult()))
        Assert.NotNull(attempt)
    | None -> ()

[<Fact; Trait("Task", "T009")>]
let ``forged host and foreign origin are rejected including port mismatches`` () =
    use harness = TestHarness.Start()
    let client = harness.Client
    let port = client.BaseAddress.Port
    for host in [ "evil.example:" + string port; "localhost:" + string (if port = 65535 then port - 1 else port + 1); "127.0.0.1.evil.example:" + string port ] do
        use response = send client HttpMethod.Get "/api/projects" (Some host) None None None
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)
    for supplied in [ "http://evil.example"; "null"; "http://127.0.0.1:" + string (if port = 65535 then port - 1 else port + 1) ] do
        use response = send client HttpMethod.Get "/api/projects" None (Some supplied) None None
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)
    use sameOrigin = send client HttpMethod.Get "/api/projects" None (Some (origin client)) None None
    Assert.Equal(HttpStatusCode.NotImplemented, sameOrigin.StatusCode)
    let localhost = $"localhost:{port}"
    use localhostResponse = send client HttpMethod.Get "/api/projects" (Some localhost) (Some $"http://{localhost}") None None
    Assert.Equal(HttpStatusCode.NotImplemented, localhostResponse.StatusCode)
    use aliasOrigin = send client HttpMethod.Get "/api/projects" (Some localhost) (Some (origin client)) None None
    Assert.Equal(HttpStatusCode.Forbidden, aliasOrigin.StatusCode)
    for supplied in [ $"{origin client}, http://evil.example"; $"{origin client}/path"; $"https://127.0.0.1:{port}" ] do
        use response = send client HttpMethod.Get "/api/projects" None (Some supplied) None None
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)

[<Fact; Trait("Task", "T009")>]
let ``forwarded headers cannot replace the actual host or allow foreign origins`` () =
    use harness = TestHarness.Start(settings = [ "ForwardedHeaders_Enabled", "true" ])
    let client = harness.Client
    use forged = new HttpRequestMessage(HttpMethod.Get, "/api/projects")
    forged.Headers.Host <- $"evil.example:{client.BaseAddress.Port}"
    forged.Headers.TryAddWithoutValidation("X-Forwarded-Host", client.BaseAddress.Authority) |> ignore
    forged.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http") |> ignore
    use rejected = client.Send(forged)
    Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode)
    use forwarded = new HttpRequestMessage(HttpMethod.Get, "/api/projects")
    forwarded.Headers.TryAddWithoutValidation("X-Forwarded-Host", "evil.example") |> ignore
    forwarded.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https") |> ignore
    forwarded.Headers.TryAddWithoutValidation("Forwarded", "host=evil.example;proto=https") |> ignore
    use ignored = client.Send(forwarded)
    Assert.Equal(HttpStatusCode.NotImplemented, ignored.StatusCode)
    use preflight = new HttpRequestMessage(HttpMethod.Options, "/api/projects")
    preflight.Headers.TryAddWithoutValidation("Origin", "http://evil.example") |> ignore
    preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST") |> ignore
    use noCors = client.Send(preflight)
    Assert.Equal(HttpStatusCode.Forbidden, noCors.StatusCode)
    Assert.False(noCors.Headers.Contains("Access-Control-Allow-Origin"))

[<Theory; Trait("Task", "T009")>]
[<InlineData("application/json")>]
[<InlineData("multipart/form-data")>]
[<InlineData("text/plain")>]
[<InlineData("application/x-www-form-urlencoded")>]
let ``mutation requests without tokens cannot reach JSON multipart or simple handlers`` contentType =
    use harness = TestHarness.Start()
    use content = new StringContent("test", Encoding.UTF8)
    content.Headers.ContentType <- System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType)
    use response = send harness.Client HttpMethod.Post "/api/documents" None (Some (origin harness.Client)) None (Some content)
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)

[<Fact; Trait("Task", "T009")>]
let ``same-origin token and cookie permit each contract mutation`` () =
    use harness = TestHarness.Start()
    let client = harness.Client
    let token = getToken client
    Assert.False(String.IsNullOrWhiteSpace(token))
    let id = Guid.NewGuid().ToString()
    let mutations =
        [ HttpMethod.Post, "/api/projects", "application/json"
          HttpMethod.Put, $"/api/projects/{id}/active", "text/plain"
          HttpMethod.Post, $"/api/projects/{id}/documents", "application/json"
          HttpMethod.Patch, $"/api/projects/{id}/documents/{id}", "application/json"
          HttpMethod.Delete, $"/api/projects/{id}/documents/{id}", "text/plain"
          HttpMethod.Post, "/api/documents", "multipart/form-data" ]
    for method, path, contentType in mutations do
        use content = new StringContent("test", Encoding.UTF8)
        content.Headers.ContentType <- System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType)
        use response = send client method path None (Some (origin client)) (Some token) (Some content)
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)

[<Fact; Trait("Task", "T009")>]
let ``invalid tokens cookie mismatches and foreign origin are rejected`` () =
    use harness = TestHarness.Start()
    let client = harness.Client
    let token = getToken client
    use bad = send client HttpMethod.Post "/api/projects" None (Some (origin client)) (Some "invalid") None
    Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode)
    use anonymous = new HttpClient(BaseAddress = client.BaseAddress)
    use missingCookie = send anonymous HttpMethod.Post "/api/projects" None (Some (origin client)) (Some token) None
    Assert.Equal(HttpStatusCode.Forbidden, missingCookie.StatusCode)
    use foreign = send client HttpMethod.Post "/api/projects" None (Some "http://evil.example") (Some token) None
    Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode)
    use foreignToken = send client HttpMethod.Get "/api/antiforgery" None (Some "http://evil.example") None None
    Assert.Equal(HttpStatusCode.Forbidden, foreignToken.StatusCode)

[<Fact; Trait("Task", "T009")>]
let ``independent hosts do not overwrite each other's antiforgery cookies`` () =
    use contentRoot = TemporaryWorkspace.Create()
    use workspaceA = TemporaryWorkspace.Create()
    use workspaceB = TemporaryWorkspace.Create()
    Assert.True(workspaceA.Root <> workspaceB.Root)
    // The hosts share a working/content root but configure different data roots.
    use hostA = HostProcess.Start(contentRoot.Root, [ "LexScholar:DataDirectory", workspaceA.Root ])
    use hostB = HostProcess.Start(contentRoot.Root, [ "LexScholar:DataDirectory", workspaceB.Root ])
    Assert.Equal("ready", hostA.Startup.Kind)
    Assert.Equal("ready", hostB.Startup.Kind)
    Assert.NotEqual(hostA.Id, hostB.Id)
    let addressA = Uri(hostA.Startup.Address)
    let addressB = Uri(hostB.Startup.Address)
    Assert.Equal("127.0.0.1", addressA.Host)
    Assert.Equal(addressA.Host, addressB.Host)
    Assert.NotEqual(0, addressA.Port)
    Assert.NotEqual(0, addressB.Port)
    Assert.NotEqual(addressA.Port, addressB.Port)

    // CookieContainer, like a browser, shares host cookies across ports.
    let cookies = CookieContainer()
    use handlerA = new HttpClientHandler(UseProxy = false, CookieContainer = cookies)
    use handlerB = new HttpClientHandler(UseProxy = false, CookieContainer = cookies)
    use clientA = new HttpClient(handlerA, false, BaseAddress = addressA, Timeout = TimeSpan.FromSeconds(10.0))
    use clientB = new HttpClient(handlerB, false, BaseAddress = addressB, Timeout = TimeSpan.FromSeconds(10.0))
    let assertAllowed (client: HttpClient) token =
        use response = send client HttpMethod.Post "/api/projects" None (Some (origin client)) (Some token) None
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)
    let assertRejected (client: HttpClient) token =
        use response = send client HttpMethod.Post "/api/projects" None (Some (origin client)) (Some token) None
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)
        use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
        Assert.Equal("invalid_antiforgery_token", json.RootElement.GetProperty("code").GetString())

    let tokenA = getToken clientA
    Assert.False(String.IsNullOrWhiteSpace(tokenA))
    assertAllowed clientA tokenA
    let tokenB = getToken clientB
    Assert.False(String.IsNullOrWhiteSpace(tokenB))
    assertAllowed clientB tokenB
    assertAllowed clientA tokenA
    assertAllowed clientB tokenB
    Assert.Equal(2, cookies.GetCookies(addressA).Count)
    Assert.Equal(2, cookies.GetCookies(addressB).Count)
    assertRejected clientA tokenB
    assertRejected clientB tokenA
    assertAllowed clientA tokenA
    assertAllowed clientB tokenB

[<Fact; Trait("Task", "T009")>]
let ``restarting a workspace replaces its stale antiforgery cookie`` () =
    use workspace = TemporaryWorkspace.Create()
    let cookies = CookieContainer()
    use handler = new HttpClientHandler(UseProxy = false, CookieContainer = cookies)
    let oldToken, cookieName =
        use first = HostProcess.Start(workspace.Root, [])
        Assert.Equal("ready", first.Startup.Kind)
        let address = Uri(first.Startup.Address)
        use client = new HttpClient(handler, false, BaseAddress = address, Timeout = TimeSpan.FromSeconds(10.0))
        let token = getToken client
        use response = send client HttpMethod.Post "/api/projects" None (Some (origin client)) (Some token) None
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)
        let stored = cookies.GetCookies(address)
        Assert.Equal(1, stored.Count)
        token, stored.[0].Name

    // A trailing separator denotes the same directory, even after a restart.
    use restarted = HostProcess.Start(workspace.Root, [ "LexScholar:DataDirectory", workspace.Root + string Path.DirectorySeparatorChar ])
    Assert.Equal("ready", restarted.Startup.Kind)
    let address = Uri(restarted.Startup.Address)
    use client = new HttpClient(handler, false, BaseAddress = address, Timeout = TimeSpan.FromSeconds(10.0))
    let assertRejected token =
        use response = send client HttpMethod.Post "/api/projects" None (Some (origin client)) (Some token) None
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)
        use json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
        Assert.Equal("invalid_antiforgery_token", json.RootElement.GetProperty("code").GetString())
    assertRejected oldToken
    let newToken = getToken client
    let stored = cookies.GetCookies(address)
    Assert.Equal(1, stored.Count)
    Assert.Equal(cookieName, stored.[0].Name)
    assertRejected oldToken
    use response = send client HttpMethod.Post "/api/projects" None (Some (origin client)) (Some newToken) None
    Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)

[<Fact; Trait("Task", "T009")>]
let ``all mutation methods require tokens even with an empty body`` () =
    use harness = TestHarness.Start()
    let id = Guid.NewGuid().ToString()
    for method, path in [ HttpMethod.Post, "/api/projects"; HttpMethod.Put, $"/api/projects/{id}/active"; HttpMethod.Patch, $"/api/projects/{id}/documents/{id}"; HttpMethod.Delete, $"/api/projects/{id}/documents/{id}" ] do
        use response = send harness.Client method path None None None None
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)

[<Fact; Trait("Task", "T009")>]
let ``valid tokens preserve JSON and multipart request bodies`` () =
    let setup (app: WebApplication) =
        app.MapPost("/api/test-json", Func<HttpContext, Task<IResult>>(fun context ->
            task {
                use! json = JsonDocument.ParseAsync(context.Request.Body)
                return Results.Text(json.RootElement.GetProperty("name").GetString())
            })) |> ignore
        app.MapPost("/api/test-multipart", Func<HttpContext, Task<IResult>>(fun context ->
            task {
                let! form = context.Request.ReadFormAsync()
                use reader = new StreamReader(form.Files.[0].OpenReadStream())
                let! content = reader.ReadToEndAsync()
                return Results.Text(content)
            })) |> ignore
    use harness = TestHarness.Start(configure = setup)
    let token = getToken harness.Client
    use jsonContent = new StringContent("{\"name\":\"JSON body\"}", Encoding.UTF8, "application/json")
    use jsonResponse = send harness.Client HttpMethod.Post "/api/test-json" None (Some (origin harness.Client)) (Some token) (Some jsonContent)
    Assert.Equal(HttpStatusCode.OK, jsonResponse.StatusCode)
    Assert.Equal("JSON body", jsonResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    use content = new MultipartFormDataContent()
    content.Add(new StringContent("test PDF bytes"), "file", "test.pdf")
    use response = send harness.Client HttpMethod.Post "/api/test-multipart" None (Some (origin harness.Client)) (Some token) (Some content)
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Equal("test PDF bytes", response.Content.ReadAsStringAsync().GetAwaiter().GetResult())

[<Fact; Trait("Task", "T009")>]
let ``security errors use safe problem JSON without echoing untrusted input`` () =
    use harness = TestHarness.Start()
    let untrusted = "http://evil.example/private/path"
    use response = send harness.Client HttpMethod.Get "/api/projects" None (Some untrusted) None None
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType.MediaType)
    let body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    use json = JsonDocument.Parse(body)
    Assert.Equal(403, json.RootElement.GetProperty("status").GetInt32())
    Assert.Equal("invalid_origin", json.RootElement.GetProperty("code").GetString())
    Assert.False(String.IsNullOrWhiteSpace(json.RootElement.GetProperty("title").GetString()))
    Assert.DoesNotContain(untrusted, body)

[<Fact; Trait("Task", "T009")>]
let ``token cookie is HttpOnly Strict and response cannot be cached`` () =
    use harness = TestHarness.Start()
    use response = harness.Client.GetAsync("/api/antiforgery").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let cookie = response.Headers.GetValues("Set-Cookie") |> Seq.exactlyOne
    Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase)
    Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase)
    Assert.Contains("no-store", response.Headers.CacheControl.ToString())

[<Fact; Trait("Task", "T009")>]
let ``document GET needs no antiforgery token and accepts ranges`` () =
    use harness = TestHarness.Start()
    use request = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{Guid.NewGuid()}/content")
    request.Headers.TryAddWithoutValidation("Range", "bytes=0-7") |> ignore
    use response = harness.Client.Send(request)
    Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)

[<Theory; Trait("Task", "T009")>]
[<InlineData("Testing")>]
[<InlineData("Development")>]
let ``unexpected endpoint exceptions return safe problem JSON`` environment =
    let privatePath = "/secret/workspace/staging/private.pdf"
    let setup (app: WebApplication) =
        app.MapGet("/api/test-failure", Func<IResult>(fun () -> failwith $"sensitive {privatePath}")) |> ignore
    use harness = TestHarness.Start(configure = setup, environment = environment)
    use response = harness.Client.GetAsync("/api/test-failure").GetAwaiter().GetResult()
    let body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode)
    Assert.NotNull(response.Content.Headers.ContentType)
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType.MediaType)
    Assert.DoesNotContain(privatePath, body)
    Assert.DoesNotContain("sensitive", body)
    Assert.DoesNotContain("StackTrace", body)
    use json = JsonDocument.Parse(body)
    Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32())
    Assert.Equal("internal_error", json.RootElement.GetProperty("code").GetString())

[<Fact; Trait("Task", "T009")>]
let ``Kestrel configuration cannot open a wildcard network listener`` () =
    use harness = TestHarness.Start(settings = [ "Kestrel:Endpoints:Untrusted:Url", "http://0.0.0.0:0" ])
    let addresses = harness.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses
    for value in addresses do
        let address = Uri(value)
        let ip = IPAddress.Parse(address.Host)
        Assert.True(IPAddress.IsLoopback(ip), $"The actual Kestrel listener is not loopback: {value}")
        assertNoNonLoopbackConnection address.Port

[<Fact; Trait("Task", "T009")>]
let ``configured loopback ports are honored`` () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    use harness = TestHarness.Start(settings = [ "LexScholar:Port", string port; "urls", "http://0.0.0.0:0" ])
    Assert.Equal(port, harness.Client.BaseAddress.Port)
    use response = harness.Client.GetAsync("/api/projects").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)
    assertNoNonLoopbackConnection port

[<Theory; Trait("Task", "T009")>]
[<InlineData("not-a-port")>]
[<InlineData("-1")>]
[<InlineData("65536")>]
let ``invalid ports fail with typed diagnostics before locking`` value =
    use workspace = TemporaryWorkspace.Create()
    use host = HostProcess.Start(workspace.Root, [ "LexScholar:Port", value ])
    Assert.Equal("rejected", host.Startup.Kind)
    Assert.Equal("InvalidPort", host.Startup.ErrorType)
    Assert.Contains("LexScholar:Port", host.Startup.Message)
    Assert.False(File.Exists(Path.Combine(workspace.Root, ".lexscholar.lock")))

[<Theory; Trait("Task", "T009")>]
[<InlineData("environment")>]
[<InlineData("command-line")>]
let ``real server ignores wildcard environment and command-line listeners`` source =
    use workspace = TemporaryWorkspace.Create()
    let info = ProcessStartInfo("dotnet", UseShellExecute = false, RedirectStandardOutput = true,
                                RedirectStandardError = true, WorkingDirectory = workspace.Root)
    info.ArgumentList.Add(typeof<WorkspaceConfiguration>.Assembly.Location)
    info.Environment.["LexScholar__DataDirectory"] <- workspace.Root
    info.Environment.["LexScholar__Port"] <- "0"
    info.Environment.["ASPNETCORE_ENVIRONMENT"] <- "Production"
    info.Environment.["DOTNET_ENVIRONMENT"] <- "Production"
    info.Environment.["Logging__LogLevel__Default"] <- "Information"
    info.Environment.["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] <- "true"
    if source = "environment" then
        info.Environment.["ASPNETCORE_URLS"] <- "http://0.0.0.0:0"
        info.Environment.["Kestrel__Endpoints__Untrusted__Url"] <- "http://0.0.0.0:0"
    else
        info.ArgumentList.Add("--urls")
        info.ArgumentList.Add("http://0.0.0.0:0")
        info.ArgumentList.Add("--Kestrel:Endpoints:Untrusted:Url")
        info.ArgumentList.Add("http://0.0.0.0:0")
    use child = new Process(StartInfo = info)
    Assert.True(child.Start())
    let errors = child.StandardError.ReadToEndAsync()
    try
        let output = StringBuilder()
        let mutable address: Uri option = None
        while address.IsNone && not child.HasExited do
            let line = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10.0)).GetAwaiter().GetResult()
            if isNull line then failwith $"Server exited before listening: {output} {errors.GetAwaiter().GetResult()}"
            output.AppendLine(line) |> ignore
            let marker = "Now listening on: "
            let position = line.IndexOf(marker, StringComparison.Ordinal)
            if position >= 0 then address <- Some (Uri(line.Substring(position + marker.Length).Trim()))
        Assert.True(address.IsSome, $"Server exited before listening: {output}")
        let actual = address.Value
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(actual.Host)), $"Public listener: {actual}")
        Assert.NotEqual(0, actual.Port)
        use client = new HttpClient(BaseAddress = actual)
        use response = client.GetAsync("/api/projects").GetAwaiter().GetResult()
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode)
        use index = client.GetAsync("/index.html").GetAwaiter().GetResult()
        Assert.Equal(HttpStatusCode.OK, index.StatusCode)
        use script = client.GetAsync("/_framework/blazor.webassembly.js").GetAwaiter().GetResult()
        Assert.Equal(HttpStatusCode.OK, script.StatusCode)
        assertNoNonLoopbackConnection actual.Port
    finally
        if not child.HasExited then child.Kill(entireProcessTree = true)
        Assert.True(child.WaitForExit(10_000), "Server process did not exit.")

[<Fact; Trait("Task", "T009")>]
let ``Bolero static assets load and workspace data is not served`` () =
    use harness = TestHarness.Start(serveClient = true)
    let secretName = "workspace.sqlite"
    use index = harness.Client.GetAsync("/index.html").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, index.StatusCode)
    let html = index.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert.Contains("_framework/blazor.webassembly.js", html)
    Assert.DoesNotContain("http://", html)
    Assert.DoesNotContain("https://", html)
    use script = harness.Client.GetAsync("/_framework/blazor.webassembly.js").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, script.StatusCode)
    use compressedRequest = new HttpRequestMessage(HttpMethod.Get, "/index.html")
    compressedRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip") |> ignore
    use compressed = harness.Client.Send(compressedRequest)
    Assert.Equal(HttpStatusCode.OK, compressed.StatusCode)
    Assert.Contains("gzip", compressed.Content.Headers.ContentEncoding)
    use secret = harness.Client.GetAsync("/" + secretName).GetAwaiter().GetResult()
    Assert.NotEqual(HttpStatusCode.OK, secret.StatusCode)

[<Fact; Trait("Task", "T009")>]
let ``runtime files are not served even if public and private roots overlap`` () =
    let configure (app: WebApplication) =
        app.Environment.WebRootFileProvider <- new PhysicalFileProvider(app.Environment.ContentRootPath)
        File.WriteAllText(Path.Combine(app.Environment.ContentRootPath, "private.pdf"), "private PDF marker")
        Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "staging")) |> ignore
        File.WriteAllText(Path.Combine(app.Environment.ContentRootPath, "staging", "draft.txt"), "private staging marker")
    use harness = TestHarness.Start(serveClient = true, configure = configure, settings = [ "ReloadStaticAssetsAtRuntime", "true" ])
    Assert.Equal("false", harness.App.Configuration.["ReloadStaticAssetsAtRuntime"])
    for path in [ "/workspace.sqlite"; "/private.pdf"; "/staging/draft.txt" ] do
        use response = harness.Client.GetAsync(path).GetAwaiter().GetResult()
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode)

[<Theory; Trait("Task", "T009")>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``manifest assets cannot resolve through a symlink into the data directory`` linkThroughDirectory =
    use publicRoot = TemporaryWorkspace.Create()
    let configure (app: WebApplication) =
        let privateFile = Path.Combine(app.Environment.ContentRootPath, "private.txt")
        // Match the built index asset's length so the pre-fix server completes
        // the response instead of failing on its manifest Content-Length.
        let assetLength = int (app.Environment.WebRootFileProvider.GetFileInfo("index.html").Length)
        File.WriteAllText(privateFile, "private marker".PadRight(assetLength, '!'))
        let target =
            if linkThroughDirectory then
                let alias = Path.Combine(publicRoot.Root, "alias")
                Directory.CreateSymbolicLink(alias, app.Environment.ContentRootPath) |> ignore
                Path.Combine(alias, "private.txt")
            else privateFile
        File.CreateSymbolicLink(Path.Combine(publicRoot.Root, "index.html"), target) |> ignore
        app.Environment.WebRootFileProvider <- new PhysicalFileProvider(publicRoot.Root)
    use harness = TestHarness.Start(serveClient = true, configure = configure)
    use response = harness.Client.GetAsync("/index.html").GetAwaiter().GetResult()
    let body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
    Assert.DoesNotContain("private marker", body)

[<Fact; Trait("Task", "T009")>]
let ``compressed variants cannot expose a private original asset`` () =
    use publicRoot = TemporaryWorkspace.Create()
    let configure (app: WebApplication) =
        let originalLength = int (app.Environment.WebRootFileProvider.GetFileInfo("index.html").Length)
        let compressedLength = int (app.Environment.WebRootFileProvider.GetFileInfo("index.html.gz").Length)
        let privateFile = Path.Combine(app.Environment.ContentRootPath, "private.txt")
        let secret = "private marker".PadRight(originalLength, '!')
        File.WriteAllText(privateFile, secret)
        File.CreateSymbolicLink(Path.Combine(publicRoot.Root, "index.html"), privateFile) |> ignore
        use buffer = new MemoryStream()
        do
            use gzip = new GZipStream(buffer, CompressionMode.Compress, true)
            let bytes = Encoding.UTF8.GetBytes(secret)
            gzip.Write(bytes, 0, bytes.Length)
        let mutable compressed = buffer.ToArray()
        Assert.True(compressed.Length <= compressedLength)
        // The manifest advertises the build-time length; pad the synthetic
        // variant so an unguarded response can complete with those headers.
        Array.Resize(&compressed, compressedLength)
        File.WriteAllBytes(Path.Combine(publicRoot.Root, "index.html.gz"), compressed)
        app.Environment.WebRootFileProvider <- new PhysicalFileProvider(publicRoot.Root)
    use harness = TestHarness.Start(serveClient = true, configure = configure)
    for path in [ "/index.html"; "/index.html.gz" ] do
        use request = new HttpRequestMessage(HttpMethod.Get, path)
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip") |> ignore
        use response = harness.Client.Send(request)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)

[<Fact; Trait("Task", "T009")>]
let ``a manifest asset physically inside the data directory is not served`` () =
    let configure (app: WebApplication) =
        File.WriteAllText(Path.Combine(app.Environment.ContentRootPath, "index.html"), "private marker")
        app.Environment.WebRootFileProvider <- new PhysicalFileProvider(app.Environment.ContentRootPath)
    use harness = TestHarness.Start(serveClient = true, configure = configure)
    use response = harness.Client.GetAsync("/index.html").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)

[<Fact; Trait("Task", "T009")>]
let ``a symlinked data directory still protects its real asset paths`` () =
    use workspace = TemporaryWorkspace.Create()
    let realData = Path.Combine(workspace.Root, "private")
    let alias = Path.Combine(workspace.Root, "data-link")
    Directory.CreateDirectory(realData) |> ignore
    Directory.CreateSymbolicLink(alias, realData) |> ignore
    File.WriteAllText(Path.Combine(realData, "index.html"), "private marker")
    let builder = HostSetup.builder workspace.Root [ "LexScholar:DataDirectory", alias ]
    builder.WebHost.UseStaticWebAssets() |> ignore
    let app =
        match Program.create builder true with
        | Ok host -> host
        | Error error -> failwithf "Test host unexpectedly rejected: %A" error
    use app = app
    app.Environment.WebRootFileProvider <- new PhysicalFileProvider(realData)
    try
        app.StartAsync().GetAwaiter().GetResult()
        let server = app.Services.GetRequiredService<IServer>()
        let address = server.Features.Get<IServerAddressesFeature>().Addresses |> Seq.exactlyOne
        use client = new HttpClient(BaseAddress = Uri(address))
        use response = client.GetAsync("/index.html").GetAwaiter().GetResult()
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
    finally
        app.StopAsync().GetAwaiter().GetResult()
