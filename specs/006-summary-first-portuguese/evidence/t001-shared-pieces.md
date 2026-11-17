# T001 — Shared pieces

- RED: `scripts/check.sh T001` — Chromium and Firefox failed: HTML language was `en`, not `pt-BR`.
- GREEN: `scripts/check.sh T001` — 42 tests passed, zero skipped, including both browsers.
- Reader helper regression: `scripts/check.sh T040` — 12 tests passed, zero skipped.
- Sensitivity: temporarily changed the existing signed-in account wording; `scripts/check.sh T001` failed the new account assertion in both browsers. Restored the original wording and reran GREEN.
- CI full suite: [run 43990678473](https://github.com/thyvini/lexscholar/actions/runs/43990678473) passed on implementation commit `5b1069c`.
