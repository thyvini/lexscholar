# T006 evidence: finished summary stored, shown after restart, dismiss deletes

Risky (storage). Covers FR-009, FR-010, SC-007. AR 2, AR 7, AR 8 apply (running jobs and failures are not stored; no export yet, F002 `$12y`).

## Environment

- Linux 7.3.3 (CachyOS), .NET SDK 10.0.114 (`global.json`), Release build with `-warnaserror`.
- SQLite via EF Core with the F001 migrations; `DocumentSummaries` from T001 (no new migration). Server tests use `TemporaryWorkspace`; restarts reuse it with `TestHarness.Start(workspace = ...)` (in-process host, same data directory and database file).

## RED

`scripts/check.sh T006` at commit 66dcfae: `Tests: 56 total, 8 failed` — assertion failures: `find` returned `None` and `listKeys` `[]` (repository stubs), no exception for an unknown version, `GET` 404 after restart, `DELETE` 501, the Dismiss effect missing.

## GREEN

`scripts/check.sh T001 T002 T003 T004 T005 T006`: `Tests: 319 total, 1 failed` — the T001 placeholder assertion `DELETE` = 501 (now 204 where nothing is kept); updated, then `scripts/check.sh T001 T006`: `Tests: 89 total, 0 failed, 0 skipped`. CI runs the full suite on the PR.

## Sensitivity checks (uncommitted, reverted)

- `Dismiss` without `capabilities.Delete`: `dismissing a finished summary deletes it for good and she can ask again` fails (1 of 56).
- `Complete` without `capabilities.Save`: the restart test, the dismiss test and F003's multi-chunk test fail (3 of 56).

## Limitations

- "Save outside the lock" is not deterministically testable: the window is a few microseconds between the save and the job being dropped. The ordering (save, then drop, inside one lock) is by construction and by the lock-held `Dismiss`/`Start`/`State` reads; it is not proven by a test.
- If `Save` throws (for example the association was just removed, FK), the job stays running for `runJob` to fail it as `service_failed`; nothing is stored.
- No browser journey was added: the dismiss flow is covered by the state test (effect) and the contract tests (DELETE), not end to end in a browser.
