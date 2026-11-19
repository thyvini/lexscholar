# T015 — Readable project titles

- RED: `scripts/check.sh T015` — the new browser regression failed in Chromium and Firefox because the project title and metadata did not have separate rows.
- GREEN: `scripts/check.sh T015 T002 T001 T007 T008 T009` — 354 passed, 0 failed, 0 skipped; build passed with warnings as errors.
- Full-suite CI: pending PR.
