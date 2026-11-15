# T013 evidence: dismissing while the ask is in flight stops the job

Risky-ish (client concurrency). Covers FR-012. Defect found by speckit-analyze C1: Dismiss in the `Generating` state dropped the client's claim but left the server job running with its marker.

## Change

- `Dismiss` in `Generating r` sends `DeleteSummary` at once and enters `Dismissing (Generating r)`, with `r` recorded in `Draining`. If the DELETE reaches the server before the job exists it is a no-op, so when the POST answers (`Accepted`, any job state) the client sends the DELETE again; a failed POST (`Responded Error`) means no job exists. The pair stays `Dismissing` (she cannot ask) until every DELETE and the POST have answered, in either order; a late answer never shows a summary or starts polling. Leaving the document meanwhile keeps this: the answer is still handled and coming back waits.
- Removed the dead `Progressed` message and its handling (nothing sends it since the stream was removed in T003), and its three tests.

## Verification

- No RED commit: the fix and tests were written together, so a sensitivity check replaces it (below).
- `scripts/check.sh T004 T006 T008 T013`: `Tests: 151 total, 0 failed, 0 skipped` (state tests; journey in Chromium and Firefox: the POST is held in the browser, Dismiss is clicked, the DELETE answers, the POST is released, the fake's held call starts and is cancelled, one request in total, no marker, she can ask again).
- One existing T006 assertion changed: dismissing an ask in flight now emits the delete.

## Sensitivity check (uncommitted, reverted)

Removing the `Generating` case of `Dismiss`: `scripts/check.sh T013` gave `Tests: 34 total, 7 failed`: five new state tests and the journey in both browsers (the held call was never cancelled, timeout). Restored.

## Limits

The journey forces the ordering DELETE-before-POST (the harder case). POST-before-DELETE is covered in the state tests only. CI runs the full suite on the PR.
