# T010 — Atomic, test-only deletion admission

## Environment and boundary

- Linux, .NET 10 pinned SDK, real SQLite, isolated `TemporaryWorkspace` hosts.
- Based on `origin/release/0.5.0` at `d475d0d` (T009 merged).
- Confirmation is mapped only by `LibraryDeletionEndpoints.mapForTests` in isolated tests. Production confirmation and retry return 404 and create no intent. No metadata/file deletion or startup recovery is implemented/enabled.
- Compare and admission use the host-owned T003 mutation monitor, then an immediate SQLite write transaction. No await, PDF/file I/O or registry-lock acquisition occurs during admission.

## RED / GREEN

- RED: `scripts/check.sh T010` — 8 assertion failures (confirmation returned 404 instead of 202/409/400); 72 selected tests, zero skips. Log: `/tmp/lexscholar-check.GKVD0g`.
- Pending-write RED: `scripts/check.sh T010` — pending-item role mutation unexpectedly succeeded; 1 assertion failure, 73 selected tests, zero skips. Log: `/tmp/lexscholar-check.ZtFgLq`.
- Guard sensitivity: temporarily bypassed `unlessPending`, uncommitted; `scripts/check.sh T010` — ordinary summary DELETE unexpectedly succeeded; 1 assertion failure, 76 selected tests, zero skips. Log: `/tmp/lexscholar-check.ruJsSX`. Bypass restored.
- Final GREEN: `scripts/check.sh T010 T009 T003` — build with `-warnaserror` passed; **242 tests passed, 0 failed, 0 skipped**. No full suite run locally.
- T005 integration GREEN: after merging latest `origin/release/0.5.0`, `scripts/check.sh T010 T009 T005` — warning-free build; **219 tests passed, 0 failed, 0 skipped**. An initial invocation hit the tool's 120-second timeout during server tests; the longer-timeout rerun passed.

## Verified

- Shared and unassociated selections persist one operation and every pending item, including original name/key, while retaining document rows, associations, summaries and original bytes.
- A SQLite trigger aborts insertion of the second item, proving operation/all-item rollback and unchanged library metadata.
- Changed project name, role, association, raw summary content/version or generation time returns `409 impact_changed` with fresh preview and no intent.
- Empty, duplicate, zero, malformed and unknown IDs are rejected without journaling; a revision for a different selection cannot authorize admission.
- Same operation/selection retries (including reordered IDs and a stale revision) remain idempotent after restart. Different selection conflicts. Concurrent same-ID requests create one operation; competing tabs/overlap create no partial admission.
- Pending documents reject new associations, role/removal changes and summary DELETE; unrelated association changes still work. Host summary save/delete capabilities are guarded without editing T005-owned `SummaryRepository.fs`.

## Limits and CI

T011 owns cancelling/fencing running and preparing summary work and deterministic send/extraction races. T012–T016 own destructive processing, crash recovery and outcomes. This is admission only, not proof of completed deletion. No power-loss durability claim is made by the transaction-failure test.

CI full-suite run: pending coordinator PR (this work was committed without push or PR by request). Merge still requires CI and GPT review on the eventual PR head.
