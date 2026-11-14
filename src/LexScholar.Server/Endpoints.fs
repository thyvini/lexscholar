namespace LexScholar.Server

open Microsoft.AspNetCore.Builder

module Endpoints =
    let map (app: WebApplication) (configuration: WorkspaceConfiguration) =
        ProjectEndpoints.map app configuration.DataDirectory
        LibraryEndpoints.map app configuration
        ReaderEndpoints.map app configuration
        let vault = ChatGptEndpoints.map app configuration
        SummaryEndpoints.map app configuration vault
