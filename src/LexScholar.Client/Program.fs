module LexScholar.Client.Program

open Microsoft.AspNetCore.Components.WebAssembly.Hosting

[<EntryPoint>]
let main args =
    let builder = WebAssemblyHostBuilder.CreateDefault(args)
    builder.Build().RunAsync() |> ignore
    0
