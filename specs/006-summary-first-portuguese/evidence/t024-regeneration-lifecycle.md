# T024 — Preserve the saved summary during replacement

- Base: `release/0.4.0`, `7e8c003`; Linux, pinned .NET 10 SDK.
- RED: `scripts/check.sh T024` — HTTP assertion failed: explicit regeneration returned 404 instead of 202 (`/tmp/lexscholar-check.ueLWss`). Initial test constructor typo was corrected before this behavioral RED.
- GREEN: `scripts/check.sh T024 T002 T003 T006 T008 T009` — warning-as-error build passed; 323 tests passed, zero failures/skips.
- Full suite: delegated to CI on the future PR; no PR opened by this slice.

Application coverage: duplicate ordinary/replacement starts; failure/retry; stale Complete/Fail/Progress; cancellation before completion and cancellation after commit; effective-token cancellation; save exception; association removal and early plan failure. These deterministically exercise both completion/cancel orderings under the unchanged shared lock, without sleeps.

HTTP/SQLite coverage: original row remains during running replacement; repeat ordinary request does not call the service again; explicit cancel and shutdown/restart retain it; failed replacement and successful replacement survive restart with the appropriate version; `/saved` reads the durable version; untrusted unsafe request is rejected before service access. Workspaces are isolated.

Storage design: no migration, no pre-delete, no second registry. Existing repository `SaveChanges` atomically updates the row; registry invokes it under its existing lock and removes the transient job only afterward. Application test injects a throwing Save; SQLite tests exercise actual successful update and service-failure preservation. Abrupt process kill, disk-full and power-loss injection are not performed; existing SQLite atomicity remains the storage guarantee.

Scope: backend lifecycle only. No feedback text/prompt or UI. T023 must forward its instructions through the shared endpoint helper for ordinary and regeneration requests; `Summarize.fs` is untouched here. Destructive DELETE remains backward compatible; future regeneration UI must use `/cancel`, not DELETE.
