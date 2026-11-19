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
open Microsoft.JSInterop
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

    let changeRole (http: HttpClient) (projectId: Guid) (documentId: Guid) documentRole = async {
        let body = JsonSerializer.Serialize({| role = roleName documentRole |})
        let content = new StringContent(body, Encoding.UTF8, "application/json") :> HttpContent
        let! result = send http HttpMethod.Patch $"api/projects/{projectId:D}/documents/{documentId:D}" (Some content)
        return result |> Result.map ignore |> Result.mapError (function
            | Library.NotFound -> Library.NotFound
            | Library.InvalidRole -> Library.InvalidRole
            | _ -> Library.RequestFailed) }

    /// Requests a summary. The server answers with the job's state (202, or 200 when one exists); nothing is streamed and
    /// leaving the document does not abort the request. Failures never show response details.
    /// The state of the pair's summary job: None when the server has nothing (404); Error when it could not be asked.
    let summaryStateAt suffix (http: HttpClient) (projectId: Guid) (documentId: Guid) : Async<Result<Summary.Remote option, unit>> = async {
        try
            use! response = http.GetAsync($"api/projects/{projectId:D}/documents/{documentId:D}/summary{suffix}") |> Async.AwaitTask
            if response.StatusCode = HttpStatusCode.NotFound then return Ok None
            elif not response.IsSuccessStatusCode then return Error ()
            else
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                match SummaryStatus.parseRemote body with
                | Some remote -> return Ok (Some remote)
                | None -> return Error ()
        with _ -> return Error () }

    let summaryState http project document = summaryStateAt "" http project document

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
    let dismissSummaryAt replacement (http: HttpClient) (projectId: Guid) (documentId: Guid) : Async<bool> = async {
        try
            use! tokenResponse = http.GetAsync("api/antiforgery") |> Async.AwaitTask
            if not tokenResponse.IsSuccessStatusCode then return false
            else
                let! tokenBody = tokenResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
                use tokenJson = JsonDocument.Parse(tokenBody)
                let suffix = if replacement then "/cancel" else ""
                let method = if replacement then HttpMethod.Post else HttpMethod.Delete
                use request = new HttpRequestMessage(method, $"api/projects/{projectId:D}/documents/{documentId:D}/summary{suffix}")
                request.Headers.Add("X-CSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString())
                use! response = http.SendAsync request |> Async.AwaitTask
                return response.IsSuccessStatusCode
        with _ -> return false }

    let dismissSummary http project document = dismissSummaryAt false http project document

    let summarize replacement (http: HttpClient) (projectId: Guid) (documentId: Guid) (destination: Summary.Destination option) (confirmed: Summary.Confirmed option) (instruction: string option) (cancellation: Threading.CancellationToken) : Async<Result<Summary.Remote, Summary.Failure>> = async {
        try
            use! tokenResponse = http.GetAsync("api/antiforgery", cancellation) |> Async.AwaitTask
            if not tokenResponse.IsSuccessStatusCode then return Error Summary.RequestFailed
            else
                let! tokenBody = tokenResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
                use tokenJson = JsonDocument.Parse(tokenBody)
                let suffix = if replacement then "/regenerate" else ""
                use request = new HttpRequestMessage(HttpMethod.Post, $"api/projects/{projectId:D}/documents/{documentId:D}/summary{suffix}")
                request.Headers.Add("X-CSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString())
                request.Content <- new StringContent(SummaryStatus.requestBodyWithInstruction destination confirmed instruction, Encoding.UTF8, "application/json")
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

/// The whole page: projects, shared library and the opened document's summary.
type State = { Projects: Model; Library: Library.Model; Summary: Summary.Model; ChatGpt: ChatGpt.Model; Statuses: SummaryStatus.Statuses; ShowLibrary: bool; PendingProjectOpen: bool; Onboarding: bool option }

type Msg =
    | FinishOnboarding
    | ShowLibrary
    | ProjectsMsg of Message
    | LibraryMsg of Library.Message
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
        | Library.SendRole (projectId, documentId, role) ->
            perform (fun () -> Api.changeRole http projectId documentId role) (fun result -> Library.RoleChanged (projectId, result))

    let private updateLibrary http message state =
        let library, effect = Library.update message state.Library
        let showLibrary = match message with Library.OpenDocument _ when library.Opened.IsSome -> false | _ -> state.ShowLibrary
        let pendingProjectOpen = match message with Library.OpenDocument _ when library.Opened.IsSome -> false | _ -> state.PendingProjectOpen
        { state with Library = library; ShowLibrary = showLibrary; PendingProjectOpen = pendingProjectOpen }, effect |> Option.map (libraryCommand http) |> Option.defaultValue Cmd.none

    /// How often the open document's running summary is asked for (research §4).
    let private summaryPollMilliseconds = 2000

    let private summaryCommand http (effect: Summary.Effect option) : Cmd<Msg> =
        let send request projectId documentId destination confirmed instruction =
            Cmd.ofEffect (fun dispatch ->
                Async.StartImmediate(async {
                    match! Api.summarize false http projectId documentId destination confirmed instruction Threading.CancellationToken.None with
                    | Ok remote -> dispatch (SummaryMsg (Summary.Accepted (request, projectId, documentId, remote)))
                    | Error failure -> dispatch (SummaryMsg (Summary.Responded (request, Error failure))) }))
        match effect with
        | Some (Summary.RegenerateSummary (request, project, document, feedback)) ->
            Cmd.ofEffect (fun dispatch ->
                Async.StartImmediate(async {
                    let! result = Api.summarize true http project document (Some Summary.UsePlan) (Some Summary.ConfirmedPlan) (LexScholar.Domain.SummaryInstruction.normalize feedback) Threading.CancellationToken.None
                    dispatch (SummaryMsg (Summary.ReplacementAnswered (request, project, document, result))) }))
        | Some (Summary.FetchReplacement (watch, project, document, delay)) ->
            Cmd.ofEffect (fun dispatch ->
                Async.StartImmediate(async {
                    if delay then do! Async.Sleep summaryPollMilliseconds
                    let! remote = Api.summaryStateAt "/regenerate" http project document
                    let state = match remote with Ok value -> value | Error () -> Some (Summary.RemoteRunning None)
                    dispatch (SummaryMsg (Summary.ReplacementPolled (watch, state))) }))
        | Some (Summary.CancelRegeneration (watch, project, document)) ->
            Cmd.ofEffect (fun dispatch ->
                Async.StartImmediate(async {
                    let! succeeded = Api.dismissSummaryAt true http project document
                    dispatch (SummaryMsg (Summary.ReplacementCancelled (watch, project, document, succeeded))) }))
        | Some (Summary.Send (request, project, document, destination, confirmed)) ->
            send request project document destination confirmed None
        | Some (Summary.SendWithInstruction (request, project, document, destination, confirmed, instruction)) ->
            send request project document destination confirmed (Some instruction)
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

    let private updateCore http (navigation: NavigationManager) msg state =
        match msg with
        | FinishOnboarding when state.Projects.HasLoaded && state.Projects.Busy.IsNone && state.Library.Busy.IsNone && state.ChatGpt.Status <> ChatGpt.Starting ->
            { state with Onboarding = Some false }, Cmd.none
        | FinishOnboarding -> state, Cmd.none
        | ShowLibrary ->
            let library, _ = Library.update Library.HideOptions state.Library
            { state with ShowLibrary = true; Library = library; PendingProjectOpen = false }, Cmd.none
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
                | Summary.ReplacementAnswered (_, _, _, Error failure)
                | Summary.ReplacementAnswered (_, _, _, Ok (Summary.RemoteFailed failure))
                | Summary.ReplacementPolled (_, Some (Summary.RemoteFailed failure)) ->
                    summary.Replacement = Some (Summary.ReplacementFailed failure) && summary.Replacement <> state.Summary.Replacement && Summary.reloadsDestinations failure
                | _ -> false
            let chatGpt, chatGptEffect = if reload then ChatGpt.update ChatGpt.Load state.ChatGpt else state.ChatGpt, None
            { state with Summary = summary; ChatGpt = chatGpt },
            Cmd.batch [ summaryCommand http effect; chatGptCommand http navigation chatGptEffect ]
        | ProjectsMsg message ->
            let projects, effect = Projects.update message state.Projects
            let onboarding =
                if state.Onboarding.IsNone && projects.HasLoaded then Some projects.Snapshot.Projects.IsEmpty
                else state.Onboarding
            let showLibrary =
                match message with
                | Select id when state.Projects.Busy.IsNone && state.Projects.Snapshot.Active = Some id -> false
                | Selected (Ok ()) when state.Projects.Busy = Some Selecting && state.PendingProjectOpen -> false
                | _ -> state.ShowLibrary
            let pendingProjectOpen =
                // A selection response may update the active project, but may
                // navigate only while its original open intent is still current.
                match message, effect with
                | _, Some (SendSelect _) -> true
                | Selected _, _ when state.Projects.Busy = Some Selecting -> false
                | _ -> state.PendingProjectOpen
            let state = { state with Projects = projects; Onboarding = onboarding; ShowLibrary = showLibrary; PendingProjectOpen = pendingProjectOpen }
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

    /// The summary follows the library's project and open document after every message.
    let update http navigation msg state =
        let viewOf (state: State) = state.Library.Project, state.Library.Opened
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
        let summary, effects = Summary.followOpened state.Library state.Summary
        { state with Summary = summary }, Cmd.batch [ command; yield! effects |> List.map (Some >> summaryCommand http) ]

    let roleLabel = function
        | FocusedSource -> Texts.focusedSource
        | ReferenceSource -> Texts.referenceSource

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
                label { "for" => "library-file"; text Texts.importPdf }
                comp<InputFile> {
                    "id" => "library-file"
                    "accept" => ".pdf,application/pdf"
                    "disabled" => busy
                    attr.callback "OnChange" (fun (event: InputFileChangeEventArgs) -> dispatch (Library.Upload event.File))
                }
                cond library.Busy <| function
                    | Some Library.Uploading -> p { "role" => "status"; text Texts.importing }
                    | _ -> empty ()
                cond library.Notice <| function
                    | Some (Library.Imported document) -> p { "role" => "status"; text (Texts.imported document.FileName) }
                    | Some (Library.Reused document) ->
                        p { "role" => "status"; text (Texts.reused document.FileName) }
                    | None -> empty ()
                cond library.Error <| function
                    | Some error -> p { attr.``class`` "error"; "role" => "alert"; text (FailureTexts.library error) }
                    | None -> empty ()
                cond (library.HasLoadedLibrary && library.Documents.IsEmpty) <| function
                    | true -> p { text Texts.emptyLibrary }
                    | false -> empty ()
                ul {
                    attr.``class`` "documents"
                    forEach library.Documents <| fun document ->
                        li {
                            span { text document.FileName; text Texts.space; small { text (Texts.parenthesizedPageCount document.PageCount) } }
                            // Bolero's cond needs a union or bool, so the checks are nested.
                            cond library.Project <| function
                                | None -> empty ()
                                | Some _ ->
                                    cond (associated.Contains document.Id) <| function
                                        | true -> small { text Texts.inThisProject }
                                        | false ->
                                            span {
                                                attr.``class`` "actions"
                                                forEach [ FocusedSource; ReferenceSource ] <| fun role ->
                                                    button {
                                                        attr.``type`` "button"
                                                        attr.disabled busy
                                                        on.click (fun _ -> dispatch (Library.Associate (document.Id, role)))
                                                        text (if role = FocusedSource then Texts.addFocused else Texts.addReference)
                                                    }
                                            }
                        }
                }
            }
        }

    let projectDocuments (library: Library.Model) showLibrary (statuses: SummaryStatus.StatusEntry list) dispatch =
        let busy = library.Busy.IsSome
        section {
            attr.``class`` "project-document-list"
            cond library.Project <| function
                | None -> p { text Texts.noProject }
                | Some project ->
                    concat {
                        cond library.LoadingProject <| function
                            | true -> p { "role" => "status"; text Texts.loadingDocuments }
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
                                            attr.``class`` (if not showLibrary && library.Opened = Some association.Document.Id then "selected" else "")
                                            button {
                                                attr.``type`` "button"
                                                attr.``class`` "item-open"
                                                "aria-current" => (if not showLibrary && library.Opened = Some association.Document.Id then "true" else "false")
                                                "aria-expanded" => (if library.Options = Some association.Document.Id then "true" else "false")
                                                on.click (fun _ ->
                                                    if not showLibrary && library.Opened = Some association.Document.Id then
                                                        dispatch (Library.RevealOptions association.Document.Id)
                                                    else dispatch (Library.OpenDocument association.Document.Id))
                                                text association.Document.FileName
                                            }
                                            small { text (Texts.pageCount association.Document.PageCount) }
                                            summaryMarker (SummaryStatus.documentStatus statuses project association.Document.Id)
                                            strong { attr.``class`` "role"; text (roleLabel association.Role) }
                                            cond (library.Options = Some association.Document.Id) <| function
                                                | true ->
                                                    div {
                                                        attr.``class`` "item-options"
                                                        button {
                                                            attr.``type`` "button"
                                                            attr.disabled (busy || library.ChangingRole.IsSome)
                                                            let role = if association.Role = FocusedSource then ReferenceSource else FocusedSource
                                                            on.click (fun _ -> dispatch (Library.ChangeRole (association.Document.Id, role)))
                                                            text (if role = FocusedSource then Texts.makeFocused else Texts.makeReference)
                                                        }
                                                        button {
                                                            attr.``type`` "button"
                                                            attr.disabled busy
                                                            on.click (fun _ -> dispatch (Library.Remove association.Document.Id))
                                                            text Texts.remove
                                                        }
                                                    }
                                                | false -> empty ()
                                        }
                                }
                            }
                    }
        }

    /// The context needed to understand and reproduce a summary (FR-008).
    let summaryContext (document: Library.Document) (generated: Summary.Generated) =
        let generatedAt = Texts.dateTime generated.GeneratedAt
        let unreadable = if generated.UnreadablePages.IsEmpty then "" else Texts.unreadable (Texts.pages (generated.UnreadablePages |> List.distinct |> List.sort))
        let omitted = if generated.OmittedPoints = 0 then "" else Texts.omittedPoints generated.OmittedPoints
        Texts.covered document.FileName (Texts.pages (generated.PagesCovered |> List.distinct |> List.sort)) + unreadable + Texts.provenance generatedAt generated.Model generated.ServiceHost + omitted

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
            span { attr.``class`` "summary-progress"; text (Texts.processedPages processed total) }
        | Some Summary.Combining -> span { attr.``class`` "summary-progress"; text Texts.combining }
        | None -> empty ()

    let private confirmDialog (document: Library.Document) (target: Summary.ConfirmTarget) replacement dispatch =
        elt "dialog" {
            attr.id "send-confirmation"
            attr.``class`` "send-dialog"
            "role" => "alertdialog"
            "aria-label" => Texts.confirmationTitle
            "aria-modal" => "true"
            "aria-describedby" =>
                (match target with
                 | Summary.Service _ -> "send-disclosure"
                 | Summary.Plan _ -> "send-disclosure send-information")
            on.event "cancel" (fun _ -> dispatch Summary.CancelSend)
            h3 { text Texts.confirmationTitle }
            match target with
            | Summary.Service (host, model) -> p { attr.id "send-disclosure"; text (Texts.confirmationService document.FileName host model) }
            | Summary.Plan account ->
                p {
                    attr.id "send-disclosure"
                    text (Texts.confirmationPlan document.FileName document.PageCount account)
                    if replacement then text Texts.feedbackPrivacy
                }
                a { attr.href Texts.confirmationHelpUrl; "target" => "_blank"; "rel" => "noopener noreferrer"; text Texts.confirmationHelp }
                p { attr.id "send-information"; attr.``class`` "send-info"; text Texts.confirmationNote }
            div {
                attr.``class`` "send-actions"
                button {
                    attr.``type`` "button"
                    attr.``class`` "send-cancel"
                    "autofocus" => "autofocus"
                    on.click (fun _ -> dispatch Summary.CancelSend)
                    text Texts.cancel
                }
                plainButton Texts.sendAndSummarize (Some "send-confirm") (fun _ -> dispatch Summary.ConfirmSend)
            }
        }

    /// One link per consecutive run of validated pages, always for the open document.
    let private citations contentUrl pages =
        let runs =
            pages
            |> List.fold (fun groups page ->
                match groups with
                | current :: rest when page = List.head current + 1 -> (page :: current) :: rest
                | _ -> [page] :: groups) []
            |> List.rev
            |> List.map List.rev
        forEach (List.indexed runs) <| fun (index, run) -> concat {
            if index > 0 then text Texts.citationSeparator
            a {
                attr.``class`` "citation"
                attr.href $"{contentUrl}#page={List.head run}"
                "target" => "_blank"
                "rel" => "noopener"
                text (Texts.pages run)
            }
        }

    let private summaryPanel (document: Library.Document) (generated: Summary.Generated) replacing dispatch =
        section {
            attr.``class`` "summary card"
            div {
                attr.style "display: flex; justify-content: space-between; align-items: center"
                h3 { text Texts.summaryReady }
                button {
                    attr.``type`` "button"
                    attr.disabled replacing
                    attr.style "color: var(--danger)"
                    on.click (fun _ -> dispatch Summary.Delete)
                    text Texts.delete
                }
            }
            p {
                attr.``class`` "summary-label"
                strong { text Texts.generated }
            }
            p { attr.``class`` "summary-context"; text (summaryContext document generated) }
            ul {
                attr.``class`` "summary-points"
                forEach generated.Points <| fun point ->
                    li {
                        span { attr.``class`` "point-text"; text point.Text }
                        text Texts.space
                        citations document.ContentUrl point.Pages
                    }
            }
        }

    /// The sign-in action, branded per OpenAI's guidance: its label, its white logo on a dark button, and its attribute.
    let private signInButton (disabled: bool) dispatch =
        button {
            attr.``type`` "button"
            attr.``class`` "siwc-button"
            "data-oai-siwc-button" => ""
            attr.disabled disabled
            on.click (fun _ -> dispatch ChatGpt.SignIn)
            img { attr.src "chatgpt-logo-white.svg"; attr.alt "" }
            text Texts.signIn
        }

    /// The account line (or the sign-in action) where she asks for a summary.
    let chatGptAccount (model: ChatGpt.Model) dispatch =
        cond model.Status <| function
            | ChatGpt.Unknown -> empty ()
            | ChatGpt.Unavailable ->
                p {
                    attr.``class`` "chatgpt-account error"
                    "role" => "alert"
                    text Texts.chatGptUnavailable
                    plainButton Texts.retry (Some "chatgpt-retry") (fun _ -> dispatch ChatGpt.Load)
                }
            | ChatGpt.SignedOut -> p { attr.``class`` "chatgpt-account"; text Texts.chatGptSignedOut; signInButton false dispatch }
            | ChatGpt.Starting -> p { attr.``class`` "chatgpt-account"; "role" => "status"; text Texts.chatGptStarting; signInButton true dispatch }
            | ChatGpt.SignedIn account ->
                p {
                    attr.``class`` "chatgpt-account"
                    text (Texts.chatGptAccount account.Name)
                    button {
                        attr.``type`` "button"
                        attr.``class`` "chatgpt-sign-out"
                        attr.disabled model.SigningOut
                        on.click (fun _ -> dispatch ChatGpt.SignOut)
                        text Texts.signOut
                    }
                }

    let private noticeText = function
        | ChatGpt.Cancelled -> Texts.signInCancelled
        | ChatGpt.NoPlanAccess -> Texts.signInNoPlan
        | ChatGpt.Failed -> Texts.signInFailed
        | ChatGpt.StartFailed -> Texts.signInStartFailed
        | ChatGpt.NotRevoked -> Texts.notRevoked
        | ChatGpt.SignOutFailed -> Texts.signOutFailed

    /// The one-time welcome and the return or sign-out message, at the top of the page.
    let chatGptBanner (model: ChatGpt.Model) dispatch =
        concat {
            cond model.WelcomePending <| function
                | true ->
                    section {
                        attr.``class`` "card chatgpt-welcome"
                        "role" => "status"
                        h2 { text Texts.welcomePlan }
                        p {
                            text Texts.welcomePlanBody
                            a { attr.href "https://chatgpt.com/settings/usage"; "target" => "_blank"; "rel" => "noopener noreferrer"; text Texts.manageUsage }
                        }
                        plainButton Texts.gotIt (Some "chatgpt-welcome-dismiss") (fun _ -> dispatch ChatGpt.DismissWelcome)
                    }
                | false -> empty ()
            cond model.Notice <| function
                | Some notice ->
                    section {
                        attr.``class`` "card chatgpt-notice"
                        "role" => "status"
                        p { text (noticeText notice) }
                        plainButton Texts.close (Some "chatgpt-notice-dismiss") (fun _ -> dispatch ChatGpt.DismissNotice)
                    }
                | None -> empty ()
        }

    let private usageLink (caption: string) =
        a { attr.href "https://chatgpt.com/settings/usage"; "target" => "_blank"; "rel" => "noopener noreferrer"; attr.``class`` "manage-usage"; text caption }

    let summaryView (document: Library.Document) (summary: Summary.Model) (chatGpt: ChatGpt.Model) dispatch dispatchChatGpt =
        concat {
            cond summary.State <| function
                | Summary.Generating _ ->
                    span {
                        "role" => "status"
                        text Texts.summarizing
                        progressText summary.Progress
                        plainButton Texts.close None (fun _ -> dispatch Summary.Dismiss)
                    }
                | Summary.Running ->
                    span {
                        "role" => "status"
                        text Texts.summarizing
                        progressText summary.Progress
                        plainButton Texts.stop None (fun _ -> dispatch Summary.Stop)
                    }
                | Summary.Shown _ -> empty ()
                | Summary.ConfirmingDelete _ ->
                    div {
                        attr.``class`` "summary-confirm"
                        "role" => "alertdialog"
                        "aria-label" => Texts.confirmDelete
                        p { text Texts.confirmDelete }
                        plainButton Texts.delete None (fun _ -> dispatch Summary.ConfirmDelete)
                        plainButton Texts.cancel None (fun _ -> dispatch Summary.CancelDelete)
                    }
                | Summary.Dismissing Summary.Running -> span { "role" => "status"; text Texts.stopping }
                | Summary.Dismissing _ -> span { "role" => "status"; text Texts.dismissing }
                | Summary.Confirming target -> confirmDialog document target false dispatch
                | Summary.Idle ->
                    section {
                        attr.``class`` "summary-request card"
                        h3 { text Texts.requestSummary }
                        p { text Texts.requestDescription }
                        label { attr.``for`` "summary-instruction"; text Texts.instructionLabel }
                        textarea {
                            attr.id "summary-instruction"
                            attr.``class`` "summary-instruction"
                            "maxlength" => string LexScholar.Domain.SummaryInstruction.maximumCharacters
                            "rows" => "3"
                            "aria-describedby" => "summary-instruction-privacy"
                            attr.placeholder Texts.instructionPlaceholder
                            attr.value summary.Instruction
                            on.input (fun event -> dispatch (Summary.EditInstruction (string event.Value)))
                        }
                        p { attr.id "summary-instruction-privacy"; attr.``class`` "muted"; text Texts.instructionPrivacy }
                        if summary.Available.Plan then
                            div {
                                attr.``class`` "summary-request-actions"
                                button {
                                    attr.``type`` "button"
                                    attr.id "summary-request"
                                    attr.``class`` "summary-primary"
                                    on.click (fun _ -> dispatch Summary.Summarize)
                                    text Texts.summarize
                                }
                            }
                    }
                | Summary.Failed failure ->
                    // Her other destination stays reachable, so a retry is always offered next to the primary action.
                    span {
                        cond (Summary.offersSignIn failure) <| function
                            | true -> signInButton (chatGpt.Status <> ChatGpt.SignedOut) dispatchChatGpt
                            | false -> empty ()
                        cond (failure = Summary.UsageLimitReached) <| function
                            | true -> usageLink Texts.manageUsage
                            | false -> empty ()
                        text Texts.space
                        button {
                            attr.``type`` "button"
                            attr.``class`` "summary-retry"
                            attr.disabled (not summary.Available.Plan)
                            on.click (fun _ -> dispatch Summary.Summarize)
                            text Texts.retry
                        }
                        plainButton Texts.close None (fun _ -> dispatch Summary.Close)
                    }
            cond summary.State <| function
                | Summary.Failed failure -> p { attr.``class`` "error"; "role" => "alert"; text (FailureTexts.summary failure) }
                | _ -> empty ()
            cond summary.DismissFailed <| function
                | true -> p { attr.``class`` "error"; "role" => "alert"; text Texts.dismissFailed }
                | false -> empty ()
            cond summary.State <| function
                | Summary.Shown generated -> summaryPanel document generated (match summary.Replacement with None | Some (Summary.ReplacementFailed _) -> false | _ -> true) dispatch
                | Summary.ConfirmingDelete generated -> summaryPanel document generated false dispatch
                | _ -> empty ()
            cond summary.State <| function
                | Summary.Shown _ ->
                    section {
                        attr.``class`` "summary-feedback card"
                        cond summary.Replacement <| function
                            | Some (Summary.AwaitingConsent account) -> confirmDialog document (Summary.Plan account) true dispatch
                            | Some (Summary.Submitting _ | Summary.Replacing) ->
                                concat {
                                    p { "role" => "status"; text Texts.replacing; progressText summary.Progress }
                                    plainButton Texts.stop None (fun _ -> dispatch Summary.CancelReplacement)
                                }
                            | Some Summary.Cancelling -> p { "role" => "status"; text Texts.stopping }
                            | Some Summary.Reconciling -> p { "role" => "status"; text Texts.reconcilingReplacement }
                            | Some Summary.CancellationFailed ->
                                concat {
                                    p { "role" => "alert"; text Texts.cancellationUncertain }
                                    plainButton Texts.stop None (fun _ -> dispatch Summary.CancelReplacement)
                                }
                            | Some (Summary.ReplacementFailed failure) ->
                                concat {
                                    p { "role" => "alert"; text Texts.replacementFailed; text Texts.space; text (FailureTexts.summary failure) }
                                    if Summary.offersSignIn failure then signInButton (chatGpt.Status <> ChatGpt.SignedOut) dispatchChatGpt
                                    if failure = Summary.UsageLimitReached then usageLink Texts.manageUsage
                                }
                            | None -> empty ()
                        if (match summary.Replacement with None | Some Summary.Reconciling | Some (Summary.ReplacementFailed _) -> true | _ -> false) then
                            label { attr.``for`` "summary-feedback"; text Texts.feedbackLabel }
                            textarea {
                                attr.id "summary-feedback"
                                attr.``class`` "summary-instruction"
                                "maxlength" => string LexScholar.Domain.SummaryInstruction.maximumCharacters
                                "rows" => "3"
                                "aria-describedby" => "summary-feedback-privacy"
                                attr.value summary.Feedback
                                on.input (fun event -> dispatch (Summary.EditFeedback (string event.Value)))
                            }
                            p { attr.id "summary-feedback-privacy"; attr.``class`` "muted"; text Texts.feedbackPrivacy }
                            button {
                                attr.``type`` "button"
                                attr.id "summary-request"
                                attr.disabled (not summary.Available.Plan || summary.Replacement = Some Summary.Reconciling || summary.ReplacementCancels > 0 || not summary.CancelledReplacements.IsEmpty)
                                on.click (fun _ ->
                                    match chatGpt.Status with
                                    | ChatGpt.SignedIn account -> dispatch (Summary.Regenerate account.Name)
                                    | _ -> ())
                                text Texts.regenerate
                            }
                    }
                | _ -> empty ()
            cond (match summary.State with Summary.Idle -> summary.Available.Plan | Summary.Shown generated | Summary.ConfirmingDelete generated -> generated.ServiceHost = "ChatGPT plan" | _ -> false) <| function
                | true -> span { attr.``class`` "summary-using-plan"; text Texts.usingPlan; text Texts.detailSeparator; usageLink Texts.manageUsage }
                | false -> empty ()
        }

    let private projectForm (model: Projects.Model) dispatch =
        let busy = model.Busy.IsSome
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
                    attr.placeholder Texts.projectNameExample
                    bind.input.string model.Name (dispatch << NameChanged)
                    attr.disabled busy
                }
                button { attr.``type`` "submit"; attr.disabled busy; text Texts.createProject }
            }
            cond model.Error <| function
                | Some error -> p { attr.``class`` "error"; "role" => "alert"; text (FailureTexts.project error) }
                | None -> empty ()
        }

    let private onboardingView (state: State) dispatch =
        section {
            attr.``class`` "onboarding"
            h2 { text Texts.welcome }
            p { attr.``class`` "onboarding-intro"; text Texts.welcomeIntro }
            section {
                attr.``class`` "onboarding-step"
                span { attr.``class`` "step-number"; text "1" }
                div {
                    h3 { text Texts.firstProject }
                    p { text Texts.firstProjectHelp }
                    cond state.Projects.Snapshot.Projects <| function
                        | project :: _ -> p { "role" => "status"; text (Texts.projectCreated project.Name) }
                        | [] -> projectForm state.Projects (ProjectsMsg >> dispatch)
                }
            }
            section {
                attr.``class`` "onboarding-step"
                span { attr.``class`` "step-number"; text "2" }
                div {
                    h3 { text Texts.importPdfs }
                    p { text Texts.importPdfsHelp }
                    label { attr.``class`` "file-button"; "for" => "onboarding-file"; text Texts.choosePdfs }
                    comp<InputFile> {
                        "id" => "onboarding-file"
                        "class" => "onboarding-file"
                        "aria-label" => Texts.choosePdfs
                        "accept" => ".pdf,application/pdf"
                        "disabled" => state.Library.Busy.IsSome
                        attr.callback "OnChange" (fun (event: InputFileChangeEventArgs) -> dispatch (LibraryMsg (Library.Upload event.File)))
                    }
                    cond state.Library.Busy <| function
                        | Some Library.Uploading -> p { "role" => "status"; text Texts.importing }
                        | _ -> empty ()
                    cond state.Library.Notice <| function
                        | Some (Library.Imported doc) -> p { "role" => "status"; text (Texts.imported doc.FileName) }
                        | Some (Library.Reused doc) -> p { "role" => "status"; text (Texts.reused doc.FileName) }
                        | None -> empty ()
                    cond state.Library.Error <| function
                        | Some failure -> p { "role" => "alert"; text (FailureTexts.library failure) }
                        | None -> empty ()
                }
            }
            section {
                attr.``class`` "onboarding-step"
                span { attr.``class`` "step-number"; text "3" }
                div {
                    h3 { text Texts.signInForSummaries }
                    p { text Texts.signInHelp }
                    div {
                        attr.``class`` "onboarding-signin"
                        cond state.ChatGpt.Status <| function
                            | ChatGpt.SignedOut -> signInButton false (ChatGptMsg >> dispatch)
                            | ChatGpt.Starting -> signInButton true (ChatGptMsg >> dispatch)
                            | ChatGpt.SignedIn _ -> p { text Texts.signedIn }
                            | ChatGpt.Unavailable -> chatGptAccount state.ChatGpt (ChatGptMsg >> dispatch)
                            | _ -> empty ()
                        small { text Texts.planRequired }
                    }
                }
            }
            div {
                attr.``class`` "onboarding-actions"
                button {
                    attr.``type`` "button"
                    attr.disabled (state.Projects.Busy.IsSome || state.Library.Busy.IsSome || state.ChatGpt.Status = ChatGpt.Starting)
                    on.click (fun _ -> dispatch FinishOnboarding)
                    text (if state.Projects.Snapshot.Projects.IsEmpty then Texts.skipOnboarding else Texts.finishOnboarding)
                }
            }
        }

    let view (state: State) dispatch =
        let model = state.Projects
        let busy = model.Busy.IsSome
        let onboarding = state.Onboarding <> Some false
        let projectName =
            model.Snapshot.Projects
            |> List.tryFind (fun p -> Some p.Id = model.Snapshot.Active)
            |> Option.map (fun p -> p.Name)
        let opened = state.Library.Associations |> List.tryFind (fun item -> Some item.Document.Id = state.Library.Opened)
        let projectLabel = projectName |> Option.defaultValue ""
        let location =
            if state.ShowLibrary then Some Texts.library
            else opened |> Option.map (fun item -> item.Document.FileName)
        let breadcrumb = [ projectName; location ] |> List.choose id |> String.concat Texts.breadcrumbSeparator
        let dispatch' = dispatch
        let dispatch = ProjectsMsg >> dispatch'
        div {
            attr.``class`` (if onboarding then "workspace onboarding-workspace" else "workspace")
            "data-ready" => string model.HasLoaded
            header {
                attr.``class`` "top-bar"
                h1 { text Texts.appName }
                span {
                    attr.``class`` "breadcrumb"
                    text (if onboarding then "" else breadcrumb)
                }
                cond state.ChatGpt.Status <| function
                    | ChatGpt.SignedIn account when not onboarding ->
                        concat {
                            span { attr.``class`` "signed-in"; text Texts.signedIn }
                            elt "details" {
                                elt "summary" { "aria-label" => Texts.account; text Texts.account }
                                div {
                                    attr.``class`` "account-panel"
                                    p { text account.Name }
                                    button {
                                        attr.``type`` "button"
                                        attr.disabled state.ChatGpt.SigningOut
                                        on.click (fun _ -> dispatch' (ChatGptMsg ChatGpt.SignOut))
                                        text Texts.signOut
                                    }
                                }
                            }
                        }
                    | ChatGpt.SignedOut when not onboarding && (state.ShowLibrary || not model.Snapshot.Projects.IsEmpty) && (state.ShowLibrary || opened.IsNone) ->
                        signInButton false (ChatGptMsg >> dispatch')
                    | ChatGpt.Starting when not onboarding && (state.ShowLibrary || not model.Snapshot.Projects.IsEmpty) && (state.ShowLibrary || opened.IsNone) ->
                        signInButton true (ChatGptMsg >> dispatch')
                    | _ -> empty ()
            }
            cond onboarding <| function
             | true ->
                main {
                    "data-testid" => "main"
                    chatGptBanner state.ChatGpt (ChatGptMsg >> dispatch')
                    cond (model.HasLoaded && state.Onboarding.IsSome) <| function
                        | true -> onboardingView state dispatch'
                        | false ->
                            concat {
                                p { "role" => "status"; text Texts.loadingProjects }
                                cond model.Error <| function
                                    | Some failure ->
                                        concat {
                                            p { "role" => "alert"; text (FailureTexts.project failure) }
                                            button { on.click (fun _ -> dispatch Reload); text Texts.refresh }
                                        }
                                    | None -> empty ()
                            }
                }
             | false ->
                concat {
                    aside {
                        attr.``class`` "project-column"
                        projectForm model dispatch
                        section {
                            h2 { text Texts.projects }
                            cond model.Busy <| function
                                | Some Loading -> p { "role" => "status"; text Texts.loadingProjects }
                                | Some Selecting -> p { "role" => "status"; text Texts.selectingProject }
                                | _ -> empty ()
                            cond (model.HasLoaded && model.Snapshot.Projects.IsEmpty && model.Busy <> Some Loading) <| function
                                | true -> p { text Texts.noProjects }
                                | false -> empty ()
                            ul {
                                attr.``class`` "projects"
                                forEach model.Snapshot.Projects <| fun project ->
                                    li {
                                        attr.``class`` (if model.Snapshot.Active = Some project.Id then "selected" else "")
                                        "aria-current" => (if model.Snapshot.Active = Some project.Id then "true" else "false")
                                        button {
                                            attr.``type`` "button"
                                            attr.``class`` "project-name item-open"
                                            attr.disabled busy
                                            "aria-expanded" => (if model.Options = Some project.Id then "true" else "false")
                                            on.click (fun _ -> dispatch (Select project.Id))
                                            text project.Name
                                        }
                                        div {
                                            attr.``class`` "project-metadata"
                                            summaryMarker (SummaryStatus.projectStatus state.Statuses.Entries project.Id)
                                            cond (model.Snapshot.Active = Some project.Id) <| function
                                                | true ->
                                                    cond (state.Library.HasLoadedProject && state.Library.Project = Some project.Id) <| function
                                                        | true -> small { attr.``class`` "project-count"; text (string state.Library.Associations.Length) }
                                                        | false -> empty ()
                                                | false -> empty ()
                                        }
                                        cond (model.Options = Some project.Id) <| function
                                            | true ->
                                                div {
                                                    attr.``class`` "item-options"
                                                    button { attr.``type`` "button"; attr.disabled busy; on.click (fun _ -> dispatch Reload); text Texts.refresh }
                                                }
                                            | false -> empty ()
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
                        h2 { text (projectName |> Option.defaultValue Texts.projects) }
                        cond state.Library.HasLoadedProject <| function
                            | true -> p { attr.``class`` "document-count"; text (Texts.documentCount state.Library.Associations.Length) }
                            | false -> empty ()
                        button { attr.``type`` "button"; on.click (fun _ -> dispatch' ShowLibrary); text Texts.addDocuments }
                        projectDocuments state.Library state.ShowLibrary state.Statuses.Entries (LibraryMsg >> dispatch')
                    }
                    main {
                        "data-testid" => "main"
                        attr.``class`` "main-column"
                        chatGptBanner state.ChatGpt (ChatGptMsg >> dispatch')
                        cond state.ShowLibrary <| function
                            | true -> libraryView state.Library (LibraryMsg >> dispatch')
                            | false -> empty ()
                        cond (not state.ShowLibrary && opened.IsNone) <| function
                            | true -> p { attr.``class`` "empty-main"; text Texts.chooseDocument }
                            | false -> empty ()
                        cond state.Library.RoleError <| function
                            | Some failure ->
                                p {
                                    "role" => "alert"
                                    attr.``class`` "error"
                                    text (FailureTexts.library failure)
                                }
                            | None -> empty ()
                        cond (if state.ShowLibrary then None else opened) <| function
                            | Some association ->
                                section {
                                    let document = association.Document
                                    let role = if association.Role = FocusedSource then Texts.focusedSource else Texts.referenceSource
                                    attr.``class`` "document-workspace"
                                    div {
                                        attr.``class`` "document-heading"
                                        div {
                                            h2 { text document.FileName }
                                            p { text (Texts.documentDetails document.PageCount role projectLabel) }
                                        }
                                        a {
                                            attr.``class`` "open-pdf"
                                            attr.href document.ContentUrl
                                            "target" => "_blank"
                                            "rel" => "noopener"
                                            text Texts.openPdf
                                            span { "aria-hidden" => "true"; text "↗" }
                                        }
                                    }
                                    chatGptAccount state.ChatGpt (ChatGptMsg >> dispatch')
                                    summaryView document state.Summary state.ChatGpt (SummaryMsg >> dispatch') (ChatGptMsg >> dispatch')
                                }
                            | None -> empty ()
                    }
                }
        }

type App() =
    inherit ProgramComponent<State, Msg>()
    [<Inject>]
    member val Navigation = Unchecked.defaultof<NavigationManager> with get, set
    [<Inject>]
    member val JavaScript = Unchecked.defaultof<IJSRuntime> with get, set
    override this.OnAfterRenderAsync(firstRender) =
        let rendered = base.OnAfterRenderAsync(firstRender)
        task {
            do! rendered
            do! this.JavaScript.InvokeVoidAsync("lexScholarSendDialog.sync").AsTask()
        }
    override this.Program =
        let http = new HttpClient(BaseAddress = Uri(this.Navigation.BaseUri))
        // One app origin owns both workspace and setup. Normalize localhost before
        // reading its state or loading data, matching the fixed OAuth callback origin.
        let entryUri = Uri(this.Navigation.Uri)
        let js = this.JavaScript :?> IJSInProcessRuntime
        let remembered =
            if Uri(this.Navigation.Uri).Host = "localhost" then None
            else
                try
                    match js.Invoke<string>("sessionStorage.getItem", [| box "lexscholar.onboarding" |]) with
                    | "active" -> Some true
                    | "finished" -> Some false
                    | _ -> None
                with _ -> None
        let start _ =
            if entryUri.Host = "localhost" then
                let target = UriBuilder(entryUri)
                target.Host <- "127.0.0.1"
                target.Path <- "/index.html"
                { Projects = { initial with Busy = Some Loading }; Library = Library.initial; Summary = Summary.initial; ChatGpt = ChatGpt.initial; Statuses = SummaryStatus.initialStatuses; ShowLibrary = false; PendingProjectOpen = false; Onboarding = None },
                Cmd.ofEffect (fun _ -> this.Navigation.NavigateTo(target.Uri.ToString(), forceLoad = true, replace = true))
            else
                let library, libraryEffect = Library.update Library.LoadLibrary Library.initial
                // Returning from ChatGPT: read the outcome once, then drop it from the address.
                let uri = Uri(this.Navigation.Uri)
                let query = System.Web.HttpUtility.ParseQueryString(uri.Query)
                let returned = match query.["chatgpt"] with null -> None | value -> Some value
                if returned.IsSome then this.Navigation.NavigateTo(uri.GetLeftPart(UriPartial.Path), replace = true)
                let chatGpt, chatGptEffect = ChatGpt.update ChatGpt.Load ChatGpt.initial
                let statuses, statusEffect = SummaryStatus.updateStatuses SummaryStatus.Refresh SummaryStatus.initialStatuses
                let chatGpt, _ = ChatGpt.update (ChatGpt.Returned returned) chatGpt
                { Projects = { initial with Busy = Some Loading }; Library = library; Summary = Summary.initial; ChatGpt = chatGpt; Statuses = statuses; ShowLibrary = false; PendingProjectOpen = false; Onboarding = remembered },
                Cmd.batch [ Ui.command http Fetch |> Cmd.map ProjectsMsg
                            Ui.chatGptCommand http this.Navigation chatGptEffect
                            Ui.statusCommand http statusEffect
                            libraryEffect |> Option.map (Ui.libraryCommand http) |> Option.defaultValue Cmd.none ]
        let update msg state =
            let next, command = Ui.update http this.Navigation msg state
            if next.Onboarding <> state.Onboarding then
                try
                    js.InvokeVoid("sessionStorage.setItem", [| box "lexscholar.onboarding"; box (if next.Onboarding = Some true then "active" else "finished") |])
                with _ -> ()
            next, command
        Program.mkProgram start update Ui.view
