namespace LexScholar.Server

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

module Endpoints =
    // Route behavior is deferred. Do not resolve or invoke application
    // capabilities until those tasks pass the required functions explicitly.
    let private notImplemented () : IResult =
        Results.Problem(title = "Not implemented", statusCode = StatusCodes.Status501NotImplemented)

    let map (app: WebApplication) (configuration: WorkspaceConfiguration) =
        let stub = Func<IResult>(notImplemented)
        ProjectEndpoints.map app configuration.DataDirectory
        LibraryEndpoints.map app configuration
        app.MapPatch("/api/projects/{projectId}/documents/{documentId}", stub) |> ignore
        app.MapGet("/api/documents/{documentId}/content", stub) |> ignore
