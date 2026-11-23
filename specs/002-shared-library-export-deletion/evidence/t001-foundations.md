# T001 — Library control foundations

- RED: `scripts/check.sh T001` — 44 tests, 2 failed because the deletion journal tables were absent.
- Additional RED: `scripts/check.sh T001` — 44 tests, 1 failed because an invalid status/phase combination was accepted.
- GREEN / pre-PR: `scripts/check.sh T001` — 44 tests passed, zero skipped.
- CI full suite: pending PR run.

Tests use isolated temporary SQLite workspaces. They migrate a pre-F002 workspace with a document, association and stored summary, reopen it twice, and check journal persistence and model/migration agreement. They also check identity, active-document uniqueness, foreign keys and state constraints. This slice does not expose deletion or perform recovery; it does not test deletion crash recovery, which belongs to later tasks.
