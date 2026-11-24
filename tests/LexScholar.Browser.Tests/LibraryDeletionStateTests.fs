module LexScholar.Browser.Tests.LibraryDeletionStateTests

open System
open LexScholar.Client.Library
open Xunit

let private document = Guid.NewGuid()
let private preview : Deletion.Preview =
    { Documents = [{ Id = document; Name = "fixture.pdf"; Projects = [] }]; Revision = "first" }
let private ready =
    { Deletion.initial with Selected = Set.singleton document; Preview = Some preview; Restored = true }

[<Fact; Trait("Task", "T017")>]
let ``cancel has no effect and confirmation freezes the exact preview`` () =
    let cancelled, effect = Deletion.update Deletion.Cancel ready
    Assert.True(effect.IsNone)
    Assert.True(cancelled.Preview.IsNone)
    let id = Guid.NewGuid()
    let submitted, effect = Deletion.update (Deletion.Confirm id) ready
    Assert.Equal(Some (Deletion.Submit { OperationId = id; DocumentIds = [document]; Revision = "first" }), effect)
    Assert.True(submitted.Busy)
    let ignored, duplicate = Deletion.update (Deletion.Confirm (Guid.NewGuid())) submitted
    Assert.Equal(submitted, ignored)
    Assert.True(duplicate.IsNone)

[<Fact; Trait("Task", "T017")>]
let ``lost answer and failed or absent lookup never permit another selection or UUID`` () =
    let id = Guid.NewGuid()
    let submitted, _ = Deletion.update (Deletion.Confirm id) ready
    let uncertain, _ = Deletion.update (Deletion.Confirmed (id, Error Deletion.Unavailable)) submitted
    let ignored, _ = Deletion.update (Deletion.Select (Guid.NewGuid())) uncertain
    Assert.Equal(uncertain, ignored)
    let loaded, _ = Deletion.update (Deletion.Loaded (0, Ok [], None)) uncertain
    Assert.Equal(uncertain.Pending, loaded.Pending)
    let retried, effect = Deletion.update Deletion.RepeatConfirmation loaded
    Assert.Equal(Some (Deletion.Submit submitted.Pending.Value), effect)
    Assert.True(retried.Busy)
    let stale, _ = Deletion.update (Deletion.Confirmed (Guid.NewGuid(), Ok ())) uncertain
    Assert.Equal(uncertain, stale)

[<Fact; Trait("Task", "T017")>]
let ``changed impact requires a fresh explicit confirmation and partial results stay incomplete`` () =
    let id = Guid.NewGuid()
    let submitted, _ = Deletion.update (Deletion.Confirm id) ready
    let fresh = { preview with Revision = "changed" }
    let changed, effect = Deletion.update (Deletion.Confirmed (id, Error (Deletion.ImpactChanged fresh))) submitted
    Assert.True(effect.IsNone)
    Assert.True(changed.Pending.IsNone)
    Assert.Equal(Some fresh, changed.Preview)
    Assert.True(changed.Changed)
    let partial : Deletion.Outcome =
        { OperationId = id; Status = "partial"
          Items = [{ DocumentId = document; Name = "fixture.pdf"; Outcome = "not_completed"; Reason = "storage_error" }] }
    let loaded, _ = Deletion.update (Deletion.Loaded (0, Ok [partial], None)) Deletion.initial
    let _, acknowledge = Deletion.update (Deletion.Acknowledge id) loaded
    Assert.True(acknowledge.IsNone)
    let _, retry = Deletion.update (Deletion.Retry id) loaded
    Assert.Equal(Some (Deletion.RetryOperation id), retry)

[<Fact; Trait("Task", "T017")>]
let ``reload restores uncertain confirmation and stale lists cannot replace newer outcomes`` () =
    let pending : Deletion.Confirmation = { OperationId = Guid.NewGuid(); DocumentIds = [document]; Revision = "first" }
    let model, _ = Deletion.update (Deletion.Loaded (0, Ok [], Some pending)) Deletion.initial
    Assert.True(model.Uncertain)
    Assert.Equal(Some pending, model.Pending)
    let model, _ = Deletion.update Deletion.Load model
    let stale, effect = Deletion.update (Deletion.Loaded (0, Ok [], None)) model
    Assert.Equal(model, stale)
    Assert.True(effect.IsNone)

[<Fact; Trait("Task", "T017")>]
let ``new selections wait for reload reconciliation and failed lookup cannot bypass it`` () =
    let ignored, effect = Deletion.update (Deletion.Select document) Deletion.initial
    Assert.Equal(Deletion.initial, ignored)
    Assert.True(effect.IsNone)
    let failed, _ = Deletion.update (Deletion.Loaded (0, Error Deletion.Unavailable, None)) Deletion.initial
    let ignored, _ = Deletion.update (Deletion.Select document) failed
    Assert.True(ignored.Selected.IsEmpty)
    Assert.False(ignored.Restored)
