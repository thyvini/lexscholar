# T001 — Library control foundations

- RED: `scripts/check.sh T001` — 44 tests, 2 failed because the deletion journal tables were absent.
- Additional RED: `scripts/check.sh T001` — 44 tests, 1 failed because an invalid status/phase combination was accepted.
- GREEN / pre-PR: `scripts/check.sh T001` — 44 tests passed, zero skipped.
- CI full suite: [run 45255783990](https://github.com/thyvini/lexscholar/actions/runs/45255783990) failed: 10 existing migration-history assertions expected the old schema; browser tests passed.
- Regression GREEN: `scripts/check.sh T001 T010 T025` — 138 tests passed, zero skipped, after updating migration and table-count expectations.
- CI full suite on the updated PR head: pending.

Tests use isolated temporary SQLite workspaces. They migrate a pre-F002 workspace with a document, association and stored summary, reopen it twice, and check journal persistence and model/migration agreement. They also check identity, active-document uniqueness, foreign keys and state constraints. This slice does not expose deletion or perform recovery; it does not test deletion crash recovery, which belongs to later tasks.
