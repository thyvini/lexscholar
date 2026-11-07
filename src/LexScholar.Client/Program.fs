module LexScholar.Client.Program

open Microsoft.AspNetCore.Components.WebAssembly.Hosting

let run args =
    WebAssemblyHostBuilder.CreateDefault(args).Build().RunAsync()
