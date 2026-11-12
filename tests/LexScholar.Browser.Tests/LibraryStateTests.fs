module LexScholar.Browser.Tests.LibraryStateTests

open System
open System.IO
open System.Threading
open Microsoft.AspNetCore.Components.Forms
open Xunit
open LexScholar.Domain
open LexScholar.Client.Library

/// Stands in for a file chosen in the browser; the state never reads it.
type private FakeFile(name: string) =
    interface IBrowserFile with
        member _.Name = name
        member _.LastModified = DateTimeOffset.UnixEpoch
        member _.Size = 0L
        member _.ContentType = "application/pdf"
        member _.OpenReadStream(_: int64, _: CancellationToken) : Stream = new MemoryStream() :> Stream

let private step message model = update message model
let private alpha = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
let private beta = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
let private paper = { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"); FileName = "paper.pdf"; PageCount = 2; ContentUrl = "/api/documents/11111111-1111-1111-1111-111111111111/content" }
let private notes = { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"); FileName = "notes.pdf"; PageCount = 5; ContentUrl = "/api/documents/22222222-2222-2222-2222-222222222222/content" }

let private assertState<'a when 'a: equality> (expected: 'a) (actual: 'a) =
    Assert.True((expected = actual), sprintf "Expected: %A\nActual: %A" expected actual)

/// The request number of a library fetch effect.
let private libraryRequest effect =
    match effect with
    | Some (FetchLibrary request) -> request
    | other -> failwithf "Expected a library fetch, got %A." other

/// The project and request number of a project fetch effect.
let private projectRequest effect =
    match effect with
    | Some (FetchProject (projectId, request)) -> projectId, request
    | other -> failwithf "Expected a project fetch, got %A." other

/// Delivers a project response for the given fetch effect.
let private projectLoaded effect result model =
    let projectId, request = projectRequest effect
    step (ProjectLoaded (projectId, request, result)) model

/// A model showing the given project with the given associations and library.
let private showing project associations documents =
    let model, effect = step (ActiveProjectChanged (Some project)) initial
    let model, _ = projectLoaded effect (Ok associations) model
    let model, effect = step LoadLibrary model
    let model, _ = step (LibraryLoaded (libraryRequest effect, Ok documents)) model
    model

[<Fact; Trait("Task", "T032")>]
let ``loading the library requests it and shows the documents`` () =
    let model, effect = step LoadLibrary initial
    let request = libraryRequest effect
    Assert.True(model.LoadingLibrary)
    let model, effect = step (LibraryLoaded (request, Ok [ paper; notes ])) model
    assertState None effect
    assertState [ paper; notes ] model.Documents
    Assert.True(model.HasLoadedLibrary)
    Assert.False(model.LoadingLibrary)

[<Fact; Trait("Task", "T032")>]
let ``switching projects clears the previous project's documents and loads the new one`` () =
    let model = showing alpha [ { Document = paper; Role = FocusedSource } ] [ paper ]
    let model, effect = step (ActiveProjectChanged (Some beta)) model
    assertState beta (fst (projectRequest effect))
    assertState (Some beta) model.Project
    Assert.Empty(model.Associations)
    Assert.False(model.HasLoadedProject)
    Assert.True(model.LoadingProject)
    let model, _ = projectLoaded effect (Ok [ { Document = paper; Role = ReferenceSource } ]) model
    assertState [ { Document = paper; Role = ReferenceSource } ] model.Associations
    Assert.True(model.HasLoadedProject)

[<Fact; Trait("Task", "T032")>]
let ``a late response for a previously active project is ignored`` () =
    let model, alphaFetch = step (ActiveProjectChanged (Some alpha)) initial
    let model, _ = step (ActiveProjectChanged (Some beta)) model
    let after, effect = projectLoaded alphaFetch (Ok [ { Document = paper; Role = FocusedSource } ]) model
    assertState None effect
    assertState model after
    Assert.Empty(after.Associations)

[<Fact; Trait("Task", "T032")>]
let ``an empty project is loaded with no documents and no project shows nothing`` () =
    let model, effect = step (ActiveProjectChanged (Some alpha)) initial
    let model, _ = projectLoaded effect (Ok []) model
    Assert.True(model.HasLoadedProject)
    Assert.Empty(model.Associations)
    let same, effect = step (ActiveProjectChanged (Some alpha)) model
    assertState None effect
    assertState model same
    let model, effect = step (ActiveProjectChanged None) model
    assertState None effect
    assertState None model.Project
    Assert.Empty(model.Associations)
    Assert.False(model.LoadingProject)

[<Fact; Trait("Task", "T032")>]
let ``an imported upload is reported and refreshes the library`` () =
    let file = FakeFile("paper.pdf") :> IBrowserFile
    let model, effect = step (Upload file) initial
    assertState (Some (SendUpload file)) effect
    assertState (Some Uploading) model.Busy
    let model, effect = step (Uploaded (Ok (Imported paper))) model
    libraryRequest effect |> ignore
    assertState (Some (Imported paper)) model.Notice
    assertState None model.Busy

[<Fact; Trait("Task", "T032")>]
let ``a reused upload is reported as already in the library`` () =
    let model, _ = step (Upload (FakeFile("copy.pdf"))) initial
    let model, effect = step (Uploaded (Ok (Reused paper))) model
    libraryRequest effect |> ignore
    assertState (Some (Reused paper)) model.Notice

[<Fact; Trait("Task", "T032")>]
let ``a rejected upload shows its failure and a new action clears it`` () =
    let model, _ = step (Upload (FakeFile("notes.txt"))) initial
    let model, effect = step (Uploaded (Error NotAPdf)) model
    assertState None effect
    assertState (Some NotAPdf) model.Error
    assertState None model.Busy
    let model, _ = step (Upload (FakeFile("paper.pdf"))) model
    assertState None model.Error

[<Fact; Trait("Task", "T032")>]
let ``associating sends the chosen role for the active project and refreshes it`` () =
    let model = showing alpha [] [ paper ]
    let model, effect = step (Associate (paper.Id, ReferenceSource)) model
    assertState (Some (SendAssociate (alpha, paper.Id, ReferenceSource))) effect
    assertState (Some Associating) model.Busy
    let model, effect = step (Associated (alpha, Ok ())) model
    assertState alpha (fst (projectRequest effect))
    assertState None model.Busy
    Assert.True(model.LoadingProject)

[<Fact; Trait("Task", "T032")>]
let ``associating needs an active project`` () =
    let model, effect = step LoadLibrary initial
    let model, _ = step (LibraryLoaded (libraryRequest effect, Ok [ paper ])) model
    let same, effect = step (Associate (paper.Id, FocusedSource)) model
    assertState None effect
    assertState model same

[<Fact; Trait("Task", "T032")>]
let ``a duplicate association shows its failure without refreshing`` () =
    let model = showing alpha [ { Document = paper; Role = FocusedSource } ] [ paper ]
    let model, _ = step (Associate (paper.Id, ReferenceSource)) model
    let model, effect = step (Associated (alpha, Error DuplicateAssociation)) model
    assertState None effect
    assertState (Some DuplicateAssociation) model.Error
    assertState None model.Busy

[<Fact; Trait("Task", "T032")>]
let ``removing an association refreshes the active project`` () =
    let model = showing alpha [ { Document = paper; Role = FocusedSource } ] [ paper ]
    let model, effect = step (Remove paper.Id) model
    assertState (Some (SendRemove (alpha, paper.Id))) effect
    assertState (Some Removing) model.Busy
    let model, effect = step (Removed (alpha, paper.Id, Ok ())) model
    assertState alpha (fst (projectRequest effect))
    assertState None model.Busy

[<Fact; Trait("Task", "T032")>]
let ``a successful removal drops the association at once, even if the reload fails`` () =
    let model = showing alpha [ { Document = paper; Role = FocusedSource }; { Document = notes; Role = ReferenceSource } ] [ paper; notes ]
    let ids (model: Model) = model.Associations |> List.map (fun item -> item.Document.Id)
    let model, _ = step (Remove paper.Id) model
    let model, effect = step (Removed (alpha, paper.Id, Ok ())) model
    assertState [ notes.Id ] (ids model)
    let model, _ = projectLoaded effect (Error RequestFailed) model
    assertState [ notes.Id ] (ids model)
    assertState (Some RequestFailed) model.Error

[<Fact; Trait("Task", "T032")>]
let ``a mutation result after a project switch does not refresh the new project`` () =
    let model = showing alpha [ { Document = paper; Role = FocusedSource } ] [ paper ]
    let model, _ = step (Remove paper.Id) model
    let model, _ = step (ActiveProjectChanged (Some beta)) model
    let model, effect = step (Removed (alpha, paper.Id, Ok ())) model
    assertState None effect
    assertState None model.Busy
    assertState (Some beta) model.Project

[<Fact; Trait("Task", "T032")>]
let ``one mutation at a time: repeated actions are ignored while one is pending`` () =
    let model = showing alpha [] [ paper ]
    let model, _ = step (Associate (paper.Id, FocusedSource)) model
    for message in [ Associate (paper.Id, ReferenceSource); Remove paper.Id; Upload (FakeFile("paper.pdf")) ] do
        let same, effect = step message model
        assertState None effect
        assertState model same

[<Fact; Trait("Task", "T032")>]
let ``HTTP problem codes are mapped to understandable failures`` () =
    let problem code = $"{{\"code\":\"{code}\"}}"
    let cases =
        [ 422, problem "not_a_pdf", NotAPdf
          400, problem "not_a_pdf", NotAPdf
          422, problem "corrupt_pdf", CorruptPdf
          422, problem "encrypted_pdf", EncryptedPdf
          413, problem "upload_too_large", TooLarge
          422, problem "page_limit_exceeded", TooManyPages
          409, problem "duplicate_association", DuplicateAssociation
          400, problem "invalid_role", InvalidRole
          404, problem "not_found", NotFound
          404, problem "document_not_found", DocumentNotFound
          500, problem "internal_error", RequestFailed
          500, "secret internal detail", RequestFailed
          409, problem "not_a_pdf", RequestFailed ]
    for status, body, expected in cases do
        assertState (status, expected) (status, parseFailure status body)

// Review follow-ups (PR #26): only the latest response for each list is shown,
// and Refresh reloads both lists.

[<Fact; Trait("Task", "T032")>]
let ``an older library response arriving last does not overwrite the newer list`` () =
    let model, first = step LoadLibrary initial
    let model, second = step (Uploaded (Ok (Imported notes))) { model with Busy = Some Uploading }
    let model, _ = step (LibraryLoaded (libraryRequest second, Ok [ paper; notes ])) model
    let model, effect = step (LibraryLoaded (libraryRequest first, Ok [ paper ])) model
    assertState None effect
    assertState [ paper; notes ] model.Documents

[<Fact; Trait("Task", "T032")>]
let ``an older project response arriving last does not overwrite the newer list`` () =
    let model = showing alpha [] [ paper; notes ]
    // Two changes in a row: the first refresh is still in flight when the second starts.
    let model, _ = step (Associate (paper.Id, FocusedSource)) model
    let model, first = step (Associated (alpha, Ok ())) model
    let model, _ = step (Associate (notes.Id, ReferenceSource)) model
    let model, second = step (Associated (alpha, Ok ())) model
    let both = [ { Document = paper; Role = FocusedSource }; { Document = notes; Role = ReferenceSource } ]
    let model, _ = projectLoaded second (Ok both) model
    let model, effect = projectLoaded first (Ok [ { Document = paper; Role = FocusedSource } ]) model
    assertState None effect
    assertState both model.Associations

[<Fact; Trait("Task", "T032")>]
let ``switching away and back ignores the first request for the same project`` () =
    let model, firstAlpha = step (ActiveProjectChanged (Some alpha)) initial
    let model, _ = step (ActiveProjectChanged (Some beta)) model
    let model, secondAlpha = step (ActiveProjectChanged (Some alpha)) model
    let current = [ { Document = paper; Role = ReferenceSource } ]
    let model, _ = projectLoaded secondAlpha (Ok current) model
    let model, effect = projectLoaded firstAlpha (Ok []) model
    assertState None effect
    assertState current model.Associations
    Assert.True(model.HasLoadedProject)

[<Fact; Trait("Task", "T032")>]
let ``refresh reloads the library and the active project's documents after a failure`` () =
    let model, effect = step (ActiveProjectChanged (Some alpha)) initial
    let model, _ = projectLoaded effect (Error RequestFailed) model
    let model, effect = step LoadLibrary model
    let model, _ = step (LibraryLoaded (libraryRequest effect, Error RequestFailed)) model
    let model, effect = step Refresh model
    match effect with
    | Some (FetchAll (library, Some (projectId, project))) ->
        assertState alpha projectId
        Assert.True(model.LoadingLibrary)
        Assert.True(model.LoadingProject)
        assertState None model.Error
        let model, _ = step (LibraryLoaded (library, Ok [ paper ])) model
        let model, _ = step (ProjectLoaded (alpha, project, Ok [ { Document = paper; Role = FocusedSource } ])) model
        assertState [ paper ] model.Documents
        assertState [ { Document = paper; Role = FocusedSource } ] model.Associations
    | other -> failwithf "Expected both lists to be fetched, got %A." other
    // Without an active project only the library is reloaded.
    let _, effect = step Refresh initial
    match effect with
    | Some (FetchAll (_, None)) -> ()
    | other -> failwithf "Expected only the library to be fetched, got %A." other

[<Fact; Trait("Task", "T039")>]
let ``reloading the project refetches only the active project's documents`` () =
    let model = showing alpha [ { Document = paper; Role = FocusedSource } ] [ paper ]
    let model, effect = step ReloadProject model
    let projectId, request = projectRequest effect
    assertState alpha projectId
    Assert.True(model.LoadingProject)
    let updated = [ { Document = paper; Role = ReferenceSource } ]
    let model, _ = step (ProjectLoaded (alpha, request, Ok updated)) model
    assertState updated model.Associations
    let _, effect = step ReloadProject initial
    assertState None effect
