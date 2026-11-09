module LexScholar.Client.Program

open Microsoft.AspNetCore.Components.WebAssembly.Hosting

[<EntryPoint>]
let main args =
    let builder = WebAssemblyHostBuilder.CreateDefault(args)
    builder.RootComponents.Add<LexScholar.Client.App>("#app")
    builder.Build().RunAsync() |> ignore
    0
