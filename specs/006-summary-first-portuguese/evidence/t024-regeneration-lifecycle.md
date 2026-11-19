# T024 — Preserve the saved summary during replacement

- Base: `release/0.4.0`, `7e8c003`; Linux, pinned .NET 10 SDK.
- RED: `scripts/check.sh T024` — HTTP assertion failed: explicit regeneration returned 404 instead of 202 (`/tmp/lexscholar-check.ueLWss`). Initial test constructor typo was corrected before this behavioral RED.
- GREEN: `scripts/check.sh T024 T002 T003 T006 T008 T009` — warning-as-error build passed; 323 tests passed, zero failures/skips.
- Full suite: delegated to CI on the future PR; no PR opened by this slice.

Application coverage: duplicate ordinary/replacement starts; failure/retry; stale Complete/Fail/Progress; cancellation before completion and cancellation after commit; effective-token cancellation; save exception; association removal and early plan failure. These deterministically exercise both completion/cancel orderings under the unchanged shared lock, without sleeps.

HTTP/SQLite coverage: original row remains during running replacement; repeat ordinary request does not call the service again; explicit cancel and shutdown/restart retain it; failed replacement and successful replacement survive restart with the appropriate version; `/saved` reads the durable version; untrusted unsafe request is rejected before service access. Workspaces are isolated.

Storage design: no migration, no pre-delete, no second registry. Existing repository `SaveChanges` atomically updates the row; registry invokes it under its existing lock and removes the transient job only afterward. Application test injects a throwing Save; SQLite tests exercise actual successful update and service-failure preservation. Abrupt process kill, disk-full and power-loss injection are not performed; existing SQLite atomicity remains the storage guarantee.

Scope: backend lifecycle only. No feedback text/prompt or UI. T023 must forward its instructions through the shared endpoint helper for ordinary and regeneration requests; `Summarize.fs` is untouched here. Destructive DELETE remains backward compatible; future regeneration UI must use `/cancel`, not DELETE.

## PR #132 review follow-up — must fix

Verdicts: GPT P0 (legacy Stop/Close can delete the durable version), GPT P1 (cancel before registration is forgotten), and independent P1 (failed replacement masks the stored version on ordinary POST) are all **must fix**. The original base-GET progress contract above is superseded by the isolated replacement-aware contract.

- Legacy base GET, ordinary POST and status markers now show the durable finished version during/after failed replacement. Only `GET .../summary/regenerate` opts into replacement progress/failure. Existing client Stop/Close cannot be exposed by replacement work; explicit confirmed DELETE stays destructive and invalidates pending preparation.
- Each replacement HTTP request registers a scoped admission before its first await. Cancel/DELETE/removal invalidate it under the same lock as `Start`, `StartWith` and `StartFailed`. Disposing the HTTP scope removes it, without retaining tombstones. Refresh may finish saving rotated OAuth credentials, but a cancelled admission cannot launch model/summary calls.
- RED: `scripts/check.sh T024` — 8 assertion failures, zero skips, including all four Chromium/Firefox running/failed legacy-client journeys, legacy API state assertions, and deterministic extraction/plan-refresh cancellation races. Both cancellation tests previously returned 202 instead of 409 (`/tmp/lexscholar-check.mIkgIk`).
- Independent P1 sensitivity: temporarily restored replacement-inclusive ordinary POST lookup (uncommitted); `scripts/check.sh T024` — four failures, including signed-out 503 and remote-consent 409 instead of saved-summary 200 (`/tmp/lexscholar-check.QGwV2c`). Restored the fix before GREEN.
- GREEN: `scripts/check.sh T024 T002 T003 T006 T008 T009` — warning-as-error build; **336 passed, zero failures/skips**, including four real Chromium/Firefox journeys and legacy deletion regressions.
- Race handshakes: injected `PageExtraction` holds a real HTTP request inside extraction even while ignoring cancellation; `FakeOpenAi.HoldTokens` resets its arrival handshake and holds refresh after sign-in. Tests issue cancel while held, release, await the POST, assert 409, zero model/summary calls and unchanged SQLite content. Registry tests also cover all registration paths and DELETE/removal followed by a newer saved row.
- Full suite/merge checks: CI and fresh GPT review are required on the pushed PR head. No merge performed.

## Independent re-review — Started response race (must fix)

The `Started` response branch bypassed the legacy/replacement audience filter used by the other successful outcomes. An ordinary job could finish and a replacement could register before response construction, exposing replacement progress/failure to the legacy client.

- RED: `scripts/check.sh T024` — two assertion failures: ordinary 202 response contained `running`/`failed` instead of the saved `finished` summary (`/tmp/lexscholar-check.3Oo0fQ`); 38 tests, zero skips.
- Deterministic regression: retain an actual ordinary `Start` outcome, complete that job, register a held replacement (running and failed variants), then invoke the exact production response helper. No sleeps, polling or test-only timing hook. Assert ordinary 202 contains the original finished summary, replacement-aware responses retain running/failed state, and storage is unchanged.
- Fix: all successful outcomes share one state lookup with `includeReplacement = regenerate`; only the HTTP status differs.
- GREEN: `scripts/check.sh T024 T002 T003 T006 T008 T009` — warning-as-error build; **338 passed, zero failures/skips**. CI/full-suite and fresh review remain required on the pushed head. No merge performed.
