module LexScholar.Server.Program

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    builder.WebHost.UseStaticWebAssets() |> ignore
    let app = builder.Build()
    app.MapStaticAssets() |> ignore
    app.MapGet("/", System.Func<IResult>(fun () -> Results.Redirect("/index.html"))) |> ignore
    app.Run()
    0
