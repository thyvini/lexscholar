namespace LexScholar.Server

open Microsoft.AspNetCore.Builder
open LexScholar.Application
open LexScholar.Infrastructure

module Endpoints =
    let map (app: WebApplication) (configuration: WorkspaceConfiguration) =
        ProjectEndpoints.map app configuration.DataDirectory
        LibraryEndpoints.map app configuration
        ReaderEndpoints.map app configuration
        let vault = ChatGptEndpoints.map app configuration
        let directory = configuration.DataDirectory
        // One registry for the process; the repository functions replace the stubs in T006.
        let jobs =
            SummaryJobs(
                { Find = fun _ -> None
                  Save = fun _ _ -> ()
                  Delete = ignore
                  AssociationExists =
                    fun key ->
                        match LibraryRepository.listForProject directory key.Project with
                        | Ok associations -> associations |> List.exists (fun (item, _) -> item.DocumentId = key.Document)
                        | Error _ -> false })
        SummaryEndpoints.map app configuration vault jobs
