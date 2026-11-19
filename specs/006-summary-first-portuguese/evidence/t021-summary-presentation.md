# T021 — Separate summary/request cards ($155)

Owner-approved visual slice against mockups 06/08, based on release commit `3bef757` (T019, PR #128). The filename, document details and secondary PDF link now sit above the separate white request or summary card. `Resumir` is accent-filled and right-aligned. The generated notice is tinted, and points are unbulleted with space between them.

T017's Inter override is retained. Existing account controls, summary state transitions, generated context, PDF/citation URLs and new-tab behavior are unchanged. No F007 instruction, feedback, regeneration or recent-instruction controls were added.

## Verification

- RED: `scripts/check.sh T021` — warning-as-error build passed; 33 tests, 2 failed, 0 skipped. Both browsers found one enclosing `.card` around the document heading instead of zero (`/tmp/lexscholar-check.f7Iell`). Earlier test setup attempts were corrected to reopen the document after the OAuth return; they are not the behavioral RED.
- GREEN: `scripts/check.sh T021 T017 T003 T007 T008` — warning-as-error build passed; 287 tests passed, 0 failed, 0 skipped. Includes Chromium and Firefox card separation, right-aligned primary action, notice tint, point spacing, Inter typography, PDF/citation new-tab navigation, plan-only requests and deletion regressions.
- An initial combined regression invocation exceeded the tool's 120-second foreground limit without reporting a failed test; restarted as a background invocation to allow completion.
- Full suite/CI: not run locally; no PR requested. Required on the integrating PR.

## Integration

No confirmation-dialog code or CSS was edited. T020 / PR #129 changes `App.fs` and `app.css` in parallel: resolve any shared-file conflict by retaining its dialog changes and T021's request/summary/heading changes. This slice does not include or replace T020. Owner visual acceptance and native PDF page rendering remain the release manual checks.

### Integration after PR #129

PR #130 was conflicting after release advanced to `3117253`. Saved `backup/t021-before-t020-integration`, then rebased onto that release commit. Resolved `App.fs`, `app.css` and `tasks.md`, retaining both slices, including T020's `summary-request` focus-return ID and all modal styles. Its JavaScript, host script registration and modal journeys remain unchanged.

`LOG=/tmp/opencode/t021-t020-integration-green.log scripts/check.sh T021 T020 T017 T003 T007 T008` — warning-as-error build passed; 305 tests passed, 0 failed, 0 skipped. Local `git merge-tree --write-tree HEAD origin/release/0.4.0` is clean. Push uses an explicit lease against the previously verified PR head `77dc8c18e6739b888bd08724166af0914c9af727`; CI must run again on the new head.
