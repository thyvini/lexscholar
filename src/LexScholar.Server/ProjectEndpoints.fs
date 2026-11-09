module LexScholar.Server.ProjectEndpoints

open System
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open LexScholar.Domain
open LexScholar.Application
open LexScholar.Infrastructure

let private dto (project: ResearchProject) =
    {| id = project.Id; name = project.Name; createdUtc = project.CreatedUtc |}

let private problem (status: int) code title : IResult =
    Results.Problem(statusCode = Nullable(status), title = title, extensions = dict [ "code", box code ])

let map (app: WebApplication) directory =
    let store : Projects.Store =
        { ResolveWorkspace = fun () -> ProjectRepository.resolveWorkspace directory
          List = ProjectRepository.list directory
          ActiveProject = ProjectRepository.activeProject directory
          Create = ProjectRepository.create directory
          Select = fun workspace project -> ProjectRepository.select directory workspace project |> Result.mapError (fun _ -> Projects.ProjectNotFound) }
    app.MapGet("/api/projects", Func<IResult>(fun () ->
        let active, projects = Projects.list store
        Results.Ok({| activeProjectId = Option.toNullable active; projects = projects |> List.map dto |}))) |> ignore
    app.MapPost("/api/projects", Func<HttpContext, System.Threading.Tasks.Task<IResult>>(fun context -> task {
        let! body = task {
            try
                let! json = JsonDocument.ParseAsync(context.Request.Body, cancellationToken = context.RequestAborted)
                use json = json
                let mutable value = Unchecked.defaultof<JsonElement>
                if json.RootElement.ValueKind = JsonValueKind.Object && json.RootElement.TryGetProperty("name", &value) && value.ValueKind = JsonValueKind.String then
                    return value.GetString()
                else return null
            with :? JsonException -> return null }
        match Projects.create store body with
        | Ok project -> return Results.Created($"/api/projects/{project.Id:D}", dto project)
        | Error (InvalidName _) -> return problem 400 "invalid_name" "Invalid project name"
        | Error DuplicateProjectName -> return problem 409 "duplicate_project_name" "Project name already exists" })) |> ignore
    app.MapPut("/api/projects/{projectId:guid}/active", Func<Guid, IResult>(fun projectId ->
        match Projects.select store projectId with
        | Ok () -> Results.NoContent()
        | Error Projects.ProjectNotFound -> problem 404 "not_found" "Project not found")) |> ignore
