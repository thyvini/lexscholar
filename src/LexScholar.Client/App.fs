namespace LexScholar.Client

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open Bolero
open Bolero.Html
open Elmish
open Microsoft.AspNetCore.Components
open LexScholar.Client.Projects

module Api =
    let parseFailure (status: HttpStatusCode) (body: string) =
        try
            use json = JsonDocument.Parse(body)
            let mutable code = Unchecked.defaultof<JsonElement>
            if json.RootElement.TryGetProperty("code", &code) then
                match int status, code.GetString() with
                | 400, "invalid_name" -> InvalidName
                | 409, "duplicate_project_name" -> DuplicateName
                | 404, "not_found" -> NotFound
                | _ -> RequestFailed
            else RequestFailed
        with _ -> RequestFailed

    let load (http: HttpClient) = async {
        try
            use! response = http.GetAsync("api/projects") |> Async.AwaitTask
            if not response.IsSuccessStatusCode then return Error RequestFailed
            else
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                use json = JsonDocument.Parse(body)
                let root = json.RootElement
                let active = root.GetProperty("activeProjectId")
                let projects =
                    root.GetProperty("projects").EnumerateArray()
                    |> Seq.map (fun p -> { Id = p.GetProperty("id").GetGuid(); Name = p.GetProperty("name").GetString() })
                    |> Seq.toList
                return Ok { Projects = projects; Active = if active.ValueKind = JsonValueKind.Null then None else Some(active.GetGuid()) }
        with _ -> return Error RequestFailed }

    let mutate (http: HttpClient) (method: HttpMethod) (path: string) (body: string option) = async {
        try
            use! tokenResponse = http.GetAsync("api/antiforgery") |> Async.AwaitTask
            if not tokenResponse.IsSuccessStatusCode then return Error RequestFailed
            else
                let! tokenBody = tokenResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
                use tokenJson = JsonDocument.Parse(tokenBody)
                let token = tokenJson.RootElement.GetProperty("requestToken").GetString()
                use request = new HttpRequestMessage(method, path)
                request.Headers.Add("X-CSRF-TOKEN", token)
                body |> Option.iter (fun value -> request.Content <- new StringContent(value, Encoding.UTF8, "application/json"))
                use! response = http.SendAsync(request) |> Async.AwaitTask
                if response.IsSuccessStatusCode then return Ok ()
                else
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    return Error (parseFailure response.StatusCode body)
        with _ -> return Error RequestFailed }

module private Ui =
    let command http = function
        | Fetch -> Cmd.OfAsync.perform Api.load http Loaded
        | SendCreate name ->
            Cmd.OfAsync.perform (fun () -> Api.mutate http HttpMethod.Post "api/projects" (Some (JsonSerializer.Serialize({| name = name |})))) () Created
        | SendSelect id ->
            Cmd.OfAsync.perform (fun () -> Api.mutate http HttpMethod.Put $"api/projects/{id:D}/active" None) () Selected

    let update http msg model =
        let model, effect = Projects.update msg model
        model, effect |> Option.map (command http) |> Option.defaultValue Cmd.none

    let errorText = function
        | InvalidName -> "Enter a project name that is not blank."
        | DuplicateName -> "A project with this name already exists."
        | NotFound -> "That project is no longer available. Refresh the list and try again."
        | RequestFailed -> "The request failed. Please try again."

    let view model dispatch =
        let busy = model.Busy.IsSome
        div {
            attr.``class`` "container"
            h1 { "LexScholar" }
            p { attr.``class`` "subtitle"; "Research projects" }
            section {
                attr.``class`` "card"
                h2 { "Create a project" }
                form {
                    on.submit (fun _ -> dispatch Create)
                    "onsubmit:preventDefault" => true
                    label { "for" => "project-name"; "Project name" }
                    input {
                        attr.id "project-name"
                        attr.``type`` "text"
                        bind.input.string model.Name (dispatch << NameChanged)
                        attr.disabled busy
                    }
                    button { attr.``type`` "submit"; attr.disabled busy; text (if model.Busy = Some Creating then "Creating…" else "Create project") }
                }
                cond model.Error <| function
                    | Some error -> p { attr.``class`` "error"; "role" => "alert"; text (errorText error) }
                    | None -> empty ()
            }
            section {
                attr.``class`` "card"
                h2 { "Your projects" }
                cond model.Busy <| function
                    | Some Loading -> p { "role" => "status"; "Loading projects…" }
                    | Some Selecting -> p { "role" => "status"; "Selecting project…" }
                    | _ -> empty ()
                cond (model.HasLoaded && model.Snapshot.Projects.IsEmpty && model.Busy <> Some Loading) <| function
                    | true -> p { "No projects yet. Create one to get started." }
                    | false -> empty ()
                ul {
                    attr.``class`` "projects"
                    forEach model.Snapshot.Projects <| fun project ->
                        li {
                            span { text project.Name }
                            cond (model.Snapshot.Active = Some project.Id) <| function
                                | true -> strong { attr.``class`` "active"; "Active" }
                                | false -> button { attr.``type`` "button"; attr.disabled busy; on.click (fun _ -> dispatch (Select project.Id)); "Select" }
                        }
                }
                button { attr.``type`` "button"; attr.disabled busy; on.click (fun _ -> dispatch Reload); "Refresh" }
            }
        }

type App() =
    inherit ProgramComponent<Model, Message>()
    [<Inject>]
    member val Navigation = Unchecked.defaultof<NavigationManager> with get, set
    override this.Program =
        let http = new HttpClient(BaseAddress = Uri(this.Navigation.BaseUri))
        Program.mkProgram (fun _ -> { initial with Busy = Some Loading }, Ui.command http Fetch) (Ui.update http) Ui.view
