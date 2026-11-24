# T009 — Non-destructive deletion impact preview

- Environment: Linux, repository-pinned .NET 10 SDK, real SQLite WAL, isolated `TemporaryWorkspace` per test; synthetic metadata and fixture-only original bytes.
- RED: `scripts/check.sh T009` — build passed; 12 failing assertions against a temporary 501 preview stub (expected 200/400/404). One production-visibility test initially omitted antiforgery and correctly received 403; corrected the test to send valid security headers.
- Snapshot sensitivity: `scripts/check.sh T009` — 80 tests, one failed assertion after temporarily replacing the supplied snapshot context with a fresh connection in `LibraryDeletionRepository.read`. The coherent-impact assertion detected the committed second-connection changes. Mutation removed, never committed.
- GREEN: `scripts/check.sh T009` — build with `-warnaserror` passed; 80 task-tagged tests passed, zero failures/skips (14 new T009 cases; task IDs also select existing feature tests).
- Full-suite CI: pending the integrator's PR; no push or PR requested for this worktree.

## Verified boundaries

Malformed, empty, duplicate, empty-Guid and unknown selections fail safely. Shared documents show both project names/roles and each stored-summary flag; unassociated documents remain eligible. Selection ordering leaves the revision unchanged. Selected names, project names, roles, associations, summary addition/removal, raw content/version and generation time change it.

A deterministic second connection commits document/project/role/association/summary changes after snapshot establishment and before impact queries; the impact and digest still match the complete earlier state, and a subsequent capture sees the changes. No sleeps or readiness polling.

The preview response contains neither summary text nor storage keys/paths. Successful preview leaves database bytes and managed original bytes unchanged, with no operation/item journal writes. Valid authenticated/antiforgery-protected POSTs to preview and confirmation on the ordinary host return 404: only isolated tests invoke `mapForTests`.

## Scope and limitations

No production composition, confirmation, job fencing, intent persistence, retry, file removal or recovery is implemented. Portuguese permanence/external-copy/cancellation explanations remain T017 UI work; T009 supplies the non-destructive impact data only. T010 will recompute and compare the revision under mutation coordination; this preview alone is not deletion authorization. Broader security/log and browser verification remains T018.

Shared integration files changed only to register the new compile units: Application, Infrastructure, Server and Server.Tests `.fsproj`, plus the codebase guide and this task checkbox. Integrate these serially with lane A; `Endpoints.fs`, `TestHarness.fs`, export modules and `SummaryRepository.fs` were not changed.
