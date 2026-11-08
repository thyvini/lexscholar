module LexScholar.Server.Program

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting.StaticWebAssets
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open LexScholar.Application
open LexScholar.Infrastructure

let create (builder: WebApplicationBuilder) (serveClient: bool) =
    builder.Services.AddSingleton<IProjectService, ProjectService>() |> ignore
    builder.Services.AddSingleton<ILibraryService, LibraryService>() |> ignore
    builder.Services.AddSingleton<IPdfValidator, PdfValidator>() |> ignore
    builder.Services.AddSingleton<IImportJournal, ImportJournal>() |> ignore
    builder.Services.AddSingleton<IVerifiedDocumentContent, VerifiedDocumentContent>() |> ignore
    let app = builder.Build()
#if DEBUG
    if serveClient then
        StaticWebAssetsLoader.UseStaticWebAssets(app.Environment, app.Configuration)
#endif
    if serveClient then
        app.MapStaticAssets() |> ignore
        app.MapGet("/", System.Func<IResult>(fun () -> Results.Redirect("/index.html"))) |> ignore
    LexScholar.Server.Endpoints.map app
    app

[<EntryPoint>]
let main args =
    let app = create (WebApplication.CreateBuilder(args)) true
    app.Run()
    0
