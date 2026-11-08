module LexScholar.Server.Program

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting.StaticWebAssets
open Microsoft.AspNetCore.Http

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    let app = builder.Build()
#if DEBUG
    StaticWebAssetsLoader.UseStaticWebAssets(app.Environment, app.Configuration)
#endif
    app.MapStaticAssets() |> ignore
    app.MapGet("/", System.Func<IResult>(fun () -> Results.Redirect("/index.html"))) |> ignore
    app.Run()
    0
