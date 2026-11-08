namespace LexScholar.Server

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

module Endpoints =
    let private notImplemented () : IResult =
        Results.Problem(title = "Not implemented", statusCode = StatusCodes.Status501NotImplemented)

    let map (app: WebApplication) =
        let stub = Func<IResult>(notImplemented)
        app.MapGet("/api/projects", stub) |> ignore
        app.MapPost("/api/projects", stub) |> ignore
        app.MapPut("/api/projects/{projectId}/active", stub) |> ignore
        app.MapGet("/api/projects/{projectId}/documents", stub) |> ignore
        app.MapPost("/api/projects/{projectId}/documents", stub) |> ignore
        app.MapPatch("/api/projects/{projectId}/documents/{documentId}", stub) |> ignore
        app.MapDelete("/api/projects/{projectId}/documents/{documentId}", stub) |> ignore
        app.MapGet("/api/documents", stub) |> ignore
        app.MapPost("/api/documents", stub) |> ignore
        app.MapGet("/api/documents/{documentId}/content", stub) |> ignore
