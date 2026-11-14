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

    /// Requests a summary and reads the NDJSON stream until its one `summary` or
    /// `failed` event. Failures never show response details.
    let summarize (http: HttpClient) (projectId: Guid) (documentId: Guid) (destination: Summary.Destination option) (confirmed: Summary.Confirmed option) (onProgress: Summary.Progress -> unit) (cancellation: Threading.CancellationToken) = async {
        try
            use! tokenResponse = http.GetAsync("api/antiforgery", cancellation) |> Async.AwaitTask
            if not tokenResponse.IsSuccessStatusCode then return Error Summary.RequestFailed
            else
                let! tokenBody = tokenResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
                use tokenJson = JsonDocument.Parse(tokenBody)
                use request = new HttpRequestMessage(HttpMethod.Post, $"api/projects/{projectId:D}/documents/{documentId:D}/summary")
                request.Headers.Add("X-CSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString())
                request.Content <- new StringContent(SummaryStream.requestBody destination confirmed, Encoding.UTF8, "application/json")
                request.SetBrowserResponseStreamingEnabled(true) |> ignore
                use! response = http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation) |> Async.AwaitTask
                if not response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    return Error (SummaryStream.failureOfBody body)
                else
                    use! stream = response.Content.ReadAsStreamAsync() |> Async.AwaitTask
                    use reader = new IO.StreamReader(stream)
                    let rec next () = async {
                        match! reader.ReadLineAsync(cancellation).AsTask() |> Async.AwaitTask with
                        | null -> return Error Summary.RequestFailed
                        | line when String.IsNullOrWhiteSpace line -> return! next ()
                        | line ->
                            use json = JsonDocument.Parse(line)
                            match SummaryStream.outcome json.RootElement with
                            | Some outcome -> return outcome
                            | None ->
                                SummaryStream.progress json.RootElement |> Option.iter onProgress
                                return! next () }
                    return! next ()
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
                return Ok { ChatGpt.Account = account; ChatGpt.Configured = None; ChatGpt.WelcomePending = root.GetProperty("welcomePending").GetBoolean() }
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
type State = { Projects: Model; Library: Library.Model; Reader: Reader.Model; Summary: Summary.Model; ChatGpt: ChatGpt.Model }

type Msg =
    | ProjectsMsg of Message
    | LibraryMsg of Library.Message
    | ReaderMsg of Reader.Message
    | SummaryMsg of Summary.Message
    | ChatGptMsg of ChatGpt.Message

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
        let state = { state with Reader = reader }
        match effect with
        | Some Reader.RefreshProject -> updateLibrary http Library.ReloadProject state
        | Some (Reader.CheckContent (request, url)) ->
            state, Cmd.OfAsync.perform (Api.checkContent http) url (fun result -> ReaderMsg (Reader.ContentChecked (request, result)))
        | Some (Reader.SendRole (projectId, documentId, role)) ->
            state, Cmd.OfAsync.perform (fun () -> Api.changeRole http projectId documentId role) () (fun result -> ReaderMsg (Reader.RoleChanged (projectId, result)))
        | None -> state, Cmd.none

    /// The requests in flight, by request number, so a `Cancel` effect can abort the fetch (FR-009).
    let private running = Collections.Generic.Dictionary<int, Threading.CancellationTokenSource>()

    let private summaryCommand http (effect: Summary.Effect option) : Cmd<Msg> =
        match effect with
        | Some (Summary.Send (request, projectId, documentId, destination, confirmed)) ->
            let source = new Threading.CancellationTokenSource()
            running[request] <- source
            Cmd.ofEffect (fun dispatch ->
                Async.StartImmediate(async {
                    let! result = Api.summarize http projectId documentId destination confirmed (fun progress -> dispatch (SummaryMsg (Summary.Progressed (request, progress)))) source.Token
                    running.Remove request |> ignore
                    source.Dispose()
                    dispatch (SummaryMsg (Summary.Responded (request, result))) }))
        | Some (Summary.Cancel request) ->
            match running.TryGetValue request with
            | true, source -> source.Cancel()
            | _ -> ()
            Cmd.none
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
        | ChatGptMsg message ->
            let chatGpt, effect = ChatGpt.update message state.ChatGpt
            { state with ChatGpt = chatGpt }, chatGptCommand http navigation effect
        | SummaryMsg message ->
            let summary, effect = Summary.update message state.Summary
            { state with Summary = summary },
            match effect with
            | effect -> summaryCommand http effect
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
        let state, command = updateCore http navigation msg state
        let summary, effect = Summary.followReader state.Reader state.Summary
        { state with Summary = summary }, Cmd.batch [ command; summaryCommand http effect ]

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

    let libraryView (library: Library.Model) (reader: Reader.Model) readerDispatch dispatch =
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
        $"{document.FileName}, covering {Summary.pageRanges generated.PagesCovered}.{unreadable} Generated {generatedAt} by {generated.Model} at {generated.ServiceHost}.{omitted}"

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

    let private confirmDialog (document: Reader.Document) host model dispatch =
        div {
            attr.``class`` "summary-confirm"
            "role" => "alertdialog"
            "aria-label" => "Confirm sending"
            p { text $"The text of {document.FileName} will be sent to {host} (model {model}), which is not on this machine. Send?" }
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

    let summaryView (document: Reader.Document) (summary: Summary.Model) onCite dispatch =
        concat {
            cond summary.State <| function
                | Summary.Generating _ ->
                    span {
                        "role" => "status"
                        "Summarizing… "
                        progressText summary.Progress
                        plainButton "Dismiss" None (fun _ -> dispatch Summary.Dismiss)
                    }
                | Summary.Shown _ -> empty ()
                | Summary.Confirming (Summary.Service (host, model)) -> confirmDialog document host model dispatch
                | Summary.Confirming (Summary.Plan _) -> empty ()
                | Summary.Idle -> plainButton "Summarize" None (fun _ -> dispatch Summary.Summarize)
                | Summary.Failed _ -> plainButton "Try again" (Some "summary-retry") (fun _ -> dispatch Summary.Summarize)
            cond summary.State <| function
                | Summary.Failed failure -> p { attr.``class`` "error"; "role" => "alert"; text (Summary.failureText failure) }
                | _ -> empty ()
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
            chatGptBanner state.ChatGpt (ChatGptMsg >> dispatch')
            libraryView state.Library state.Reader (ReaderMsg >> dispatch') (LibraryMsg >> dispatch')
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
            cond state.Reader.Open <| function
                | Some document ->
                    section {
                        attr.``class`` "card reader"
                        h2 { "Reader" }
                        p { text document.FileName }
                        p { "Use your browser's PDF viewer controls to navigate pages." }
                        a { attr.href document.ContentUrl; "target" => "_blank"; "rel" => "noopener"; "Open in new tab" }
                        button { attr.``type`` "button"; on.click (fun _ -> dispatch' (ReaderMsg Reader.Close)); "Close" }
                        chatGptAccount state.ChatGpt (ChatGptMsg >> dispatch')
                        summaryView document state.Summary (ReaderMsg << Reader.ShowPage >> dispatch') (SummaryMsg >> dispatch')
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
            let chatGpt, _ = ChatGpt.update (ChatGpt.Returned returned) chatGpt
            { Projects = { initial with Busy = Some Loading }; Library = library; Reader = Reader.initial; Summary = Summary.initial; ChatGpt = chatGpt },
            Cmd.batch [ Ui.command http Fetch |> Cmd.map ProjectsMsg
                        Ui.chatGptCommand http this.Navigation chatGptEffect
                        libraryEffect |> Option.map (Ui.libraryCommand http) |> Option.defaultValue Cmd.none ]
        Program.mkProgram start (Ui.update http this.Navigation) Ui.view
