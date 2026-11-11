module LexScholar.Browser.Tests.ProjectStateTests

open System
open Xunit
open LexScholar.Client.Projects

let step msg model = update msg model
let id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
let snapshot = { Projects = [ { Id = id; Name = "First" } ]; Active = Some id }

[<Fact; Trait("Task", "T018")>]
let ``initial load starts empty and requests projects`` () =
    let model, effect = step Reload initial
    Assert.Empty(model.Snapshot.Projects)
    Assert.Equal(Some Loading, model.Busy)
    Assert.Equal(Some Fetch, effect)

[<Fact; Trait("Task", "T018")>]
let ``load identifies active project`` () =
    let model, _ = step Reload initial
    let model, _ = step (Loaded (Ok snapshot)) model
    Assert.Equal(Some id, model.Snapshot.Active)
    Assert.True(snapshot.Projects = model.Snapshot.Projects)
    Assert.Equal(None, model.Busy)

[<Fact; Trait("Task", "T018")>]
let ``creation refreshes and prevents duplicate submissions`` () =
    let model, _ = step (NameChanged "First") initial
    let model, effect = step Create model
    Assert.Equal(Some (SendCreate "First"), effect)
    let unchanged, effect = step Create model
    Assert.Equal(None, effect)
    Assert.Equal(model, unchanged)
    let model, effect = step (Created (Ok ())) model
    Assert.Equal(Some Fetch, effect)
    Assert.Equal(Some Loading, model.Busy)
    Assert.Equal("", model.Name)

[<Fact; Trait("Task", "T018")>]
let ``selection refreshes after success`` () =
    let model, _ = step (Select id) initial
    Assert.Equal(Some Selecting, model.Busy)
    let model, effect = step (Selected (Ok ())) model
    Assert.Equal(Some Loading, model.Busy)
    Assert.Equal(Some Fetch, effect)

[<Fact; Trait("Task", "T018")>]
let ``validation duplicate failure and recovery`` () =
    let model, _ = step (NameChanged "  ") initial
    let model, effect = step Create model
    Assert.Equal(None, effect)
    Assert.Equal(Some InvalidName, model.Error)
    let model, _ = step (NameChanged "first") model
    let model, _ = step Create model
    let model, _ = step (Created (Error DuplicateName)) model
    Assert.Equal(Some DuplicateName, model.Error)
    let model, _ = step (NameChanged "second") model
    Assert.Equal(None, model.Error)
    let model, effect = step Create model
    Assert.Equal(Some Creating, model.Busy)
    Assert.Equal(Some (SendCreate "second"), effect)

[<Fact; Trait("Task", "T018")>]
let ``load failure can be retried`` () =
    let model, _ = step Reload initial
    let model, _ = step (Loaded (Error RequestFailed)) model
    Assert.Equal(Some RequestFailed, model.Error)
    let model, effect = step Reload model
    Assert.Equal(None, model.Error)
    Assert.Equal(Some Fetch, effect)

[<Fact; Trait("Task", "T018")>]
let ``HTTP problem codes are mapped safely`` () =
    let parse = LexScholar.Client.Api.parseFailure
    Assert.Equal(DuplicateName, parse System.Net.HttpStatusCode.Conflict "{\"code\":\"duplicate_project_name\"}")
    Assert.Equal(InvalidName, parse System.Net.HttpStatusCode.BadRequest "{\"code\":\"invalid_name\"}")
    Assert.Equal(NotFound, parse System.Net.HttpStatusCode.NotFound "{\"code\":\"not_found\"}")
    Assert.Equal(RequestFailed, parse System.Net.HttpStatusCode.InternalServerError "secret internal detail")

[<Fact; Trait("Task", "T018")>]
let ``unexpected late results and repeated selections cannot overwrite state`` () =
    let model, _ = step (Select id) initial
    let same, effect = step (Select (Guid.NewGuid())) model
    Assert.Equal(model, same)
    Assert.Equal(None, effect)
    let same, _ = step (Loaded (Ok snapshot)) same
    Assert.Equal(model, same)
    let model, _ = step (Selected (Error NotFound)) model
    Assert.Equal(Some NotFound, model.Error)
    let model, effect = step Reload model
    Assert.Equal(None, model.Error)
    Assert.Equal(Some Fetch, effect)
