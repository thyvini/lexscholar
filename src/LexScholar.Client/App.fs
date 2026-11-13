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

    let private summaryFailure (code: string) =
        match code with
        | "summary_service_not_configured" -> Summary.NotConfigured
        | "document_unavailable" -> Summary.Unavailable
        | "no_text" -> Summary.NoText
        | "not_enough_text" -> Summary.NotEnoughText
        | "too_many_pages" -> Summary.TooManyPages
        | "service_failed" -> Summary.ServiceFailed None
        | _ -> Summary.RequestFailed

    let private problemCode (body: string) =
        try
            use json = JsonDocument.Parse(body)
            json.RootElement.GetProperty("code").GetString()
        with _ -> ""

    /// A 409 `confirmation_required` carries the host and model to show; anything else maps by its code.
    let private confirmationOrFailure (body: string) : Summary.Failure =
        try
            use json = JsonDocument.Parse(body)
            let root = json.RootElement
            if root.GetProperty("code").GetString() = "confirmation_required" then
                Summary.ConfirmationRequired (root.GetProperty("serviceHost").GetString(), root.GetProperty("model").GetString())
            else summaryFailure (problemCode body)
        with _ -> Summary.RequestFailed

    let private summaryEvent (element: JsonElement) : Result<Summary.Generated, Summary.Failure> option =
        let pages (name: string) = element.GetProperty(name).EnumerateArray() |> Seq.map (fun page -> page.GetInt32()) |> Seq.toList
        match element.GetProperty("type").GetString() with
        | "summary" ->
            Some (Ok { Summary.DocumentId = element.GetProperty("documentId").GetGuid()
                       Summary.Points =
                           element.GetProperty("points").EnumerateArray()
                           |> Seq.map (fun point ->
                               { Summary.Text = point.GetProperty("text").GetString()
                                 Summary.Pages = point.GetProperty("pages").EnumerateArray() |> Seq.map (fun page -> page.GetInt32()) |> Seq.toList })
                           |> Seq.toList
                       Summary.PagesCovered = pages "pagesCovered"
                       Summary.UnreadablePages = pages "unreadablePages"
                       Summary.OmittedPoints = element.GetProperty("omittedPoints").GetInt32()
                       Summary.GeneratedAt = element.GetProperty("generatedAt").GetDateTimeOffset()
                       Summary.ServiceHost = element.GetProperty("serviceHost").GetString()
                       Summary.Model = element.GetProperty("model").GetString() })
        | "failed" ->
            match summaryFailure (element.GetProperty("code").GetString()) with
            | Summary.ServiceFailed _ ->
                let detail =
                    match element.TryGetProperty "detail" with
                    | true, value when value.ValueKind = JsonValueKind.String -> Some (value.GetString())
                    | _ -> None
                Some (Error (Summary.ServiceFailed detail))
            | failure -> Some (Error failure)
        | _ -> None

    /// The progress an event reports, if it is one.
    let private progressEvent (element: JsonElement) : Summary.Progress option =
        match element.GetProperty("type").GetString() with
        | "progress" -> Some (Summary.PagesProcessed (element.GetProperty("pagesProcessed").GetInt32(), element.GetProperty("totalPages").GetInt32()))
        | "combining" -> Some Summary.Combining
        | _ -> None

    /// `confirmSend` names the service the researcher confirmed, so the server can check it is still the configured one.
    let private summaryBody (confirmed: Summary.Confirmed option) =
        match confirmed with
        | Some service -> JsonSerializer.Serialize({| confirmSend = true; confirmedHost = service.Host; confirmedModel = service.Model |})
        | None -> JsonSerializer.Serialize({| confirmSend = false |})

    /// Requests a summary and reads the NDJSON stream until its one `summary` or
    /// `failed` event. Failures never show response details.
    let summarize (http: HttpClient) (projectId: Guid) (documentId: Guid) (confirmed: Summary.Confirmed option) (onProgress: Summary.Progress -> unit) (cancellation: Threading.CancellationToken) = async {
        try
            use! tokenResponse = http.GetAsync("api/antiforgery", cancellation) |> Async.AwaitTask
            if not tokenResponse.IsSuccessStatusCode then return Error Summary.RequestFailed
            else
                let! tokenBody = tokenResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
                use tokenJson = JsonDocument.Parse(tokenBody)
                use request = new HttpRequestMessage(HttpMethod.Post, $"api/projects/{projectId:D}/documents/{documentId:D}/summary")
                request.Headers.Add("X-CSRF-TOKEN", tokenJson.RootElement.GetProperty("requestToken").GetString())
                request.Content <- new StringContent(summaryBody confirmed, Encoding.UTF8, "application/json")
                request.SetBrowserResponseStreamingEnabled(true) |> ignore
                use! response = http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation) |> Async.AwaitTask
                if not response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    return Error (confirmationOrFailure body)
                else
                    use! stream = response.Content.ReadAsStreamAsync() |> Async.AwaitTask
                    use reader = new IO.StreamReader(stream)
                    let rec next () = async {
                        match! reader.ReadLineAsync(cancellation).AsTask() |> Async.AwaitTask with
                        | null -> return Error Summary.RequestFailed
                        | line when String.IsNullOrWhiteSpace line -> return! next ()
                        | line ->
                            use json = JsonDocument.Parse(line)
                            match summaryEvent json.RootElement with
                            | Some outcome -> return outcome
                            | None ->
                                progressEvent json.RootElement |> Option.iter onProgress
                                return! next () }
                    return! next ()
        with _ -> return Error Summary.RequestFailed }

/// The whole page: projects, shared library, native reader and its summary.
type State = { Projects: Model; Library: Library.Model; Reader: Reader.Model; Summary: Summary.Model }

type Msg =
    | ProjectsMsg of Message
    | LibraryMsg of Library.Message
    | ReaderMsg of Reader.Message
    | SummaryMsg of Summary.Message

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
        | Some (Summary.Send (request, projectId, documentId, confirmed)) ->
            let source = new Threading.CancellationTokenSource()
            running[request] <- source
            Cmd.ofEffect (fun dispatch ->
                Async.StartImmediate(async {
                    let! result = Api.summarize http projectId documentId confirmed (fun progress -> dispatch (SummaryMsg (Summary.Progressed (request, progress)))) source.Token
                    running.Remove request |> ignore
                    source.Dispose()
                    dispatch (SummaryMsg (Summary.Responded (request, result))) }))
        | Some (Summary.Cancel request) ->
            match running.TryGetValue request with
            | true, source -> source.Cancel()
            | _ -> ()
            Cmd.none
        | None -> Cmd.none

    let private updateCore http msg state =
        match msg with
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
    let update http msg state =
        let state, command = updateCore http msg state
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

    let summaryFailureText = Summary.failureText

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

    /// The summary controls and panel of the open document. Citations are plain
    /// page-number buttons (`button.citation`, `data-page`) that ask the reader to show that page.
    let summaryView (document: Reader.Document) (summary: Summary.Model) onCite dispatch =
        concat {
            cond summary.State <| function
                | Summary.Generating _ ->
                    span {
                        "role" => "status"
                        "Summarizing… "
                        cond summary.Progress <| function
                            | Some (Summary.PagesProcessed (processed, total)) ->
                                span { attr.``class`` "summary-progress"; text $"{processed} of {total} pages processed. " }
                            | Some Summary.Combining -> span { attr.``class`` "summary-progress"; text "Combining points. " }
                            | None -> empty ()
                        button { attr.``type`` "button"; on.click (fun _ -> dispatch Summary.Dismiss); "Dismiss" }
                    }
                | Summary.Shown _ -> empty ()
                | Summary.Confirming (host, model) ->
                    div {
                        attr.``class`` "summary-confirm"
                        "role" => "alertdialog"
                        "aria-label" => "Confirm sending"
                        p { text $"The text of {document.FileName} will be sent to {host} (model {model}), which is not on this machine. Send?" }
                        button { attr.``type`` "button"; attr.``class`` "send-confirm"; on.click (fun _ -> dispatch Summary.ConfirmSend); "Send" }
                        " "
                        button { attr.``type`` "button"; attr.``class`` "send-cancel"; on.click (fun _ -> dispatch Summary.CancelSend); "Cancel" }
                    }
                | Summary.Idle ->
                    button { attr.``type`` "button"; on.click (fun _ -> dispatch Summary.Summarize); "Summarize" }
                | Summary.Failed _ ->
                    button { attr.``type`` "button"; attr.``class`` "summary-retry"; on.click (fun _ -> dispatch Summary.Summarize); "Try again" }
            cond summary.State <| function
                | Summary.Failed failure -> p { attr.``class`` "error"; "role" => "alert"; text (summaryFailureText failure) }
                | _ -> empty ()
            cond summary.State <| function
                | Summary.Shown generated ->
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
                                    forEach point.Pages <| fun page ->
                                        button {
                                            attr.``type`` "button"
                                            attr.``class`` "citation"
                                            "data-page" => string page
                                            attr.title $"Page {page}"
                                            on.click (fun _ -> onCite page)
                                            text (string page)
                                        }
                                }
                        }
                        button { attr.``type`` "button"; on.click (fun _ -> dispatch Summary.Dismiss); "Dismiss" }
                    }
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
            { Projects = { initial with Busy = Some Loading }; Library = library; Reader = Reader.initial; Summary = Summary.initial },
            Cmd.batch [ Ui.command http Fetch |> Cmd.map ProjectsMsg
                        libraryEffect |> Option.map (Ui.libraryCommand http) |> Option.defaultValue Cmd.none ]
        Program.mkProgram start (Ui.update http) Ui.view
