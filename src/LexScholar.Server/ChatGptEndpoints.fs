/// Routes of the ChatGPT sign-in (F005, contracts/chatgpt-api.yaml). Shared pieces
/// only: destinations answers signed out; the sign-in routes answer 501 until T004/T005.
module LexScholar.Server.ChatGptEndpoints

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open LexScholar.Domain

let private notImplemented () : IResult = Results.StatusCode 501

let private destinations (configuration: WorkspaceConfiguration) : IResult =
    let configured : obj =
        match configuration.Summary with
        | None -> null
        | Some summary ->
            box {| serviceHost = summary.Endpoint.Authority
                   model = summary.Model
                   local = (ServiceLocation.classify summary.Endpoint.Host = Local) |}
    Results.Json({| chatgpt = {| signedIn = false |}; configured = configured; welcomePending = false |}, System.Text.Json.JsonSerializerOptions.Web)

let map (app: WebApplication) (configuration: WorkspaceConfiguration) =
    app.MapGet("/api/summary/destinations", Func<IResult>(fun () -> destinations configuration)) |> ignore
    app.MapPost("/api/chatgpt/sign-in", Func<IResult>(notImplemented)) |> ignore
    app.MapGet("/auth/callback", Func<IResult>(notImplemented)) |> ignore
    app.MapPost("/api/chatgpt/sign-out", Func<IResult>(notImplemented)) |> ignore
    app.MapPost("/api/chatgpt/welcome/dismiss", Func<IResult>(notImplemented)) |> ignore
