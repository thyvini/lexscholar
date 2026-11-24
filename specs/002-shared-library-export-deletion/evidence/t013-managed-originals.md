# T013 — Managed-original removal

- Base: `origin/release/0.5.0` at `3760dc3` (T012/T006 merged).
- Environment: Linux 7.3.5-2-cachyos, .NET SDK 10.0.114, isolated temporary SQLite/filesystem workspaces only.
- RED: `scripts/check.sh T013` — build succeeded; 44 tests, 8 assertion/runtime failures, zero skips. File/finalization stubs could not complete or preserve incomplete outcomes (log `/tmp/lexscholar-check.WkXFaM`). Initial compile-only issues were corrected before capturing this RED.
- GREEN: `scripts/check.sh T013 T012 T006` — warning-as-error build passed; 150 tests passed, zero failed/skipped (final code run).
- CI full suite: pending; no push or PR requested. CI must run on the eventual PR head.

## Safety verified

- Metadata still present or intent absent cannot authorize unlink. Shared associations and stored summaries are absent before completion; unassociated originals also complete.
- Read-only managed original unlinked, directory fsynced, then completion committed. Checkpoints observe `deleted` is not visible during unlink/sync.
- Injected failure before/after unlink and before/after directory sync leaves `incomplete` with retained `metadata_removed` phase; missing-file retries repeat directory sync and succeed idempotently.
- Injected pre/post completion commit errors: fresh durable read distinguishes rollback from lost acknowledgement. Already-deleted retries work.
- Permission capability failure is sanitized, never treated as missing. Native nonfile unlink failure and missing-directory open failure also fail closed.
- Invalid/traversal/corrupt retained keys cannot remove outside files. Unrelated original stays unchanged; symlink target stays unchanged; a symlinked originals directory is rejected.
- Actual file-lease queue handshake holds processing before metadata or unlink, while mutation/registry work can progress; no sleep or readiness polling.

## Limits and visibility

Fault injection is deterministic exception injection, not physical power loss or real disk exhaustion. Successful Linux fsync is the supported durability boundary; hardware/storage failures beyond it are not claimed. Permission failure is capability-injected so the test works even with elevated privileges. Process termination/restart across these boundaries belongs to T014. External hostile mutation of the private workspace is outside app-managed immutability guarantees. Production endpoints, confirmation/retry and startup recovery remain unchanged and disabled; only isolated tests compose `processItem`.

## PR #153 GPT P1 — terminal completed retry

- Verdict: **must fix**. Re-running unlink/sync for a durable `deleted` item could touch a reused name or regress completion when storage became unavailable.
- RED: `scripts/check.sh T013` — 50 tests, 1 failed, zero skipped; the new completed-retry test received storage failure instead of success when its filesystem capability threw (`/tmp/lexscholar-check.f3WQyD`).
- Fix: read terminal `deleted` before filesystem work in the existing SQLite transaction. The fresh error-recovery transaction also returns success for durable `deleted`, never overwriting it with `incomplete`; this preserves completion after uncertain commit or another processor's committed completion.
- GREEN: `scripts/check.sh T013 T012 T006` — warning-as-error build passed; 151 tests passed, zero failed/skipped.
- Regression asserts zero filesystem and completion-checkpoint calls, unchanged durable completion/reason, preservation of a later file at the same name, and successful retry with the originals directory unavailable. Existing pre/post commit fault tests remain green. Only unfinished missing-file retries repeat directory sync.
- Full CI/review on the new PR head remains pending; production deletion remains disabled.

## T008 release integration

- Integrated `origin/release/0.5.0` at `2fab144` into the T013 branch after T008 merged. Resolved the codebase-guide conflict preserving public export, metadata deletion and original-deletion sections; retained both research decisions and checked task/evidence entries.
- `scripts/check.sh T013 T012 T008` — warning-as-error build passed; 119 tests passed, zero failed/skipped, including targeted browser journeys. Production export is enabled by T008; production deletion remains disabled.
