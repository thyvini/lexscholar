# T012 — durable per-document metadata deletion

## Verification

- RED: `scripts/check.sh T012` — build passed; 23 task-filtered tests, four assertion failures against the non-destructive repository stub (metadata removal returned storage error; failure status remained pending). Log: `/tmp/lexscholar-check.F4bjqs`.
- GREEN: `scripts/check.sh T012 T010 T011` — warning-as-error Release build passed; 146 tests, zero failures/skips. Includes six T012 cases (one two-case theory), admission and summary-fencing regressions plus historical tests sharing these task IDs.
- Full suite/CI: not run locally as instructed; CI verification is pending the coordinator's PR.

## Boundaries exercised

Linux, .NET 10 pinned SDK, isolated temporary SQLite WAL workspaces and synthetic fixture metadata/files only. No researcher data.

- Shared document with two project associations and stored summaries, plus an unassociated selected document: association cascade, document deletion and journal `metadata_removed` commit together. Journal keeps its original storage key and never claims `deleted`; original files remain.
- Independent WAL reads at the pre-commit barrier see all original associations, summaries, document and pending phase. Durable reads after commit/reinitialization see completed metadata removal; repeated processing is idempotent.
- Injected exceptions after association deletion and immediately before commit roll back all metadata changes. Fresh read records only sanitized `storage_error`, `incomplete`/`pending`; another selected item progresses independently, and retry succeeds.
- Injected lost acknowledgement immediately after the actual SQLite commit returns success only after a new context/transaction confirms the durable phase and absence of document, associations and summaries.
- Unselected document, both of its associations and summaries, and project rows remain intact. Unknown operation cannot authorize deletion or preparation cancellation.
- Workflow rechecks intent then fences preparations under workspace → registry → SQLite order. Checkpoints verify the shared workspace boundary and selected-token cancellation while an unrelated preparation remains live.

## Limits and production gate

No production endpoint/startup composition changed: destructive processing remains disabled through T016. These internal capabilities must be composed with the same host-owned coordination/registry; `LibraryDeletion.removeMetadata` performs the intent check/fencing before the repository transaction. No file lease or original unlink occurs in this slice (T013/T015 own those boundaries).

Commit uncertainty is simulated at the actual post-commit boundary, not by a physical power failure. Actual process termination/recovery belongs to T014. If SQLite cannot persist even the incomplete marker, processing returns storage error and leaves durable intent for later recovery; no false success is inferred. File absence/completed deletion and public result/retry remain T013/T016.
