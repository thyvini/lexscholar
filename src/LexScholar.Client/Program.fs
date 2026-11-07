module LexScholar.Client.Program

open Microsoft.AspNetCore.Components.WebAssembly.Hosting

[<EntryPoint>]
let main args =
    WebAssemblyHostBuilder.CreateDefault(args).Build().RunAsync() |> ignore
    0
