# T015 — reimport and association safety

- Base: `32f39f4`, latest fetched `origin/release/0.5.0` (T014 and T007 merged).
- Environment: Linux, .NET SDK 10.0.114; isolated `TestHarness` temporary workspaces, fixture PDFs only.
- RED: `scripts/check.sh T015` — 14 tests, 3 assertion failures, zero skips. Pending-before-lookup and intent-during-comparison reused the selected identity; the promotion hold was bypassed by deduplication. Log: `/tmp/lexscholar-check.Z4ef2W`.
- GREEN: `scripts/check.sh T015 T013` — 65 passed, zero failed/skipped, Release build with `-warnaserror`.
- Regression: `scripts/check.sh T007 T010 T011 T014 T015` — 313 passed, zero failed/skipped, Release build with `-warnaserror`. Includes export lifetime, atomic admission, summary fencing and restart recovery.
- Sensitivity: temporarily disposed `processItem`'s file lease immediately after acquisition. `scripts/check.sh T015` failed the bounded unlink/import handshake (15 tests, one failed, zero skipped); mutation removed before GREEN. Log: `/tmp/lexscholar-check.0n4xTE`.
- An earlier broader run was terminated at its 120-second command limit while the intermediate nested EF query was broken. The query was corrected and the completed regression command above supersedes that run.

## Deterministic boundaries and observations

Five new cases hold a real opened original during comparison, hold after real promotion, or hold before real unlink. They demonstrate that intent committed before lookup or during comparison cannot return the old identity; fresh promotion uses a different key and UUID, and old-item processing leaves those bytes intact. Promotion excludes processing through the shared file lease without blocking registry progress. Unlink excludes a queued reimport; afterward a valid new record can be associated and ordinary association removal retains its PDF. Old-identity summary admission remains fenced. A pending selection overlapping another document rejects the whole operation, while unrelated valid imports remain intact.

Only SQLite candidate rereading runs inside the mutation monitor; file comparison/promotion and lease waits remain outside the registry lock. Existing T010/T011 regressions cover running/preparing summary cancellation and stale save prevention. No production admission, retry, processing or startup recovery was enabled; no changes were needed to `LibraryDeletion.fs` or `LibraryControlEndpoints.fs`.

## Limitations / CI

This verifies process-local coordination under the existing single-host workspace lock, not external filesystem modification or physical erasure. New destructive processing is exercised only through internal capabilities with temporary workspaces. Full suite not run locally; CI run pending a future PR (this assignment creates no push or PR).

## PR #155 review follow-up — production visibility assertion

- GPT P2 verdict: **must fix**. The original `{}` POST without antiforgery could be rejected before endpoint admission, so its non-202 assertion did not prove the production route was disabled.
- Replaced it with a separate production-host test using an existing fixture document, a fresh valid impact revision, a new operation UUID, JSON selection, same-origin header and the host's antiforgery cookie/token. The response must be exactly `404`; neither journal table may gain rows, and document identity and original bytes remain unchanged.
- Sensitivity RED: temporarily mapped `LibraryDeletionEndpoints.mapForTests` in this test's isolated host. `scripts/check.sh T015` — 16 tests, one assertion failure, zero skips: expected `NotFound`, actual `Accepted` (log `/tmp/lexscholar-check.odYg68`). This proves the request passes security/body/revision validation and would admit if the route were enabled.
- Restored ordinary production composition before GREEN: `scripts/check.sh T015` — 16 passed, zero failed/skipped; Release build with `-warnaserror`. No full suite locally; PR CI supplies full-suite verification. Production admission/retry/recovery remain disabled.
