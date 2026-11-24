# T014 — Interrupted deletion recovery before orphan cleanup

## Verification

- Base: `origin/release/0.5.0` at `c96ce62` (T013 and T006 merged).
- RED: `scripts/check.sh T014` — 23 total, 1 assertion failure, zero skips: metadata-removed original was incorrectly quarantined on ordinary startup.
- RED with recovery seam/no-op implementation: `scripts/check.sh T014` — 37 total, 12 assertion failures, zero skips: committed intents did not recover, failed recovery did not retain incomplete state, and deletion originals were quarantined.
- GREEN/final code verification: `scripts/check.sh T014 T012 T013 T006` — 192 total, zero failures/skips; solution build uses `-warnaserror`. T014 supplies 19 cases; runner smoke cases and the selected prerequisite tests are included in the command total.
- An intermediate expanded-fixture run failed on an invalid role literal; corrected to the existing `ReferenceSource` contract before GREEN.
- CI full-suite run: pending integrator PR; not run locally. No push or PR created by this slice.

## Risk evidence

Environment: Linux `7.3.5-2-cachyos`, .NET SDK `10.0.114`, ext4, isolated temporary workspaces, synthetic bytes and summary rows only.

- Child process exits immediately with code 73 after durable intent; during association removal; before/after metadata commit; before/after unlink; before/after directory sync; before/after completion commit. `Environment.Exit` bypasses catch/finally/disposal, so SQLite's rollback/recovery and committed states are observed by a fresh connection before host restart. No sleeps or readiness polling.
- Same-workspace startup resumes each interrupted item, removes the shared document's two associations and summaries and original, never quarantines it, and remains terminal on a second restart. A pre-intent workspace remains untouched.
- Injected exceptions before/after metadata and completion commits exercise existing fresh-transaction reconciliation: a committed acknowledgement loss completes; rollback retains incomplete and a later restart completes.
- Injected original-access denial leaves sanitized `storage_error`/`incomplete`, retains the original outside quarantine, permits another item's recovery and preserves an unselected original, associations and summaries. Removing the injected fault allows the next startup to complete.
- Ordinary startup performs no destructive recovery. Explicit test DI is ignored in Production. Cleanup still excludes unfinished journal keys even when recovery is disabled, while unrelated or reused unreferenced names retain F001 quarantine semantics.

## Scope and limitations

Recovery runs only through explicit test DI in `Testing`, under the startup data-directory lock, after migrations and before cleanup/requests. There are no live summary jobs or file users at this boundary; normal processing's file-lease/registry capabilities are not required. T017 must enable production recovery alongside admission/retry. No public route or configuration switch enables it here.

Access denial is injected rather than changing machine permissions. Abrupt process exit tests application/SQLite crash recovery, not physical power loss, controller caches or hardware faults. No physical secure-erasure claim. If the journal cannot be read, all orphan cleanup is skipped without blocking workspace access (PR #154 follow-up below); if failure-marker persistence is itself impossible, the existing durable intent is retained without claiming completion.

## PR #154 P1 — Journal read failure must not block workspace startup

Verdict: **must fix**. The initial implementation preserved originals by failing startup, but that unnecessarily prevented access to unrelated workspace data.

- Deterministic fault: rename `DeletionItems` in an isolated migrated SQLite workspace, retaining the journal rows under another name. This produces a real journal-query failure without breaking migrations or other workspace tables. Restore the table afterward to verify intent preservation and resumed orphan cleanup.
- RED: `scripts/check.sh T014` — 43 total, 2 assertion failures, zero skips. Both cleanup and production host-construction tests expected no exception but observed SQLite `no such table: DeletionItems`.
- GREEN/final code verification: `scripts/check.sh T014 T012 T013 T006` — 194 total, zero failures/skips; warning-as-error solution build.
- Cleanup returns `DeletionJournalReadFailed`, no quarantine/conflict results, and leaves both the retained-intent original and an unrelated orphan untouched. Restoring readable journal state permits the unrelated orphan to be quarantined while retaining the pending original.
- A real Production host starts and answers `GET /api/projects` with 200 despite journal-read failure. Its warning is exactly the sanitized fixed message, carries no exception, storage key, SQLite details or path. Production recovery remains disabled, and restored intent still reads `pending`.
- CI full suite/re-review: pending pushed follow-up commit on PR #154; not run locally.
