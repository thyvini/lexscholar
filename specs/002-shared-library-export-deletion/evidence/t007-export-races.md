# T007 — Coordinated export races

## Environment and boundary

- Linux, pinned .NET 10 SDK, real SQLite/filesystem adapters and isolated `TemporaryWorkspace` hosts.
- Public export remains disabled. Tests map the existing test-only export endpoint.
- No new implementation or technology choice: T003's shared file lease and T002's captured snapshot already provide the required behavior.
- Handshakes observe a real opened original, the shared lease's queued-wait event, post-promotion/pre-insertion state, and registry job admission. All barriers have failure bounds and explicit release; no sleeps or readiness polling.

## Verification

Existing behavior requires sensitivity checks, not a claimed new-behavior RED:

1. Temporarily bypassed the export file lease in `Application/LibraryExport.fs`.
   - `LOG=/tmp/opencode/t007-sensitivity-lease.log scripts/check.sh T007`
   - **RED:** 159 tests, 3 failed, 0 skipped. Both copy-held import cases and the promotion-held export case observed completed HTTP requests instead of the required queued lease handshake.
2. Restored the lease; temporarily wrote the manifest from a second database capture after PDF copying.
   - `LOG=/tmp/opencode/t007-sensitivity-snapshot.log scripts/check.sh T007`
   - **RED:** 159 tests, 2 failed, 0 skipped. Concurrent association removal leaked into the archive: expected two captured associations, got one.
3. Restored both deliberate defects.
   - `LOG=/tmp/opencode/t007-green.log scripts/check.sh T007`
   - **GREEN:** warning-as-error solution build; 159 task-tagged tests, 0 failed, 0 skipped (including the three new T007 cases). Existing features reuse task IDs, so this command also runs their T007 regressions; it is not the full suite.

## Assertions and limitations

- During a real open-original copy, host uploads may stage/validate, but both duplicate reuse and new-document promotion/insertion queue behind export. A simulated unlink also waits on that same boundary.
- Role change, association removal and successful replacement completion progress while export holds the file lease and blocks on PDF I/O. Registry capability work observes the mutation monitor held. A newly admitted regeneration remains uncancelled through successful export.
- Despite those committed changes, manifest roles/associations, stored summary points, readable summary text and exact PDF bytes all retain the earlier captured state. New import bytes are excluded from this archive.
- After construction, simulated metadata deletion/unlink can proceed; the complete ZIP still contains the original bytes and disposal leaves no private archive staging.
- With a real import paused after managed-file promotion but before insertion, export queues without blocking registry/database work. After release it captures the committed document and exact promoted bytes, not an intermediate state.
- Destructive deletion is deliberately simulated under the existing lease/mutation boundary, not enabled in production. Durable intent, recovery, and reimport/deletion semantics remain T011–T016 work; this evidence does not claim those behaviors.
- No crash or disk fault injection here: T006 covers export construction/transfer faults and cleanup; this slice covers controlled concurrency.

CI full suite: pending the eventual PR (not pushed/opened by this implementation task).
