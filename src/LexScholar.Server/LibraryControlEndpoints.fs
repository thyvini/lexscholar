namespace LexScholar.Server

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open LexScholar.Application
open LexScholar.Infrastructure

module LibraryControlEndpoints =
    let private mapWithBuild order (app: WebApplication) (coordination: LibraryCoordination) build =
        app.MapPost("/api/library/export", Func<HttpContext, System.Threading.Tasks.Task<IResult>>(fun context -> task {
            let! result = LexScholar.Application.LibraryExport.create coordination build context.RequestAborted
            return
                match result with
                | Ok stream -> Results.File(stream, "application/zip", "lexscholar-library.zip")
                | Error failure ->
                    let status, code, title =
                        match failure with
                        | LexScholar.Application.LibraryExport.Failure.EmptyLibrary -> 409, "empty_library", "Não há documentos para exportar."
                        | LexScholar.Application.LibraryExport.Failure.DocumentUnavailable _ -> 409, "document_unavailable", "Documento indisponível."
                        | LexScholar.Application.LibraryExport.Failure.ExportFailed -> 500, "export_failed", "Não foi possível exportar a biblioteca."
                    let extensions =
                        [ "code", box code
                          match failure with
                          | LexScholar.Application.LibraryExport.Failure.DocumentUnavailable (id, name) ->
                              "documentId", box id
                              "name", box name
                          | _ -> () ]
                    Results.Problem(statusCode = status, title = title, extensions = dict extensions)
        })).WithOrder(order) |> ignore

    /// Explicit test overrides take precedence over the production route.
    let mapWithBuildForTests app coordination build =
        mapWithBuild -1 app coordination build

    let mapForTests app directory coordination =
        mapWithBuildForTests app coordination (LibraryExport.build directory)

    let map app directory coordination =
        mapWithBuild 0 app coordination (LibraryExport.build directory)
