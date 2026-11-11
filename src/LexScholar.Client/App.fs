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
open Microsoft.AspNetCore.Components.Forms
open LexScholar.Domain
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

    // Library requests. Failures map through Library.parseFailure, which never
    // shows response details.

    let private libraryDocument (element: JsonElement) : Library.Document =
        { Id = element.GetProperty("id").GetGuid()
          FileName = element.GetProperty("originalFileName").GetString()
          PageCount = element.GetProperty("pageCount").GetInt32()
          ContentUrl = element.GetProperty("contentUrl").GetString() }

    let private role (value: string) =
        match value with
        | "FocusedSource" -> FocusedSource
        | "ReferenceSource" -> ReferenceSource
        | other -> failwithf "Unknown role %s" other

    let private roleName = function
        | FocusedSource -> "FocusedSource"
        | ReferenceSource -> "ReferenceSource"

    let private getArray (http: HttpClient) (path: string) (parse: JsonElement -> 'a) = async {
        try
            use! response = http.GetAsync(path) |> Async.AwaitTask
            let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            if not response.IsSuccessStatusCode then return Error (Library.parseFailure (int response.StatusCode) body)
            else
                use json = JsonDocument.Parse(body)
                return Ok (json.RootElement.EnumerateArray() |> Seq.map parse |> Seq.toList)
        with _ -> return Error Library.RequestFailed }

    /// Sends one unsafe request with a fresh antiforgery token; returns the
    /// success status and body.
    let private send (http: HttpClient) (method: HttpMethod) (path: string) (content: HttpContent option) = async {
        try
            use! tokenResponse = http.GetAsync("api/antiforgery") |> Async.AwaitTask
            if not tokenResponse.IsSuccessStatusCode then return Error Library.RequestFailed
            else
                let! tokenBody = tokenResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
                use tokenJson = JsonDocument.Parse(tokenBody)
                use request = new HttpRequestMessage(method, path)
                request.Headers.Add("X-CSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString())
                content |> Option.iter (fun body -> request.Content <- body)
                use! response = http.SendAsync(request) |> Async.AwaitTask
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                if response.IsSuccessStatusCode then return Ok (response.StatusCode, body)
                else return Error (Library.parseFailure (int response.StatusCode) body)
        with _ -> return Error Library.RequestFailed }

    let libraryDocuments (http: HttpClient) = getArray http "api/documents" libraryDocument

    let projectDocuments (http: HttpClient) (projectId: Guid) =
        getArray http $"api/projects/{projectId:D}/documents" (fun item ->
            { Library.Association.Document = libraryDocument (item.GetProperty("document"))
              Library.Association.Role = role (item.GetProperty("role").GetString()) })

    let upload (http: HttpClient) (file: IBrowserFile) = async {
        try
            // The server enforces the configured byte limit, so the client does not cap the upload.
            use stream = file.OpenReadStream(Int64.MaxValue)
            use content = new MultipartFormDataContent()
            let part = new StreamContent(stream)
            part.Headers.ContentType <- Headers.MediaTypeHeaderValue("application/pdf")
            content.Add(part, "file", file.Name)
            match! send http HttpMethod.Post "api/documents" (Some (content :> HttpContent)) with
            | Error error -> return Error error
            | Ok (status, body) ->
                use json = JsonDocument.Parse(body)
                let document = libraryDocument json.RootElement
                return Ok (if status = HttpStatusCode.Created then Library.Imported document else Library.Reused document)
        with _ -> return Error Library.RequestFailed }

    let associate (http: HttpClient) (projectId: Guid) (documentId: Guid) (documentRole: DocumentRole) = async {
        let body = JsonSerializer.Serialize({| documentId = documentId; role = roleName documentRole |})
        let content = new StringContent(body, Encoding.UTF8, "application/json") :> HttpContent
        let! result = send http HttpMethod.Post $"api/projects/{projectId:D}/documents" (Some content)
        return result |> Result.map ignore }

    let removeAssociation (http: HttpClient) (projectId: Guid) (documentId: Guid) = async {
        let! result = send http HttpMethod.Delete $"api/projects/{projectId:D}/documents/{documentId:D}" None
        return result |> Result.map ignore }

/// The whole page: projects and the shared library.
type State = { Projects: Model; Library: Library.Model }

type Msg =
    | ProjectsMsg of Message
    | LibraryMsg of Library.Message

module private Ui =
    let command http = function
        | Fetch -> Cmd.OfAsync.perform Api.load http Loaded
        | SendCreate name ->
            Cmd.OfAsync.perform (fun () -> Api.mutate http HttpMethod.Post "api/projects" (Some (JsonSerializer.Serialize({| name = name |})))) () Created
        | SendSelect id ->
            Cmd.OfAsync.perform (fun () -> Api.mutate http HttpMethod.Put $"api/projects/{id:D}/active" None) () Selected

    let libraryCommand http effect : Cmd<Msg> =
        let perform work toMessage = Cmd.OfAsync.perform work () (toMessage >> LibraryMsg)
        let fetchLibrary request =
            perform (fun () -> Api.libraryDocuments http) (fun result -> Library.LibraryLoaded (request, result))
        let fetchProject projectId request =
            perform (fun () -> Api.projectDocuments http projectId) (fun result -> Library.ProjectLoaded (projectId, request, result))
        match effect with
        | Library.FetchLibrary request -> fetchLibrary request
        | Library.FetchProject (projectId, request) -> fetchProject projectId request
        | Library.FetchAll (library, project) ->
            Cmd.batch [ fetchLibrary library
                        match project with
                        | Some (projectId, request) -> fetchProject projectId request
                        | None -> Cmd.none ]
        | Library.SendUpload file -> perform (fun () -> Api.upload http file) Library.Uploaded
        | Library.SendAssociate (projectId, documentId, role) ->
            perform (fun () -> Api.associate http projectId documentId role) (fun result -> Library.Associated (projectId, result))
        | Library.SendRemove (projectId, documentId) ->
            perform (fun () -> Api.removeAssociation http projectId documentId) (fun result -> Library.Removed (projectId, result))

    let private updateLibrary http message state =
        let library, effect = Library.update message state.Library
        { state with Library = library }, effect |> Option.map (libraryCommand http) |> Option.defaultValue Cmd.none

    let update http msg state =
        match msg with
        | ProjectsMsg message ->
            let projects, effect = Projects.update message state.Projects
            let state = { state with Projects = projects }
            let projectCommand = effect |> Option.map (command http >> Cmd.map ProjectsMsg) |> Option.defaultValue Cmd.none
            // The page's Refresh button also reloads both library lists, so a
            // failed load can be retried.
            let state, projectCommand =
                match message with
                | Reload ->
                    let state, libraryCommand = updateLibrary http Library.Refresh state
                    state, Cmd.batch [ projectCommand; libraryCommand ]
                | _ -> state, projectCommand
            // Follow the active project once the project list has loaded.
            if projects.HasLoaded && projects.Snapshot.Active <> state.Library.Project then
                let state, libraryCommand = updateLibrary http (Library.ActiveProjectChanged projects.Snapshot.Active) state
                state, Cmd.batch [ projectCommand; libraryCommand ]
            else state, projectCommand
        | LibraryMsg message -> updateLibrary http message state

    let errorText = function
        | InvalidName -> "Enter a project name that is not blank."
        | DuplicateName -> "A project with this name already exists."
        | NotFound -> "That project is no longer available. Refresh the list and try again."
        | RequestFailed -> "The request failed. Please try again."

    let libraryErrorText = function
        | Library.NotAPdf -> "That file is not a PDF. Choose a PDF file."
        | Library.CorruptPdf -> "That PDF is damaged or cannot be read."
        | Library.EncryptedPdf -> "Encrypted or password-protected PDFs cannot be imported."
        | Library.TooLarge -> "That PDF is larger than the configured size limit."
        | Library.TooManyPages -> "That PDF has more pages than the configured page limit."
        | Library.DuplicateAssociation -> "That document is already in this project."
        | Library.InvalidRole -> "Choose Focused Source or Reference Source."
        | Library.NotFound -> "That project or document is no longer available. Refresh and try again."
        | Library.DocumentNotFound -> "That document is no longer in the library. Refresh and try again."
        | Library.RequestFailed -> "The request failed. Please try again."

    let roleLabel = function
        | FocusedSource -> "Focused Source"
        | ReferenceSource -> "Reference Source"

    let pages count = if count = 1 then "1 page" else $"{count} pages"

    let libraryView (library: Library.Model) dispatch =
        let busy = library.Busy.IsSome
        let associated = library.Associations |> List.map (fun item -> item.Document.Id) |> Set.ofList
        concat {
            section {
                attr.``class`` "card"
                h2 { "Shared library" }
                label { "for" => "library-file"; "Import a PDF" }
                comp<InputFile> {
                    "id" => "library-file"
                    "accept" => ".pdf,application/pdf"
                    "disabled" => busy
                    attr.callback "OnChange" (fun (event: InputFileChangeEventArgs) -> dispatch (Library.Upload event.File))
                }
                cond library.Busy <| function
                    | Some Library.Uploading -> p { "role" => "status"; "Importing…" }
                    | _ -> empty ()
                cond library.Notice <| function
                    | Some (Library.Imported document) -> p { "role" => "status"; text $"Imported {document.FileName}." }
                    | Some (Library.Reused document) ->
                        p { "role" => "status"; text $"Already in the library: {document.FileName}. The existing document was reused." }
                    | None -> empty ()
                cond library.Error <| function
                    | Some error -> p { attr.``class`` "error"; "role" => "alert"; text (libraryErrorText error) }
                    | None -> empty ()
                cond (library.HasLoadedLibrary && library.Documents.IsEmpty) <| function
                    | true -> p { "No documents in the library yet." }
                    | false -> empty ()
                ul {
                    attr.``class`` "documents"
                    forEach library.Documents <| fun document ->
                        li {
                            span { text document.FileName; " "; small { text $"({pages document.PageCount})" } }
                            // Bolero's cond needs a union or bool, so the checks are nested.
                            cond library.Project <| function
                                | None -> empty ()
                                | Some _ ->
                                    cond (associated.Contains document.Id) <| function
                                        | true -> small { "In this project" }
                                        | false ->
                                            span {
                                                attr.``class`` "actions"
                                                forEach [ FocusedSource; ReferenceSource ] <| fun role ->
                                                    button {
                                                        attr.``type`` "button"
                                                        attr.disabled busy
                                                        on.click (fun _ -> dispatch (Library.Associate (document.Id, role)))
                                                        text $"Add as {roleLabel role}"
                                                    }
                                            }
                        }
                }
            }
            section {
                attr.``class`` "card"
                h2 { "Project documents" }
                cond library.Project <| function
                    | None -> p { "Select a project to see its documents." }
                    | Some _ ->
                        concat {
                            cond library.LoadingProject <| function
                                | true -> p { "role" => "status"; "Loading project documents…" }
                                | false -> empty ()
                            cond (library.HasLoadedProject && library.Associations.IsEmpty) <| function
                                | true -> p { "No documents are associated with this project." }
                                | false -> empty ()
                            ul {
                                attr.``class`` "project-documents"
                                forEach library.Associations <| fun association ->
                                    li {
                                        span { text association.Document.FileName }
                                        strong { attr.``class`` "role"; text (roleLabel association.Role) }
                                        button {
                                            attr.``type`` "button"
                                            attr.disabled busy
                                            on.click (fun _ -> dispatch (Library.Remove association.Document.Id))
                                            "Remove"
                                        }
                                    }
                            }
                        }
            }
        }

    let view (state: State) dispatch =
        let model = state.Projects
        let busy = model.Busy.IsSome
        let dispatch' = dispatch
        let dispatch = ProjectsMsg >> dispatch'
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
            libraryView state.Library (LibraryMsg >> dispatch')
        }

type App() =
    inherit ProgramComponent<State, Msg>()
    [<Inject>]
    member val Navigation = Unchecked.defaultof<NavigationManager> with get, set
    override this.Program =
        let http = new HttpClient(BaseAddress = Uri(this.Navigation.BaseUri))
        let start _ =
            let library, libraryEffect = Library.update Library.LoadLibrary Library.initial
            { Projects = { initial with Busy = Some Loading }; Library = library },
            Cmd.batch [ Ui.command http Fetch |> Cmd.map ProjectsMsg
                        libraryEffect |> Option.map (Ui.libraryCommand http) |> Option.defaultValue Cmd.none ]
        Program.mkProgram start (Ui.update http) Ui.view
