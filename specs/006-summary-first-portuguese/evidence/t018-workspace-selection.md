# T018 — Workspace selection presentation

- Reference: mockup `06-summary-first.png` has a tinted, unoutlined project, an outlined selected source, and both structural column dividers. Owner's “no right border” is interpreted as no selected-project outline/right edge, not removal of the frame divider. Drawer changes ($156) are excluded.
- RED: `LOG=/tmp/opencode/t018-red.log scripts/check.sh T018` — 12 tests, 4 failures, 0 skipped. Both browsers report the project's unwanted solid outline and the missing current-document accessibility attribute.
- GREEN: `LOG=/tmp/opencode/t018-green.log scripts/check.sh T018` — build with `-warnaserror`; 12 tests passed, 0 skipped, including four new Chromium/Firefox cases.
- Sensitivity: temporarily removed `not showLibrary` from the source's selected-class condition. `LOG=/tmp/opencode/t018-sensitivity.log scripts/check.sh T018` — 12 tests, 2 failures, 0 skipped: both browsers detect the stale selection while the library is shown. Mutation restored, uncommitted.
- Final verification after rebasing onto `origin/release/0.4.0` (`f8738cc`, including T017): `LOG=/tmp/opencode/t018-final.log scripts/check.sh T018 T015 T006` — build with `-warnaserror`; 80 tests passed, 0 failed, 0 skipped.
- Coverage: no selected item in an empty workspace; project tint without outline/right border; frame dividers retained; keyboard focus ring and Enter activation; project documents follow project changes; project changes clear the opened document; library navigation clears source selection; opening a source restores its selected style and main-area summary request. The source's existing open button now reports `aria-current` without replacing its label or keyboard behavior.
- CI full suite: pending lead's PR; no PR opened or merged by this slice.
