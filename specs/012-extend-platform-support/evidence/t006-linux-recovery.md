# T006 — Linux recovery/restart gap assessment

Partial only: Darwin completion depends on verified T002 + T005 and native candidate results. No Mac completion or power-loss guarantee is claimed.

## Existing coverage and narrow additions

- `RestartTests` already checks 100 graceful in-process restarts, including projects, roles, associations, active selection and served-content hashes (T041). Added a real child host: confirm project/import/association/selection through HTTP, capture the workspace including served PDF hash, forcibly terminate, then reopen the same workspace and compare it unchanged (T006).
- `LibraryDeletionRecoveryTests` already exits a real child at ten intent/metadata/unlink/sync/completion checkpoints and verifies durable phase, recovery and terminal restart (T014). Extended that theory with an unselected managed original, its associations/summaries and byte-for-byte survival after recovery and repeated startup; retained the T014 tag and added T006.
- Existing recovery error tests cover storage failures and uncertain commits. Added an explicit pre-sync failure after unlink: startup must leave `incomplete`/`storage_error`, not success; the next ordinary startup retries missing-file removal plus sync to `deleted`, and repeated startup retains the durable result and unrelated bytes.

No production changes, lock bypass, readiness polling or new test seam. The sync fault uses the existing file checkpoint; it establishes exception handling at that boundary, not a real kernel `fsync` failure or power-loss survival.

## Verification

- Initial GREEN: `scripts/check.sh T006` — warning-free build, 88 tests passed, zero skipped.
- Sensitivity RED: `LOG=/tmp/opencode/f012-t006-sensitivity.log scripts/check.sh T006` — warning-free build, 88 tests, 12 assertion failures, zero skipped. Deliberately changed the persisted project name after termination (snapshot assertion failed), corrupted the unselected original in the crash child (all ten byte-survival cases failed), and suppressed the injected pre-sync error (expected `incomplete`, actual `deleted`). These were temporary mutations in the owned test files only, not production changes, and were removed.
- Final GREEN: `LOG=/tmp/opencode/f012-t006-green.log scripts/check.sh T006 T014 T017 T041` — warning-free build, 159 tests passed, zero skipped.

Environment: Linux x86-64, kernel `7.3.5-2-cachyos`, .NET SDK `10.0.114`; isolated temporary workspace on local tmpfs. This is not durable-media evidence.

## Remaining gates

- T006 stays unchecked; Darwin runs require the verified lock/deletion candidates and evidence on the resulting exact revision.
- Full-suite CI and `gpt-review` passed on [PR #208](https://github.com/thyvini/lexscholar/pull/208) head `39531b3`; [CI run](https://github.com/thyvini/lexscholar/actions/runs/45936386506). The merged release head `9b89742` [CI run](https://github.com/thyvini/lexscholar/actions/runs/45938481859) was in progress when recorded.
- Prior T005 candidate-workflow provider safety rejection remains recorded in its evidence. It was not retried here. No new provider safety rejection occurred during this work.
