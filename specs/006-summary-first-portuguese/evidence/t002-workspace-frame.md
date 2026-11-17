# T002 — Three-column workspace

- RED: `scripts/check.sh T002` — 58 tests, 2 failed, zero skipped; both browsers failed the new journey at `GetByTestId("main")`, which did not exist before the frame.
- GREEN: `scripts/check.sh T002 T019 T033 T040 T004 T011 T001` — build with `-warnaserror`; 198 tests passed, zero failures or skips, including Chromium and Firefox.
- CI full suite: [run 43997374945](https://github.com/thyvini/lexscholar/actions/runs/43997374945); final-head gate tracked on [PR #113](https://github.com/thyvini/lexscholar/pull/113/checks).
- Review round 1: approved, complete coverage; one P2 about the previous document staying selected/in the breadcrumb while Biblioteca is shown. Verdict: **must fix** (verified current-location ambiguity). Additional RED: `scripts/check.sh T002` — 72 tests, 2 failed, zero skips; both browsers expected `Alpha / Biblioteca` but saw `Alpha / valid.pdf`. Fixed by displaying Biblioteca in the breadcrumb and suppressing the document highlight while that view is active, without clearing reader/summary state. Final-head review is recorded on the PR checks.

The new journeys check the three grid columns, named source-role lists, active-project and library counts, breadcrumb, library/import navigation, the existing reader in the main column, and the signed-in top-bar account menu/sign-out. Project/library journeys retain creation, selection, restart, import, rejection, association and removal coverage. Other affected journeys change only navigation/translated selectors; their behavioral assertions remain.

Owner decision: counts for inactive projects are deferred. Counts use existing loaded state, with no new API requests. The reader, summary service choices and remaining English interface texts intentionally remain for their later slices; no F007 controls are introduced.
