module LexScholar.Server.Program

open Microsoft.AspNetCore.Builder

[<EntryPoint>]
let main args =
    let app = WebApplication.CreateBuilder(args).Build()
    app.Run()
    0
