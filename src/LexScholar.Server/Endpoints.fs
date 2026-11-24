namespace LexScholar.Server

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open LexScholar.Application
open LexScholar.Infrastructure

module Endpoints =
    let map (app: WebApplication) (configuration: WorkspaceConfiguration) =
        ProjectEndpoints.map app configuration.DataDirectory
        let directory = configuration.DataDirectory
        let coordination = app.Services.GetRequiredService<LibraryCoordination>()
        LibraryControlEndpoints.map app directory coordination
        // One registry for the process, over the stored summaries.
        let jobs =
            SummaryJobs(
                { Find = SummaryRepository.find directory
                  Save = fun key summary ->
                      LibraryDeletionRepository.unlessPending directory key.Document (fun () -> SummaryRepository.save directory key summary)
                  Delete = fun key ->
                      LibraryDeletionRepository.unlessPending directory key.Document (fun () -> SummaryRepository.delete directory key)
                  AssociationExists =
                    fun key ->
                        not (LibraryDeletionRepository.isPending directory key.Document)
                        && (match LibraryRepository.listForProject directory key.Project with
                            | Ok associations -> associations |> List.exists (fun (item, _) -> item.DocumentId = key.Document)
                            | Error _ -> false) }, coordination = coordination,
                isPending = LibraryDeletionRepository.isPending directory)
        LibraryEndpoints.map app configuration jobs coordination
        DocumentEndpoints.map app configuration coordination
        let vault = ChatGptEndpoints.map app configuration
        SummaryEndpoints.map app configuration vault jobs
