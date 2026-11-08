module LexScholar.Client.Bootstrap.Program

[<EntryPoint>]
let main args =
    LexScholar.Client.Program.run args |> ignore
    0
