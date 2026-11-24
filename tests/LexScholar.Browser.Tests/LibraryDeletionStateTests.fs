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

[<Fact; Trait("Task", "T017")>]
let ``definitive admission rejection releases the frozen request without claiming deletion`` () =
    let id = Guid.NewGuid()
    let submitted, _ = Deletion.update (Deletion.Confirm id) ready
    let rejected, _ = Deletion.update (Deletion.Confirmed (id, Error Deletion.NoIntent)) submitted
    Assert.True(rejected.Pending.IsNone)
    Assert.True(rejected.Preview.IsNone)
    Assert.True(rejected.Selected.IsEmpty)
    Assert.False(rejected.Uncertain)
    Assert.Empty(rejected.Outcomes)

[<Fact; Trait("Task", "T017")>]
let ``restored matching outcome cannot be acknowledged before pending request is reconciled`` () =
    let id = Guid.NewGuid()
    let pending : Deletion.Confirmation = { OperationId = id; DocumentIds = [document]; Revision = "first" }
    let complete : Deletion.Outcome =
        { OperationId = id; Status = "complete"
          Items = [{ DocumentId = document; Name = "fixture.pdf"; Outcome = "deleted"; Reason = null }] }
    let restored, _ = Deletion.update (Deletion.Loaded (0, Ok [complete], Some pending)) Deletion.initial
    let _, effect = Deletion.update (Deletion.Acknowledge id) restored
    Assert.True(effect.IsNone)

[<Fact; Trait("Task", "T017")>]
let ``library refresh prunes vanished unconfirmed selections but preserves frozen confirmation`` () =
    let model = { initial with Deletion = { ready with Preview = None }; LibraryRequest = 1 }
    let loaded, _ = update (LibraryLoaded (1, Ok [])) model
    Assert.True(loaded.Deletion.Selected.IsEmpty)
    let frozen, _ = update (LibraryLoaded (1, Ok [])) { model with Deletion = ready }
    Assert.Equal(ready.Preview, frozen.Deletion.Preview)

[<Fact; Trait("Task", "T017")>]
let ``results fetch cannot erase a failed mutation error`` () =
    let failed, _ = Deletion.update (Deletion.Mutated (Error Deletion.Unavailable)) Deletion.initial
    let loaded, _ = Deletion.update (Deletion.Loaded (failed.Request, Ok [], None)) failed
    Assert.True(loaded.Error)

[<Fact; Trait("Task", "T017")>]
let ``only exact admission rejection codes prove no intent not lookup 404 or transport failures`` () =
    for status, body in
        [ 400, """{"code":"invalid_selection"}"""; 404, """{"code":"document_not_found"}"""
          409, """{"code":"operation_conflict"}"""; 409, """{"code":"deletion_in_progress"}""" ] do
        Assert.Equal(Deletion.NoIntent, Deletion.admissionFailure status body)
    for status, body in
        [ 404, """{"code":"operation_not_found"}"""; 404, "{}"; 404, "not JSON"
          403, """{"code":"document_not_found"}"""; 500, """{"code":"document_not_found"}"""
          409, """{"code":"unknown"}""" ] do
        Assert.Equal(Deletion.Unavailable, Deletion.admissionFailure status body)

[<Fact; Trait("Task", "T017")>]
let ``matching partial or running operation clears pending only after storage acknowledgement`` () =
    let id = Guid.NewGuid()
    let pending : Deletion.Confirmation = { OperationId = id; DocumentIds = [document]; Revision = "first" }
    for status in ["partial"; "running"; "complete"] do
        let outcome : Deletion.Outcome =
            { OperationId = id; Status = status
              Items = [{ DocumentId = document; Name = "fixture.pdf"; Outcome = "not_completed"; Reason = "pending" }] }
        let restored, effect = Deletion.update (Deletion.Loaded (0, Ok [outcome], Some pending)) Deletion.initial
        Assert.Equal(Some (Deletion.ForgetConfirmation pending), effect)
        Assert.True(restored.Busy)
        let failed, _ = Deletion.update (Deletion.Reconciled (id, false)) restored
        Assert.Equal(Some pending, failed.Pending)
        Assert.True(failed.Error)
        let cleared, _ = Deletion.update (Deletion.Reconciled (id, true)) restored
        Assert.True(cleared.Pending.IsNone)
        Assert.False(cleared.Busy)
        Assert.False(cleared.Uncertain)
        let stale, _ = Deletion.update (Deletion.Reconciled (id, true)) { restored with Pending = Some { pending with OperationId = Guid.NewGuid() } }
        Assert.True(stale.Pending.IsSome)

[<Fact; Trait("Task", "T017")>]
let ``same UUID with different items cannot reconcile a frozen request`` () =
    let id = Guid.NewGuid()
    let pending : Deletion.Confirmation = { OperationId = id; DocumentIds = [document]; Revision = "first" }
    let outcome : Deletion.Outcome =
        { OperationId = id; Status = "complete"
          Items = [{ DocumentId = Guid.NewGuid(); Name = "other.pdf"; Outcome = "deleted"; Reason = null }] }
    let loaded, effect = Deletion.update (Deletion.Loaded (0, Ok [outcome], Some pending)) Deletion.initial
    Assert.Equal(Some pending, loaded.Pending)
    Assert.True(effect.IsNone)
