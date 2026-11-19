# T011 — Local dates and remaining texts

- RED / old-format sensitivity: `scripts/check.sh T011` — the historical-summary context assertion failed in Chromium and Firefox: it displayed `2026-11-14 18:54` instead of `14/11/2026 18:54`.
- GREEN: `scripts/check.sh T011` — build passed with warnings as errors; 37 tests passed, zero skipped.
- Full suite: CI on the PR.
- SC-003's manual walk through all interface states is deferred to T012.
