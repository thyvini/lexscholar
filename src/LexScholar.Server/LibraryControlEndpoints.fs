namespace LexScholar.Server

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open LexScholar.Application
open LexScholar.Infrastructure

module LibraryControlEndpoints =
    /// Not called by production composition until T008.
    let mapForTests (app: WebApplication) (directory: string) (coordination: LibraryCoordination) =
        app.MapPost("/api/library/export", Func<HttpContext, System.Threading.Tasks.Task<IResult>>(fun context -> task {
            let! result = LexScholar.Application.LibraryExport.create coordination (LibraryExport.build directory) context.RequestAborted
            return
                match result with
                | Ok stream -> Results.File(stream, "application/zip", "lexscholar-library.zip")
                | Error failure ->
                    let status, code, title =
                        match failure with
                        | LexScholar.Application.LibraryExport.Failure.EmptyLibrary -> 409, "empty_library", "Não há documentos para exportar."
                        | LexScholar.Application.LibraryExport.Failure.DocumentUnavailable _ -> 409, "document_unavailable", "Documento indisponível."
                        | LexScholar.Application.LibraryExport.Failure.ExportFailed -> 500, "export_failed", "Não foi possível exportar a biblioteca."
                    Results.Problem(statusCode = status, title = title, extensions = dict [ "code", box code ])
        })) |> ignore
