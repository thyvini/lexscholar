# T003 — Library operation coordination

- RED: `scripts/check.sh T003` — 80 tests, 1 failed: summary persistence did not enter the workspace mutation boundary.
- Review sensitivity: `scripts/check.sh T003` — 1 failed when import used a different file coordination object; restored afterward.
- Review sensitivity: `scripts/check.sh T003` — 1 failed when the host summary registry used a different mutation boundary; restored afterward.
- Review sensitivity: `scripts/check.sh T003` — 1 timed out when extraction was deliberately run while holding the host mutation boundary; restored afterward.
- Review GREEN: `scripts/check.sh T003 T009 T021 T024` — 230 tests passed, zero skipped.
- Full CI: [PR #143](https://github.com/thyvini/lexscholar/actions/runs/45285769117) — passed on `8e5fd15`, before these review changes.

Tests use in-memory capabilities and an isolated host workspace with a fixture PDF. The import test holds the host's file lease and observes the import's queued wait before asserting that it has not committed; after release, import completes. A second test uses the actual host registry, checks its lock holds the host mutation boundary while accessing SQLite, admits and completes a summary while a simulated deletion waits on the file lease and the job callback is held, and then removes the association. Application tests also cover lease cancellation and release, summary admission and persistence under the mutation boundary.

An additional host request opens the verified fixture PDF, reads its first byte and pauses with the real stream open before PdfPig extraction. While paused, the host registry admits and completes another summary and simulated deletion acquires the released file lease and removes the other association. The real extraction and original summary then complete. This proves progress with an open PDF stream, not a stalled kernel read. Actual library deletion, delayed admission fencing and restart recovery remain later slices.

SQLite observation: the T003 host test reports `wal` in its isolated temporary workspace. The separate T002 snapshot test demonstrated writer progress during a held read snapshot; see [T002 evidence](t002-snapshot.md).
