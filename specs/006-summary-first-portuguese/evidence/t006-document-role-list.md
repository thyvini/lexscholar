# T006 — Role change in the document list

- RED: `scripts/check.sh T006` — both browser journeys failed because the Portuguese role action was absent.
- GREEN: `scripts/check.sh T006 T004` — build passed with warnings as errors; 107 tests passed, none skipped.
- Full suite: CI on the PR.
