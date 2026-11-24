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

Access denial is injected rather than changing machine permissions. Abrupt process exit tests application/SQLite crash recovery, not physical power loss, controller caches or hardware faults. No physical secure-erasure claim. If the journal cannot be read, startup aborts before orphan cleanup; if failure-marker persistence is itself impossible, the existing durable intent is retained without claiming completion.
