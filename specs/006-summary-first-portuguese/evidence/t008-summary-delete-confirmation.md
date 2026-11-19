# T008 — Summary deletion confirmation

- RED: `scripts/check.sh T008` — two browser assertions failed: the finished summary had no "Excluir" button.
- GREEN: `scripts/check.sh T008 T013 T007 T010` — 242 tests passed, zero skipped; warning-as-error build passed. Includes Chromium and Firefox, actual host restart over the same workspace, and F004 stop/in-flight regressions.
- CI full suite: pending PR `build-and-test`.
