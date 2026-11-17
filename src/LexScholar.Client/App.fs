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
open Microsoft.AspNetCore.Components.WebAssembly.Http
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

    /// Check availability without downloading the entire PDF before the native viewer opens it.
    let checkContent (http: HttpClient) (url: string) = async {
        try
            use request = new HttpRequestMessage(HttpMethod.Get, url)
            request.Headers.Range <- Headers.RangeHeaderValue(Nullable 0L, Nullable 0L)
            use! response = http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead) |> Async.AwaitTask
            if response.IsSuccessStatusCode then return Ok ()
            else
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                return Error (Reader.parseFailure (int response.StatusCode) body)
        with _ -> return Error Reader.RequestFailed }

    let changeRole (http: HttpClient) (projectId: Guid) (documentId: Guid) documentRole = async {
        let body = JsonSerializer.Serialize({| role = roleName documentRole |})
        let content = new StringContent(body, Encoding.UTF8, "application/json") :> HttpContent
        let! result = send http HttpMethod.Patch $"api/projects/{projectId:D}/documents/{documentId:D}" (Some content)
        return result |> Result.map ignore |> Result.mapError (function
            | Library.NotFound -> Reader.AssociationNotFound
            | Library.InvalidRole -> Reader.InvalidRole
            | _ -> Reader.RequestFailed) }

    /// Requests a summary. The server answers with the job's state (202, or 200 when one exists); nothing is streamed and
    /// leaving the document does not abort the request. Failures never show response details.
    /// The state of the pair's summary job: None when the server has nothing (404); Error when it could not be asked.
    let summaryState (http: HttpClient) (projectId: Guid) (documentId: Guid) : Async<Result<Summary.Remote option, unit>> = async {
        try
            use! response = http.GetAsync($"api/projects/{projectId:D}/documents/{documentId:D}/summary") |> Async.AwaitTask
            if response.StatusCode = HttpStatusCode.NotFound then return Ok None
            elif not response.IsSuccessStatusCode then return Error ()
            else
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                match SummaryStatus.parseRemote body with
                | Some remote -> return Ok (Some remote)
                | None -> return Error ()
        with _ -> return Error () }

    /// The status of every summary, for the list markers; Error when it could not be read.
    let summaryStatuses (http: HttpClient) : Async<Result<SummaryStatus.StatusEntry list, unit>> = async {
        try
            use! response = http.GetAsync("api/summaries") |> Async.AwaitTask
            if not response.IsSuccessStatusCode then return Error ()
            else
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                match SummaryStatus.parseStatuses body with
                | Some entries -> return Ok entries
                | None -> return Error ()
        with _ -> return Error () }

    /// Deletes the pair's finished or failed summary on the server (idempotent); false when it could not be deleted.
    let dismissSummary (http: HttpClient) (projectId: Guid) (documentId: Guid) : Async<bool> = async {
        try
            use! tokenResponse = http.GetAsync("api/antiforgery") |> Async.AwaitTask
            if not tokenResponse.IsSuccessStatusCode then return false
            else
                let! tokenBody = tokenResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
                use tokenJson = JsonDocument.Parse(tokenBody)
                use request = new HttpRequestMessage(HttpMethod.Delete, $"api/projects/{projectId:D}/documents/{documentId:D}/summary")
                request.Headers.Add("X-CSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString())
                use! response = http.SendAsync request |> Async.AwaitTask
                return response.IsSuccessStatusCode
        with _ -> return false }

    let summarize (http: HttpClient) (projectId: Guid) (documentId: Guid) (destination: Summary.Destination option) (confirmed: Summary.Confirmed option) (cancellation: Threading.CancellationToken) : Async<Result<Summary.Remote, Summary.Failure>> = async {
        try
            use! tokenResponse = http.GetAsync("api/antiforgery", cancellation) |> Async.AwaitTask
            if not tokenResponse.IsSuccessStatusCode then return Error Summary.RequestFailed
            else
                let! tokenBody = tokenResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
                use tokenJson = JsonDocument.Parse(tokenBody)
                use request = new HttpRequestMessage(HttpMethod.Post, $"api/projects/{projectId:D}/documents/{documentId:D}/summary")
                request.Headers.Add("X-CSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString())
                request.Content <- new StringContent(SummaryStatus.requestBody destination confirmed, Encoding.UTF8, "application/json")
                use! response = http.SendAsync(request, cancellation) |> Async.AwaitTask
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                if not response.IsSuccessStatusCode then return Error (SummaryStatus.failureOfBody body)
                else
                    // 202, or 200 when the pair already has a summary: the job's state.
                    match SummaryStatus.parseRemote body with
                    | Some remote -> return Ok remote
                    | None -> return Error Summary.RequestFailed
        with _ -> return Error Summary.RequestFailed }

module ChatGptApi =
    let private json (body: string) = JsonDocument.Parse(body)

    let destinations (http: HttpClient) = async {
        try
            use! response = http.GetAsync("api/summary/destinations") |> Async.AwaitTask
            if not response.IsSuccessStatusCode then return Error ()
            else
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                use document = json body
                let root = document.RootElement
                let chatgpt = root.GetProperty("chatgpt")
                let account =
                    if chatgpt.GetProperty("signedIn").GetBoolean() then
                        let mutable name = Unchecked.defaultof<JsonElement>
                        let mutable confirmed = Unchecked.defaultof<JsonElement>
                        Some { ChatGpt.Name = (if chatgpt.TryGetProperty("account", &name) then name.GetString() else "your ChatGPT account")
                               ChatGpt.Confirmed = chatgpt.TryGetProperty("confirmed", &confirmed) && confirmed.GetBoolean() }
                    else None
                let configured =
                    match root.TryGetProperty "configured" with
                    | true, value when value.ValueKind = JsonValueKind.Object ->
                        Some { ChatGpt.Host = value.GetProperty("serviceHost").GetString()
                               ChatGpt.Model = value.GetProperty("model").GetString()
                               ChatGpt.Local = value.GetProperty("local").GetBoolean() }
                    | _ -> None
                return Ok { ChatGpt.Account = account; ChatGpt.Configured = configured; ChatGpt.WelcomePending = root.GetProperty("welcomePending").GetBoolean() }
        with _ -> return Error () }

    /// Unsafe request with a fresh antiforgery token; the status and body, or None when it could not be sent.
    let private post (http: HttpClient) (path: string) = async {
        try
            use! tokenResponse = http.GetAsync("api/antiforgery") |> Async.AwaitTask
            if not tokenResponse.IsSuccessStatusCode then return None
            else
                let! tokenBody = tokenResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
                use tokenJson = json tokenBody
                use request = new HttpRequestMessage(HttpMethod.Post, path)
                request.Headers.Add("X-CSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString())
                use! response = http.SendAsync(request) |> Async.AwaitTask
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                return Some (int response.StatusCode, body)
        with _ -> return None }

    let startSignIn (http: HttpClient) = async {
        match! post http "api/chatgpt/sign-in" with
        | Some (200, body) ->
            try
                use document = json body
                return Ok (document.RootElement.GetProperty("authorizationUrl").GetString())
            with _ -> return Error ChatGpt.RequestFailed
        | Some (409, _) -> return Error ChatGpt.AlreadySignedIn
        | _ -> return Error ChatGpt.RequestFailed }

    let signOut (http: HttpClient) = async {
        match! post http "api/chatgpt/sign-out" with
        | Some (200, body) ->
            try
                use document = json body
                return Ok (document.RootElement.GetProperty("revoked").GetBoolean())
            with _ -> return Error ()
        | _ -> return Error () }

    let dismissWelcome (http: HttpClient) = async {
        let! _ = post http "api/chatgpt/welcome/dismiss"
        return () }

/// The whole page: projects, shared library, native reader and its summary.
type State = { Projects: Model; Library: Library.Model; Reader: Reader.Model; Summary: Summary.Model; ChatGpt: ChatGpt.Model; Statuses: SummaryStatus.Statuses; ShowLibrary: bool }

type Msg =
    | ShowLibrary
    | ProjectsMsg of Message
    | LibraryMsg of Library.Message
    | ReaderMsg of Reader.Message
    | SummaryMsg of Summary.Message
    | ChatGptMsg of ChatGpt.Message
    | StatusMsg of SummaryStatus.StatusMessage

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
            perform (fun () -> Api.removeAssociation http projectId documentId) (fun result -> Library.Removed (projectId, documentId, result))

    let private updateLibrary http message state =
        let library, effect = Library.update message state.Library
        { state with Library = library; Reader = Reader.followLibrary library state.Reader }, effect |> Option.map (libraryCommand http) |> Option.defaultValue Cmd.none

    let private updateReader http message state =
        let reader, effect = Reader.update message state.Reader
        let showLibrary = match message with Reader.Open _ -> false | _ -> state.ShowLibrary
        let state = { state with Reader = reader; ShowLibrary = showLibrary }
        match effect with
        | Some Reader.RefreshProject -> updateLibrary http Library.ReloadProject state
        | Some (Reader.CheckContent (request, url)) ->
            state, Cmd.OfAsync.perform (Api.checkContent http) url (fun result -> ReaderMsg (Reader.ContentChecked (request, result)))
        | Some (Reader.SendRole (projectId, documentId, role)) ->
            state, Cmd.OfAsync.perform (fun () -> Api.changeRole http projectId documentId role) () (fun result -> ReaderMsg (Reader.RoleChanged (projectId, result)))
        | None -> state, Cmd.none

    /// How often the open document's running summary is asked for (research §4).
    let private summaryPollMilliseconds = 2000

    let private summaryCommand http (effect: Summary.Effect option) : Cmd<Msg> =
        match effect with
        | Some (Summary.Send (request, projectId, documentId, destination, confirmed)) ->
            Cmd.ofEffect (fun dispatch ->
                Async.StartImmediate(async {
                    match! Api.summarize http projectId documentId destination confirmed Threading.CancellationToken.None with
                    | Ok remote -> dispatch (SummaryMsg (Summary.Accepted (request, projectId, documentId, remote)))
                    | Error failure -> dispatch (SummaryMsg (Summary.Responded (request, Error failure))) }))
        | Some (Summary.Fetch (watch, projectId, documentId, delay)) ->
            Cmd.ofEffect (fun dispatch ->
                Async.StartImmediate(async {
                    // While polling, a failed fetch keeps waiting; the first fetch on return shows nothing then.
                    if delay then do! Async.Sleep summaryPollMilliseconds
                    let! remote = Api.summaryState http projectId documentId
                    let remote = match remote with Ok state -> state | Error () -> (if delay then Some (Summary.RemoteRunning None) else None)
                    dispatch (SummaryMsg (Summary.Polled (watch, remote))) }))
        | Some (Summary.DeleteSummary (projectId, documentId)) ->
            Cmd.ofEffect (fun dispatch ->
                Async.StartImmediate(async {
                    let! succeeded = Api.dismissSummary http projectId documentId
                    dispatch (SummaryMsg (Summary.Dismissed (projectId, documentId, succeeded))) }))
        | None -> Cmd.none

    /// Reads the status list, after the polling delay when the effect says so (a loop that runs while any summary does).
    let statusCommand http (effect: SummaryStatus.StatusEffect option) : Cmd<Msg> =
        match effect with
        | Some (SummaryStatus.FetchStatuses (watch, delay)) ->
            Cmd.ofEffect (fun dispatch ->
                Async.StartImmediate(async {
                    if delay then do! Async.Sleep summaryPollMilliseconds
                    let! result = Api.summaryStatuses http
                    dispatch (StatusMsg (SummaryStatus.StatusesLoaded (watch, result))) }))
        | None -> Cmd.none

    let chatGptCommand http (navigation: NavigationManager) (effect: ChatGpt.Effect option) : Cmd<Msg> =
        let toMessage message = ChatGptMsg message
        match effect with
        | Some (ChatGpt.FetchDestinations request) ->
            Cmd.OfAsync.perform ChatGptApi.destinations http (fun result -> toMessage (ChatGpt.Loaded (request, result)))
        | Some ChatGpt.StartSignIn -> Cmd.OfAsync.perform ChatGptApi.startSignIn http (ChatGpt.AuthorizationReady >> toMessage)
        | Some (ChatGpt.Navigate url) ->
            Cmd.ofEffect (fun _ -> navigation.NavigateTo(url, forceLoad = true))
        | Some ChatGpt.SendSignOut -> Cmd.OfAsync.perform ChatGptApi.signOut http (ChatGpt.SignedOutResult >> toMessage)
        | Some ChatGpt.SendDismissWelcome -> Cmd.ofEffect (fun _ -> ChatGptApi.dismissWelcome http |> Async.StartImmediate)
        | None -> Cmd.none

    let private updateCore http navigation msg state =
        match msg with
        | ShowLibrary -> { state with ShowLibrary = true }, Cmd.none
        | ChatGptMsg message ->
            let chatGpt, effect = ChatGpt.update message state.ChatGpt
            { state with ChatGpt = chatGpt }, chatGptCommand http navigation effect
        | StatusMsg message ->
            let statuses, effect = SummaryStatus.updateStatuses message state.Statuses
            { state with Statuses = statuses }, statusCommand http effect
        | SummaryMsg message ->
            let summary, effect = Summary.update message state.Summary
            // A failure that ended the sign-in reloads the destinations: whether it came as a problem on the POST, or as the
            // failed state of a background job (the POST's own answer, or a later poll).
            let reload =
                match message with
                | Summary.Responded (request, Error failure) when state.Summary.State = Summary.Generating request && Summary.reloadsDestinations failure -> true
                | Summary.Accepted (_, _, _, Summary.RemoteFailed failure)
                | Summary.Polled (_, Some (Summary.RemoteFailed failure)) ->
                    summary.State = Summary.Failed failure && state.Summary.State <> summary.State && Summary.reloadsDestinations failure
                | _ -> false
            let chatGpt, chatGptEffect = if reload then ChatGpt.update ChatGpt.Load state.ChatGpt else state.ChatGpt, None
            { state with Summary = summary; ChatGpt = chatGpt },
            Cmd.batch [ summaryCommand http effect; chatGptCommand http navigation chatGptEffect ]
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
        | ReaderMsg message -> updateReader http message state

    /// The summary follows the reader's project and open document after every message.
    let update http navigation msg state =
        let viewOf (state: State) = state.Library.Project, state.Reader.Open |> Option.map (fun document -> document.Id)
        let before = viewOf state
        let state, command = updateCore http navigation msg state
        // The markers are read again on every project or document view change (AR 4) and once a summary was asked for or
        // dismissed (its POST or DELETE has been answered) and once a document was removed from the project.
        let statuses, statusEffect =
            match msg with
            // A removal from the project deletes the document's stored summary on the server; a POST whose answer failed may still have
            // started the job.
            | SummaryMsg (Summary.Accepted _ | Summary.Responded _ | Summary.Dismissed _) | LibraryMsg (Library.Removed _) -> SummaryStatus.updateStatuses SummaryStatus.Refresh state.Statuses
            | _ -> SummaryStatus.followView before (viewOf state) state.Statuses
        let state = { state with Statuses = statuses }
        let command = Cmd.batch [ command; statusCommand http statusEffect ]
        // The summary knows which destinations exist from the sign-in state and the configured service.
        let available =
            { Summary.Plan = (match state.ChatGpt.Status with ChatGpt.SignedIn _ -> true | _ -> false)
              Summary.Configured = state.ChatGpt.Configured.IsSome }
        let summary, _ = Summary.update (Summary.DestinationsChanged available) state.Summary
        let state = { state with Summary = summary }
        let summary, effects = Summary.followReader state.Reader state.Summary
        { state with Summary = summary }, Cmd.batch [ command; yield! effects |> List.map (Some >> summaryCommand http) ]

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

    /// The marker of a running, finished or failed summary (FR-008); nothing when there is none.
    let summaryMarker (status: SummaryStatus.Status option) =
        cond status <| function
            | Some status ->
                let word = match status with SummaryStatus.Status.Running -> Texts.summaryRunning | SummaryStatus.Status.Finished -> Texts.summaryReady | SummaryStatus.Status.Failed -> Texts.summaryFailed
                small { attr.``class`` "summary-marker"; attr.title word; text word }
            | None -> empty ()

    let libraryView (library: Library.Model) dispatch =
        let busy = library.Busy.IsSome
        let associated = library.Associations |> List.map (fun item -> item.Document.Id) |> Set.ofList
        concat {
            section {
                attr.``class`` "card"
                h2 { text Texts.library }
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
        }

    let projectDocuments (library: Library.Model) (reader: Reader.Model) (statuses: SummaryStatus.StatusEntry list) readerDispatch dispatch =
        let busy = library.Busy.IsSome
        section {
            attr.``class`` "project-document-list"
            cond library.Project <| function
                | None -> p { text Texts.noProject }
                | Some project ->
                    concat {
                        cond library.LoadingProject <| function
                            | true -> p { "role" => "status"; "Loading project documents…" }
                            | false -> empty ()
                        cond (library.HasLoadedProject && library.Associations.IsEmpty) <| function
                            | true -> p { text Texts.noDocuments }
                            | false -> empty ()
                        forEach [ FocusedSource, Texts.focusedSources; ReferenceSource, Texts.referenceSources ] <| fun (role, heading) ->
                            concat {
                                h3 { text heading }
                                ul {
                                    attr.``class`` "project-documents"
                                    "aria-label" => heading
                                    forEach (library.Associations |> List.filter (fun association -> association.Role = role)) <| fun association ->
                                        li {
                                            attr.``class`` (if reader.Open |> Option.exists (fun opened -> opened.Id = association.Document.Id) then "selected" else "")
                                            span { text association.Document.FileName }
                                            small { text (Texts.pageCount association.Document.PageCount) }
                                            summaryMarker (SummaryStatus.documentStatus statuses project association.Document.Id)
                                            strong { attr.``class`` "role"; text (roleLabel association.Role) }
                                            button {
                                                attr.``type`` "button"
                                                on.click (fun _ -> readerDispatch (Reader.Open { Id = association.Document.Id; FileName = association.Document.FileName; ContentUrl = association.Document.ContentUrl }))
                                                "Open"
                                            }
                                            button {
                                                attr.``type`` "button"
                                                attr.disabled (busy || reader.ChangingRole.IsSome)
                                                let role = if association.Role = FocusedSource then ReferenceSource else FocusedSource
                                                on.click (fun _ -> readerDispatch (Reader.ChangeRole (association.Document.Id, role)))
                                                text $"Make {roleLabel role}"
                                            }
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

    /// The context needed to understand and reproduce a summary (FR-008).
    let summaryContext (document: Reader.Document) (generated: Summary.Generated) =
        let generatedAt = generated.GeneratedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
        let unreadable =
            if generated.UnreadablePages.IsEmpty then ""
            else $" Not readable (no text layer): {Summary.pageRanges generated.UnreadablePages}."
        let omitted =
            if generated.OmittedPoints = 0 then ""
            else $" {generated.OmittedPoints} generated points were left out because their citations could not be verified."
        let preposition = if generated.ServiceHost = "ChatGPT plan" then "using" else "at"
        $"{document.FileName}, covering {Summary.pageRanges generated.PagesCovered}.{unreadable} Generated {generatedAt} by {generated.Model} {preposition} {generated.ServiceHost}.{omitted}"

    let private plainButton label (cssClass: string option) onClick =
        button {
            attr.``type`` "button"
            match cssClass with
            | Some name -> attr.``class`` name
            | None -> attr.empty ()
            on.click onClick
            text label
        }

    let private progressText progress =
        match progress with
        | Some (Summary.PagesProcessed (processed, total)) ->
            span { attr.``class`` "summary-progress"; text $"{processed} of {total} pages processed. " }
        | Some Summary.Combining -> span { attr.``class`` "summary-progress"; text "Combining points. " }
        | None -> empty ()

    let private confirmDialog (document: Reader.Document) (target: Summary.ConfirmTarget) dispatch =
        div {
            attr.``class`` "summary-confirm"
            "role" => "alertdialog"
            "aria-label" => "Confirm sending"
            match target with
            | Summary.Service (host, model) ->
                p { text $"The text of {document.FileName} will be sent to {host} (model {model}), which is not on this machine. Send?" }
            | Summary.Plan account ->
                p { text $"The text of {document.FileName} will be sent to your ChatGPT plan (account {account}), which is not on this machine." }
                p {
                    text "It goes to OpenAI under your ChatGPT account. How it is used and kept follows your ChatGPT data controls (Settings, Data controls, in ChatGPT). "
                    a {
                        attr.href "https://help.openai.com/en/articles/20001542-using-your-chatgpt-plan-in-other-apps-and-sites"
                        "target" => "_blank"; "rel" => "noopener noreferrer"
                        text "Using your ChatGPT plan in other apps and sites"
                    }
                    text ". Send?"
                }
            plainButton "Send" (Some "send-confirm") (fun _ -> dispatch Summary.ConfirmSend)
            " "
            plainButton "Cancel" (Some "send-cancel") (fun _ -> dispatch Summary.CancelSend)
        }

    /// Page-number buttons that ask the reader to show that page.
    let private citation onCite page =
        button {
            attr.``type`` "button"
            attr.``class`` "citation"
            "data-page" => string page
            attr.title $"Page {page}"
            on.click (fun _ -> onCite page)
            text (string page)
        }

    let private summaryPanel (document: Reader.Document) (generated: Summary.Generated) onCite dispatch =
        section {
            attr.``class`` "summary"
            h3 { "Generated summary" }
            p {
                attr.``class`` "summary-label"
                strong { "Generated by an AI service for your review." }
                " It is not verified and not your own text. Check each point against the cited pages."
            }
            p { attr.``class`` "summary-context"; text (summaryContext document generated) }
            ul {
                attr.``class`` "summary-points"
                forEach generated.Points <| fun point ->
                    li {
                        span { attr.``class`` "point-text"; text point.Text }
                        " "
                        forEach point.Pages (citation onCite)
                    }
            }
            plainButton "Dismiss" None (fun _ -> dispatch Summary.Dismiss)
        }

    /// The summary controls and panel of the open document. Citations are plain
    /// page-number buttons (`button.citation`, `data-page`) that ask the reader to show that page.
    /// The sign-in action, branded per OpenAI's guidance: its label, its white logo on a dark button, and its attribute.
    let private signInButton (disabled: bool) dispatch =
        button {
            attr.``type`` "button"
            attr.``class`` "siwc-button"
            "data-oai-siwc-button" => ""
            attr.disabled disabled
            on.click (fun _ -> dispatch ChatGpt.SignIn)
            img { attr.src "chatgpt-logo-white.svg"; attr.alt "" }
            text "Continue with ChatGPT"
        }

    /// The account line (or the sign-in action) where she asks for a summary.
    let chatGptAccount (model: ChatGpt.Model) dispatch =
        cond model.Status <| function
            | ChatGpt.Unknown -> empty ()
            | ChatGpt.Unavailable ->
                p {
                    attr.``class`` "chatgpt-account error"
                    "role" => "alert"
                    "ChatGPT sign-in status could not be loaded. "
                    plainButton "Try again" (Some "chatgpt-retry") (fun _ -> dispatch ChatGpt.Load)
                }
            | ChatGpt.SignedOut -> p { attr.``class`` "chatgpt-account"; "ChatGPT: signed out. "; signInButton false dispatch }
            | ChatGpt.Starting -> p { attr.``class`` "chatgpt-account"; "role" => "status"; "Opening ChatGPT… "; signInButton true dispatch }
            | ChatGpt.SignedIn account ->
                p {
                    attr.``class`` "chatgpt-account"
                    text $"Signed in to ChatGPT as {account.Name}. "
                    button {
                        attr.``type`` "button"
                        attr.``class`` "chatgpt-sign-out"
                        attr.disabled model.SigningOut
                        on.click (fun _ -> dispatch ChatGpt.SignOut)
                        text "Sign out"
                    }
                }

    let private noticeText = function
        | ChatGpt.Cancelled -> "Sign-in was cancelled. You are not signed in."
        | ChatGpt.NoPlanAccess -> "This ChatGPT account's plan can't be used by apps, so you are not signed in."
        | ChatGpt.Failed -> "Sign-in could not be completed. You are not signed in. Please try again."
        | ChatGpt.StartFailed -> "Sign-in could not be started. Please try again."
        | ChatGpt.NotRevoked -> "You are signed out. OpenAI did not confirm the disconnection; you can also disconnect LexScholar in your ChatGPT settings."
        | ChatGpt.SignOutFailed -> "Signing out failed. Please try again."

    /// The one-time welcome and the return or sign-out message, at the top of the page.
    let chatGptBanner (model: ChatGpt.Model) dispatch =
        concat {
            cond model.WelcomePending <| function
                | true ->
                    section {
                        attr.``class`` "card chatgpt-welcome"
                        "role" => "status"
                        h2 { "You’re using your ChatGPT plan" }
                        p {
                            "Eligible usage in this app uses your ChatGPT plan. Manage usage in your ChatGPT settings. "
                            a { attr.href "https://chatgpt.com/settings/usage"; "target" => "_blank"; "rel" => "noopener noreferrer"; "Manage usage" }
                        }
                        plainButton "Got it" (Some "chatgpt-welcome-dismiss") (fun _ -> dispatch ChatGpt.DismissWelcome)
                    }
                | false -> empty ()
            cond model.Notice <| function
                | Some notice ->
                    section {
                        attr.``class`` "card chatgpt-notice"
                        "role" => "status"
                        p { text (noticeText notice) }
                        plainButton "Dismiss" (Some "chatgpt-notice-dismiss") (fun _ -> dispatch ChatGpt.DismissNotice)
                    }
                | None -> empty ()
        }

    let private usageLink (caption: string) =
        a { attr.href "https://chatgpt.com/settings/usage"; "target" => "_blank"; "rel" => "noopener noreferrer"; attr.``class`` "manage-usage"; text caption }

    /// Her plan is the destination of the next request.
    let private usingPlan (summary: Summary.Model) =
        summary.Available.Plan && (not summary.Available.Configured || summary.Choice = Some Summary.UsePlan)

    let private destinationChoice (chatGpt: ChatGpt.Model) (summary: Summary.Model) dispatch =
        let radio (value: Summary.Destination) id (caption: string) =
            span {
                input {
                    attr.``type`` "radio"
                    attr.name "summary-destination"
                    attr.id id
                    attr.``checked`` (summary.Choice = Some value)
                    on.change (fun _ -> dispatch (Summary.Choose value))
                }
                label { attr.``for`` id; text caption }
                " "
            }
        let configured =
            match chatGpt.Configured with
            | Some service -> $"{service.Host} (model {service.Model})"
            | None -> "the configured service"
        fieldset {
            attr.``class`` "summary-destination"
            legend { "Summarize with" }
            radio Summary.UsePlan "summary-destination-plan" "Your ChatGPT plan"
            radio Summary.UseConfigured "summary-destination-configured" configured
        }

    let summaryView (document: Reader.Document) (summary: Summary.Model) (chatGpt: ChatGpt.Model) onCite dispatch dispatchChatGpt =
        let needsChoice = summary.Available.Plan && summary.Available.Configured
        concat {
            cond (needsChoice && (match summary.State with Summary.Generating _ | Summary.Running | Summary.Confirming _ -> false | _ -> true)) <| function
                | true -> destinationChoice chatGpt summary dispatch
                | false -> empty ()
            cond summary.State <| function
                | Summary.Generating _ ->
                    span {
                        "role" => "status"
                        "Summarizing… "
                        progressText summary.Progress
                        plainButton "Dismiss" None (fun _ -> dispatch Summary.Dismiss)
                    }
                | Summary.Running ->
                    span {
                        "role" => "status"
                        "Summarizing… "
                        progressText summary.Progress
                        plainButton "Stop" None (fun _ -> dispatch Summary.Stop)
                    }
                | Summary.Shown _ -> empty ()
                | Summary.Dismissing Summary.Running -> span { "role" => "status"; "Stopping… " }
                | Summary.Dismissing _ -> span { "role" => "status"; "Dismissing… " }
                | Summary.Confirming target -> confirmDialog document target dispatch
                | Summary.Idle ->
                    button {
                        attr.``type`` "button"
                        attr.disabled (needsChoice && summary.Choice.IsNone)
                        on.click (fun _ -> dispatch Summary.Summarize)
                        text "Summarize"
                    }
                | Summary.Failed failure ->
                    // Her other destination stays reachable, so a retry is always offered next to the primary action.
                    span {
                        cond (Summary.offersSignIn failure) <| function
                            | true -> signInButton (chatGpt.Status <> ChatGpt.SignedOut) dispatchChatGpt
                            | false -> empty ()
                        cond (failure = Summary.UsageLimitReached) <| function
                            | true -> usageLink "Manage usage"
                            | false -> empty ()
                        " "
                        button {
                            attr.``type`` "button"
                            attr.``class`` "summary-retry"
                            attr.disabled (needsChoice && summary.Choice.IsNone)
                            on.click (fun _ -> dispatch Summary.Summarize)
                            text "Try again"
                        }
                    }
            cond (usingPlan summary && (match summary.State with Summary.Failed Summary.UsageLimitReached -> false | _ -> true)) <| function
                | true -> span { attr.``class`` "summary-using-plan"; text " Using ChatGPT plan. "; usageLink "Manage usage" }
                | false -> empty ()
            cond summary.State <| function
                | Summary.Failed Summary.NotConfigured ->
                    p { attr.``class`` "error"; "role" => "alert"; text "Summaries need a ChatGPT sign-in or a summarization service. "
                        span { attr.``class`` "secondary"; text (Summary.failureText Summary.NotConfigured) } }
                | Summary.Failed failure -> p { attr.``class`` "error"; "role" => "alert"; text (Summary.failureText failure) }
                | _ -> empty ()
            cond summary.DismissFailed <| function
                | true -> p { attr.``class`` "error"; "role" => "alert"; "The summary could not be dismissed. Please try again." }
                | false -> empty ()
            cond summary.State <| function
                | Summary.Shown generated -> summaryPanel document generated onCite dispatch
                | _ -> empty ()
        }

    let view (state: State) dispatch =
        let model = state.Projects
        let busy = model.Busy.IsSome
        let dispatch' = dispatch
        let dispatch = ProjectsMsg >> dispatch'
        div {
            attr.``class`` "workspace"
            header {
                attr.``class`` "top-bar"
                h1 { "LexScholar" }
                span {
                    attr.``class`` "breadcrumb"
                    text (model.Snapshot.Projects |> List.tryFind (fun p -> Some p.Id = model.Snapshot.Active) |> Option.map (fun p -> p.Name) |> Option.defaultValue "")
                    cond state.Reader.Open <| function
                        | Some document -> text $" / {document.FileName}"
                        | None -> empty ()
                }
                cond state.ChatGpt.Status <| function
                    | ChatGpt.SignedIn account ->
                        concat {
                            span { attr.``class`` "signed-in"; text Texts.signedIn }
                            elt "details" {
                                elt "summary" { "aria-label" => Texts.account; text Texts.account }
                                p { text account.Name }
                                button {
                                    attr.``type`` "button"
                                    attr.disabled state.ChatGpt.SigningOut
                                    on.click (fun _ -> dispatch' (ChatGptMsg ChatGpt.SignOut))
                                    text Texts.signOut
                                }
                            }
                        }
                    | _ -> empty ()
            }
            aside {
            attr.``class`` "project-column"
            section {
                attr.``class`` "create-project"
                h2 { text Texts.createProject }
                form {
                    on.submit (fun _ -> dispatch Create)
                    "onsubmit:preventDefault" => true
                    label { "for" => "project-name"; text Texts.projectName }
                    input {
                        attr.id "project-name"
                        attr.``type`` "text"
                        bind.input.string model.Name (dispatch << NameChanged)
                        attr.disabled busy
                    }
                    button { attr.``type`` "submit"; attr.disabled busy; text Texts.createProject }
                }
                cond model.Error <| function
                    | Some error -> p { attr.``class`` "error"; "role" => "alert"; text (errorText error) }
                    | None -> empty ()
            }
            section {
                h2 { text Texts.projects }
                cond model.Busy <| function
                    | Some Loading -> p { "role" => "status"; "Loading projects…" }
                    | Some Selecting -> p { "role" => "status"; "Selecting project…" }
                    | _ -> empty ()
                cond (model.HasLoaded && model.Snapshot.Projects.IsEmpty && model.Busy <> Some Loading) <| function
                    | true -> p { text Texts.noProjects }
                    | false -> empty ()
                ul {
                    attr.``class`` "projects"
                    forEach model.Snapshot.Projects <| fun project ->
                        li {
                            span { text project.Name }
                            summaryMarker (SummaryStatus.projectStatus state.Statuses.Entries project.Id)
                            cond (model.Snapshot.Active = Some project.Id) <| function
                                 | true ->
                                     concat {
                                         strong { attr.``class`` "active"; text Texts.active }
                                         cond (state.Library.HasLoadedProject && state.Library.Project = Some project.Id) <| function
                                             | true -> small { attr.``class`` "project-count"; text (string state.Library.Associations.Length) }
                                             | false -> empty ()
                                     }
                                 | false -> button { attr.``type`` "button"; attr.disabled busy; on.click (fun _ -> dispatch (Select project.Id)); text Texts.select }
                        }
                }
                button { attr.``type`` "button"; attr.disabled busy; on.click (fun _ -> dispatch Reload); text Texts.refresh }
            }
            button {
                attr.``type`` "button"
                "aria-label" => Texts.library
                attr.``class`` (if state.ShowLibrary then "library-navigation selected" else "library-navigation")
                on.click (fun _ -> dispatch' ShowLibrary)
                text Texts.library
                cond state.Library.HasLoadedLibrary <| function
                    | true -> small { attr.``class`` "library-count"; text (string state.Library.Documents.Length) }
                    | false -> empty ()
            }
            }
            aside {
                attr.``class`` "document-column"
                h2 { text (model.Snapshot.Projects |> List.tryFind (fun p -> Some p.Id = model.Snapshot.Active) |> Option.map (fun p -> p.Name) |> Option.defaultValue Texts.projects) }
                cond state.Library.HasLoadedProject <| function
                    | true -> p { attr.``class`` "document-count"; text (Texts.documentCount state.Library.Associations.Length) }
                    | false -> empty ()
                button { attr.``type`` "button"; on.click (fun _ -> dispatch' ShowLibrary); text Texts.addDocuments }
                projectDocuments state.Library state.Reader state.Statuses.Entries (ReaderMsg >> dispatch') (LibraryMsg >> dispatch')
            }
            main {
            "data-testid" => "main"
            attr.``class`` "main-column"
            chatGptBanner state.ChatGpt (ChatGptMsg >> dispatch')
            cond state.ShowLibrary <| function
                | true -> libraryView state.Library (LibraryMsg >> dispatch')
                | false -> empty ()
            cond (not state.ShowLibrary && state.Reader.Open.IsNone && state.Reader.Opening.IsNone) <| function
                | true -> p { attr.``class`` "empty-main"; text Texts.chooseDocument }
                | false -> empty ()
            cond state.Reader.Error <| function
                | Some error ->
                    p {
                        "role" => "alert"
                        attr.``class`` "error"
                        text (match error with
                              | Reader.Unavailable -> "This document's stored file is missing, unreadable or not the recorded size. Its library record and project associations are unchanged."
                              | Reader.DocumentNotFound -> "That document is no longer in the library. Refresh and try again."
                              | Reader.AssociationNotFound -> "That association is no longer available. Refresh and try again."
                              | Reader.InvalidRole -> "Choose Focused Source or Reference Source."
                              | Reader.RequestFailed -> "The request failed. Please try again.")
                    }
                | None -> empty ()
            cond state.Reader.Opening <| function
                | Some _ -> p { "role" => "status"; "Opening document…" }
                | None -> empty ()
            cond (if state.ShowLibrary then None else state.Reader.Open) <| function
                | Some document ->
                    section {
                        attr.``class`` "card reader"
                        h2 { "Reader" }
                        p { text document.FileName }
                        p { "Use your browser's PDF viewer controls to navigate pages." }
                        a { attr.href document.ContentUrl; "target" => "_blank"; "rel" => "noopener"; "Open in new tab" }
                        button { attr.``type`` "button"; on.click (fun _ -> dispatch' (ReaderMsg Reader.Close)); "Close" }
                        chatGptAccount state.ChatGpt (ChatGptMsg >> dispatch')
                        summaryView document state.Summary state.ChatGpt (ReaderMsg << Reader.ShowPage >> dispatch') (SummaryMsg >> dispatch') (ChatGptMsg >> dispatch')
                        iframe {
                            // Keyed by the navigation counter: every citation click re-creates the frame.
                            attr.key state.Reader.Navigation
                            attr.``class`` "reader-frame"
                            "title" => document.FileName
                            "src" => (match state.Reader.Page with Some page -> $"{document.ContentUrl}#page={page}" | None -> document.ContentUrl)
                            "style" => "width:100%;height:75vh;border:0"
                        }
                    }
                | None -> empty ()
            }
        }

type App() =
    inherit ProgramComponent<State, Msg>()
    [<Inject>]
    member val Navigation = Unchecked.defaultof<NavigationManager> with get, set
    override this.Program =
        let http = new HttpClient(BaseAddress = Uri(this.Navigation.BaseUri))
        let start _ =
            let library, libraryEffect = Library.update Library.LoadLibrary Library.initial
            // Returning from ChatGPT: read the outcome once, then drop it from the address.
            let uri = Uri(this.Navigation.Uri)
            let query = System.Web.HttpUtility.ParseQueryString(uri.Query)
            let returned = match query.["chatgpt"] with null -> None | value -> Some value
            if returned.IsSome then this.Navigation.NavigateTo(uri.GetLeftPart(UriPartial.Path), replace = true)
            let chatGpt, chatGptEffect = ChatGpt.update ChatGpt.Load ChatGpt.initial
            let statuses, statusEffect = SummaryStatus.updateStatuses SummaryStatus.Refresh SummaryStatus.initialStatuses
            let chatGpt, _ = ChatGpt.update (ChatGpt.Returned returned) chatGpt
            { Projects = { initial with Busy = Some Loading }; Library = library; Reader = Reader.initial; Summary = Summary.initial; ChatGpt = chatGpt; Statuses = statuses; ShowLibrary = false },
            Cmd.batch [ Ui.command http Fetch |> Cmd.map ProjectsMsg
                        Ui.chatGptCommand http this.Navigation chatGptEffect
                        Ui.statusCommand http statusEffect
                        libraryEffect |> Option.map (Ui.libraryCommand http) |> Option.defaultValue Cmd.none ]
        Program.mkProgram start (Ui.update http this.Navigation) Ui.view
