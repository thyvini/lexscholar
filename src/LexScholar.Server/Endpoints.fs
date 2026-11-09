namespace LexScholar.Server

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

module Endpoints =
    // Route behavior is deferred. Do not resolve or invoke application
    // capabilities until those tasks pass the required functions explicitly.
    let private notImplemented () : IResult =
        Results.Problem(title = "Not implemented", statusCode = StatusCodes.Status501NotImplemented)

    let map (app: WebApplication) directory =
        let stub = Func<IResult>(notImplemented)
        ProjectEndpoints.map app directory
        app.MapGet("/api/projects/{projectId}/documents", stub) |> ignore
        app.MapPost("/api/projects/{projectId}/documents", stub) |> ignore
        app.MapPatch("/api/projects/{projectId}/documents/{documentId}", stub) |> ignore
        app.MapDelete("/api/projects/{projectId}/documents/{documentId}", stub) |> ignore
        app.MapGet("/api/documents", stub) |> ignore
        app.MapPost("/api/documents", stub) |> ignore
        app.MapGet("/api/documents/{documentId}/content", stub) |> ignore
