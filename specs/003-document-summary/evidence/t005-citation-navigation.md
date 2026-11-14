# T005 citation navigation

- RED: `scripts/check.sh T005` -> 4 failed of 44 (state tests: `Expected: Some 3, Actual: None`; journey on Chromium and Firefox: frame `src` was `/api/documents/<id>/content`, expected `...#page=2`).
- GREEN: `scripts/check.sh T005` -> 44 total, 0 failed, 0 skipped.
- Repeated click: the journey marks the frame element, clicks the same citation again, and asserts the mark is gone (frame re-created via the navigation key) while `src` still ends `#page=2`.
- Full suite: CI run on the PR.
