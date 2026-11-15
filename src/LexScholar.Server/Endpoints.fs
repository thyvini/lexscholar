namespace LexScholar.Server

open Microsoft.AspNetCore.Builder
open LexScholar.Application
open LexScholar.Infrastructure

module Endpoints =
    let map (app: WebApplication) (configuration: WorkspaceConfiguration) =
        ProjectEndpoints.map app configuration.DataDirectory
        let directory = configuration.DataDirectory
        // One registry for the process, over the stored summaries.
        let jobs =
            SummaryJobs(
                { Find = SummaryRepository.find directory
                  Save = SummaryRepository.save directory
                  Delete = SummaryRepository.delete directory
                  AssociationExists =
                    fun key ->
                        match LibraryRepository.listForProject directory key.Project with
                        | Ok associations -> associations |> List.exists (fun (item, _) -> item.DocumentId = key.Document)
                        | Error _ -> false })
        LibraryEndpoints.map app configuration jobs
        ReaderEndpoints.map app configuration
        let vault = ChatGptEndpoints.map app configuration
        SummaryEndpoints.map app configuration vault jobs
