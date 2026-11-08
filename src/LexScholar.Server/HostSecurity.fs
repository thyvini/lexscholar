namespace LexScholar.Server

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Threading.Tasks
open Microsoft.AspNetCore.Antiforgery
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.AspNetCore.StaticAssets
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection

module HostSecurity =
    [<Literal>]
    let TokenHeader = "X-CSRF-TOKEN"

    let configure (builder: WebApplicationBuilder) port =
        // Default hosting can enable forwarded headers from environment settings.
        // This host does not run behind a trusted proxy.
        let settings = Dictionary<string, string>()
        settings.Add("ForwardedHeaders_Enabled", "false")
        // MapStaticAssets otherwise installs a runtime fallback for unlisted files
        // in build-manifest mode, and can rewrite compressed asset responses.
        settings.Add("ReloadStaticAssetsAtRuntime", "false")
        settings.Add("DisableStaticAssetNotFoundRuntimeFallback", "true")
        builder.Configuration.AddInMemoryCollection(settings) |> ignore
        builder.Services.Configure<ForwardedHeadersOptions>(fun (options: ForwardedHeadersOptions) ->
            options.ForwardedHeaders <- ForwardedHeaders.None) |> ignore
        builder.WebHost.ConfigureKestrel(Action<KestrelServerOptions>(fun options ->
            // Configuration-backed Kestrel endpoints are additive to code-backed
            // endpoints. Drop the loader so hostile Kestrel:Endpoints cannot bind.
            options.ConfigurationLoader <- null
            options.Listen(IPAddress.Loopback, port))) |> ignore
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider() |> ignore
        builder.Services.AddAntiforgery(fun options ->
            options.HeaderName <- TokenHeader
            options.SuppressReadingTokenFromFormBody <- true
            options.Cookie.Name <- ".LexScholar.Antiforgery"
            options.Cookie.HttpOnly <- true
            options.Cookie.SameSite <- SameSiteMode.Strict
            options.Cookie.SecurePolicy <- CookieSecurePolicy.None) |> ignore

    let private validHost (context: HttpContext) =
        let host = context.Request.Host
        let port = context.Connection.LocalPort
        let expected name =
            String.Equals(host.Value, $"{name}:{port}", StringComparison.OrdinalIgnoreCase)
            || (port = 80 && String.Equals(host.Value, name, StringComparison.OrdinalIgnoreCase))
        context.Request.Headers.Host.Count = 1
        && not (isNull context.Connection.LocalIpAddress)
        && IPAddress.IsLoopback(context.Connection.LocalIpAddress)
        && String.Equals(context.Request.Scheme, "http", StringComparison.OrdinalIgnoreCase)
        && (expected "localhost" || expected "127.0.0.1")

    let private validOrigin (context: HttpContext) =
        let origins = context.Request.Headers.Origin
        let host = context.Request.Host
        let matches (value: string) =
            String.Equals(value, $"http://{host.Value}", StringComparison.OrdinalIgnoreCase)
            || (context.Connection.LocalPort = 80 &&
                (String.Equals(value, $"http://{host.Host}", StringComparison.OrdinalIgnoreCase)
                 || String.Equals(value, $"http://{host.Host}:80", StringComparison.OrdinalIgnoreCase)))
        origins.Count = 0 ||
        (origins.Count = 1 && matches origins.[0])

    // Resolve every existing component, not just a final symlink. Static assets
    // and the already-locked data directory exist by the time this is called.
    let private resolvedPath (path: string) =
        try
            let rec resolve remaining (candidate: string) =
                if remaining = 0 then raise (IOException("Too many symbolic links."))
                let full = Path.GetFullPath(candidate)
                let root = Path.GetPathRoot(full)
                let parts = full.Substring(root.Length).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                let mutable current = root
                for part in parts do
                    current <- Path.Combine(current, part)
                    let attributes = File.GetAttributes(current)
                    if attributes.HasFlag(FileAttributes.ReparsePoint) then
                        let info: FileSystemInfo =
                            if attributes.HasFlag(FileAttributes.Directory) then DirectoryInfo(current) :> FileSystemInfo
                            else FileInfo(current) :> FileSystemInfo
                        let target = info.ResolveLinkTarget(true)
                        if isNull target then raise (IOException("Unable to resolve symbolic link."))
                        // The target can itself contain symlinks in its parent path.
                        current <- resolve (remaining - 1) target.FullName
                Path.TrimEndingDirectorySeparator(current)
            Some (resolve 40 path)
        with
        | :? IOException
        | :? UnauthorizedAccessException
        | :? System.Security.SecurityException
        | :? ArgumentException
        | :? NotSupportedException -> None

    let private safeStaticAsset (app: WebApplication) dataDirectory (context: HttpContext) =
        let endpoint = context.GetEndpoint()
        let asset = if isNull endpoint then null else endpoint.Metadata.GetMetadata<StaticAssetDescriptor>()
        if isNull asset then true
        else
            match resolvedPath dataDirectory with
            | None -> false
            | Some data ->
                let paths =
                    if asset.AssetPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                       || asset.AssetPath.EndsWith(".br", StringComparison.OrdinalIgnoreCase) then
                        // Build-time compressed copies may live outside the
                        // original asset's directory, but carry its contents.
                        [ asset.AssetPath; asset.AssetPath.Substring(0, asset.AssetPath.Length - 3) ]
                    else [ asset.AssetPath ]
                paths |> List.forall (fun path ->
                    let file = app.Environment.WebRootFileProvider.GetFileInfo(path)
                    match if file.Exists && not (isNull file.PhysicalPath) then resolvedPath file.PhysicalPath else None with
                    | Some actual ->
                        actual <> data && not (actual.StartsWith(data + string Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    | None -> false)

    let useSecurity (app: WebApplication) dataDirectory =
        app.UseExceptionHandler(Action<IApplicationBuilder>(fun branch ->
            branch.Run(RequestDelegate(fun context ->
                Errors.problem context 500 "An unexpected error occurred." "internal_error")))) |> ignore

        app.Use(Func<HttpContext, RequestDelegate, Task>(fun context next ->
            task {
                // Security preconditions use 403, keeping documented domain
                // 400 problem codes distinct from host/origin/CSRF failures.
                if not (validHost context) then
                    do! Errors.problem context 403 "Invalid host." "invalid_host"
                elif not (validOrigin context) then
                    do! Errors.problem context 403 "Origin is not allowed." "invalid_origin"
                else
                    do! next.Invoke(context)
            } :> Task)) |> ignore

        app.UseRouting() |> ignore
        app.Use(Func<HttpContext, RequestDelegate, Task>(fun context next ->
            if safeStaticAsset app dataDirectory context then next.Invoke(context)
            else Errors.problem context 404 "Resource not found." "not_found")) |> ignore
        app.UseAntiforgery() |> ignore
        let antiforgery = app.Services.GetRequiredService<IAntiforgery>()
        app.Use(Func<HttpContext, RequestDelegate, Task>(fun context next ->
            task {
                let method = context.Request.Method
                if HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) then
                    do! next.Invoke(context)
                elif HttpMethods.IsTrace(method) then
                    do! Errors.problem context 405 "Method is not allowed." "method_not_allowed"
                else
                    let! valid = task {
                        try
                            do! antiforgery.ValidateRequestAsync(context)
                            return true
                        with :? AntiforgeryValidationException -> return false
                    }
                    if valid then
                        do! next.Invoke(context)
                    else
                        do! Errors.problem context 403 "A valid antiforgery token is required." "invalid_antiforgery_token"
            } :> Task)) |> ignore

        app.MapGet("/api/antiforgery", Func<HttpContext, IResult>(fun context ->
            let tokens = antiforgery.GetAndStoreTokens(context)
            context.Response.Headers.CacheControl <- "no-store"
            Results.Json({| requestToken = tokens.RequestToken |}))) |> ignore
