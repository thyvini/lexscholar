module LexScholar.Browser.Tests.ReaderStateTests

open System
open Xunit
open LexScholar.Domain
open LexScholar.Client.Reader

let private step message model = update message model
let private alpha = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
let private beta = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
let private paper = { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"); FileName = "paper.pdf"; ContentUrl = "/api/documents/11111111-1111-1111-1111-111111111111/content" }
let private notes = { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"); FileName = "notes.pdf"; ContentUrl = "/api/documents/22222222-2222-2222-2222-222222222222/content" }

let private assertState<'a when 'a: equality> (expected: 'a) (actual: 'a) =
    Assert.True((expected = actual), sprintf "Expected: %A\nActual: %A" expected actual)

/// The request number and URL of a content check effect.
let private check effect =
    match effect with
    | Some (CheckContent (request, url)) -> request, url
    | other -> failwithf "Expected a content check, got %A." other

let private inProject project = step (ActiveProjectChanged (Some project)) initial |> fst

/// A model showing the given document in the given project.
let private showing project document =
    let model, effect = step (Open document) (inProject project)
    let request, _ = check effect
    step (ContentChecked (request, Ok ())) model |> fst

[<Fact; Trait("Task", "T039")>]
let ``opening a document checks its content before showing it`` () =
    let model, effect = step (Open paper) (inProject alpha)
    let _, url = check effect
    assertState paper.ContentUrl url
    assertState (Some paper) model.Opening
    assertState None model.Open

[<Fact; Trait("Task", "T039")>]
let ``an available document is shown in the viewer`` () =
    let model, effect = step (Open paper) (inProject alpha)
    let request, _ = check effect
    let model, effect = step (ContentChecked (request, Ok ())) model
    assertState None effect
    assertState (Some paper) model.Open
    assertState None model.Opening
    assertState None model.Error

[<Fact; Trait("Task", "T039")>]
let ``an unavailable document shows the failure and no viewer, and opening again clears it`` () =
    let model, effect = step (Open paper) (inProject alpha)
    let request, _ = check effect
    let model, _ = step (ContentChecked (request, Error Unavailable)) model
    assertState (Some Unavailable) model.Error
    assertState None model.Open
    assertState None model.Opening
    let model, effect = step (Open paper) model
    check effect |> ignore
    assertState None model.Error

[<Fact; Trait("Task", "T039")>]
let ``only the latest content check is used`` () =
    let model, first = step (Open paper) (inProject alpha)
    let model, second = step (Open notes) model
    let model, _ = step (ContentChecked (fst (check second), Ok ())) model
    let model, effect = step (ContentChecked (fst (check first), Error Unavailable)) model
    assertState None effect
    assertState (Some notes) model.Open
    assertState None model.Error

[<Fact; Trait("Task", "T039")>]
let ``switching projects closes the viewer and ignores a pending check`` () =
    let model = showing alpha paper
    let model, _ = step (ActiveProjectChanged (Some beta)) model
    assertState None model.Open
    let model, pending = step (Open notes) model
    let model, _ = step (ActiveProjectChanged (Some alpha)) model
    let model, effect = step (ContentChecked (fst (check pending), Ok ())) model
    assertState None effect
    assertState None model.Open
    assertState None model.Opening

[<Fact; Trait("Task", "T039")>]
let ``the viewer closes when its document is no longer associated with the project`` () =
    let model = showing alpha paper
    let same, _ = step (AssociationsChanged [ paper.Id; notes.Id ]) model
    assertState model same
    let model, effect = step (AssociationsChanged [ notes.Id ]) model
    assertState None effect
    assertState None model.Open

[<Fact; Trait("Task", "T039")>]
let ``closing hides the viewer`` () =
    let model, _ = step Close (showing alpha paper)
    assertState None model.Open

[<Fact; Trait("Task", "T039")>]
let ``opening needs an active project`` () =
    let model, effect = step (Open paper) initial
    assertState None effect
    assertState initial model

[<Fact; Trait("Task", "T039")>]
let ``changing a role sends it for the active project and refreshes the project`` () =
    let model, effect = step (ChangeRole (paper.Id, ReferenceSource)) (inProject alpha)
    assertState (Some (SendRole (alpha, paper.Id, ReferenceSource))) effect
    assertState (Some paper.Id) model.ChangingRole
    let model, effect = step (RoleChanged (alpha, Ok ())) model
    assertState (Some RefreshProject) effect
    assertState None model.ChangingRole

[<Fact; Trait("Task", "T039")>]
let ``a failed role change shows its failure, and a result after a switch is ignored`` () =
    let model, _ = step (ChangeRole (paper.Id, ReferenceSource)) (inProject alpha)
    let model, effect = step (RoleChanged (alpha, Error AssociationNotFound)) model
    assertState None effect
    assertState (Some AssociationNotFound) model.Error
    let model, _ = step (ChangeRole (paper.Id, FocusedSource)) model
    let model, _ = step (ActiveProjectChanged (Some beta)) model
    let model, effect = step (RoleChanged (alpha, Ok ())) model
    assertState None effect
    assertState None model.ChangingRole

[<Fact; Trait("Task", "T039")>]
let ``one role change at a time`` () =
    let model, _ = step (ChangeRole (paper.Id, ReferenceSource)) (inProject alpha)
    let same, effect = step (ChangeRole (notes.Id, FocusedSource)) model
    assertState None effect
    assertState model same
    let none, effect = step (ChangeRole (paper.Id, FocusedSource)) initial
    assertState None effect
    assertState initial none

[<Fact; Trait("Task", "T039")>]
let ``HTTP problem codes are mapped to understandable failures`` () =
    let problem code = $"{{\"code\":\"{code}\"}}"
    let cases =
        [ 503, problem "document_unavailable", Unavailable
          404, problem "document_not_found", DocumentNotFound
          404, problem "not_found", AssociationNotFound
          400, problem "invalid_role", InvalidRole
          500, problem "internal_error", RequestFailed
          503, "secret internal detail", RequestFailed
          404, problem "document_unavailable", RequestFailed ]
    for status, body, expected in cases do
        assertState (status, expected) (status, parseFailure status body)

[<Fact; Trait("Task", "T039")>]
let ``a role failure from a previous visit to the same project is ignored`` () =
    let model, _ = step (ChangeRole (paper.Id, ReferenceSource)) (inProject alpha)
    let model, _ = step (ActiveProjectChanged (Some beta)) model
    let model, _ = step (ActiveProjectChanged (Some alpha)) model
    let model, effect = step (RoleChanged (alpha, Error AssociationNotFound)) model
    assertState None model.Error
    assertState None model.ChangingRole
    assertState None effect

[<Fact; Trait("Task", "T039")>]
let ``a removed document cannot reopen from a pending availability response`` () =
    let model, pending = step (Open paper) (inProject alpha)
    let model, _ = step (AssociationsChanged []) model
    let model, _ = step (ContentChecked (fst (check pending), Ok ())) model
    assertState None model.Open
    assertState None model.Opening

[<Fact; Trait("Task", "T039")>]
let ``a successful role change after returning to its project refreshes without replacing the new reader state`` () =
    let model, _ = step (ChangeRole (paper.Id, ReferenceSource)) (inProject alpha)
    let model, _ = step (ActiveProjectChanged (Some beta)) model
    let model, _ = step (ActiveProjectChanged (Some alpha)) model
    // The return visit can load associations before the pending PATCH commits.
    let model, _ = step (AssociationsChanged [ paper.Id; notes.Id ]) model
    let model, pending = step (Open notes) model
    let model, _ = step (ContentChecked (fst (check pending), Ok ())) model
    let refreshed, effect = step (RoleChanged (alpha, Ok ())) model
    assertState (Some RefreshProject) effect
    assertState (Some notes) refreshed.Open
    assertState model.Project refreshed.Project
    assertState model.CheckRequest refreshed.CheckRequest
    assertState model.Error refreshed.Error
    assertState None refreshed.ChangingRole

// Removing the open document. The page steps the library, then the reader
// follows it (App.fs), so these tests drive both the same way.

module private Page =
    module L = LexScholar.Client.Library

    let association (document: Document) : L.Association =
        { Document = { Id = document.Id; FileName = document.FileName; PageCount = 2; ContentUrl = document.ContentUrl }
          Role = FocusedSource }

    let fetch effect =
        match effect with
        | Some (L.FetchProject (projectId, request)) -> projectId, request
        | other -> failwithf "Expected a project fetch, got %A." other

    let step message (library, reader) =
        let library, effect = L.update message library
        (library, followLibrary library reader), effect

    /// Project alpha with paper and notes loaded; the reader shows nothing yet.
    let loaded () =
        let page, effect = step (L.ActiveProjectChanged (Some alpha)) (L.initial, initial)
        let projectId, request = fetch effect
        step (L.ProjectLoaded (projectId, request, Ok [ association paper; association notes ])) page |> fst

    /// Removes the document; the server accepts it. Returns the page and the reload effect.
    let removed (document: Document) page =
        let page, _ = step (L.Remove document.Id) page
        step (L.Removed (alpha, document.Id, Ok ())) page

[<Fact; Trait("Task", "T039")>]
let ``removing the open document closes the reader even if the project reload fails`` () =
    let library, reader = Page.loaded ()
    let reader, effect = step (Open paper) reader
    let reader, _ = step (ContentChecked (fst (check effect), Ok ())) reader
    assertState (Some paper) reader.Open
    let (library, reader), reload = Page.removed paper (library, reader)
    assertState None reader.Open
    let projectId, request = Page.fetch reload
    let failed = LexScholar.Client.Library.ProjectLoaded (projectId, request, Error LexScholar.Client.Library.RequestFailed)
    let (_, reader), _ = Page.step failed (library, reader)
    assertState None reader.Open
    assertState None reader.Opening

[<Fact; Trait("Task", "T039")>]
let ``a content check that returns after the removal does not reopen the document`` () =
    let library, reader = Page.loaded ()
    let reader, pending = step (Open paper) reader
    let (_, reader), _ = Page.removed paper (library, reader)
    let reader, _ = step (ContentChecked (fst (check pending), Ok ())) reader
    assertState None reader.Open
    assertState None reader.Opening

[<Fact; Trait("Task", "T005")>]
let ``showing a cited page records it and counts the navigation`` () =
    let model, effect = step (ShowPage 3) (showing alpha paper)
    assertState None effect
    assertState (Some 3) model.Page
    assertState 1 model.Navigation

[<Fact; Trait("Task", "T005")>]
let ``showing the same page again counts a new navigation`` () =
    let model = showing alpha paper |> step (ShowPage 3) |> fst |> step (ShowPage 3) |> fst
    assertState (Some 3) model.Page
    assertState 2 model.Navigation

[<Fact; Trait("Task", "T005")>]
let ``a page request without an open document changes nothing`` () =
    let model = inProject alpha
    assertState (model, None) (step (ShowPage 2) model)

[<Fact; Trait("Task", "T005")>]
let ``opening or closing a document forgets the cited page`` () =
    let cited = showing alpha paper |> step (ShowPage 3) |> fst
    assertState None (step (Open notes) cited |> fst).Page
    assertState None (step Close cited |> fst).Page
