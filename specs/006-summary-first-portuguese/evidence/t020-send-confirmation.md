# T020 — Send confirmation, screen 04 ($155)

## PR #129 accessibility review correction

Reviewer P2 **must fix**: associate the consent disclosure and information note with the alertdialog using `aria-describedby`, so initial Cancel focus does not omit the consent details from its accessible description. Stable IDs now reference both paragraphs for plan confirmation and only the applicable disclosure for the retained service branch. No text, focus or consent behavior changed.

- RED: `scripts/check.sh T020` — 2 browser failures, expected `aria-describedby` association was absent (`/tmp/lexscholar-check.HFmOnH`).
- GREEN: `scripts/check.sh T020` — warning-as-error build passed; **20 passed, 0 failed, 0 skipped**.
- Regression in Chromium and Firefox checks the referenced document/account disclosure and note, and the complete computed accessible description; existing keyboard cancellation/send assertions still pass. Real screen-reader output remains manual validation.

Based on `origin/release/0.4.0` at `3bef757` (including #128). Scope: send confirmation only; no other summary layout or F007 changes.

## RED

`scripts/check.sh T020` — new Chromium and Firefox assertions failed meaningfully: confirmation position expected `fixed`, actual `static` (2 failures; log `/tmp/lexscholar-check.bVlTjB`). Build passed with warnings as errors.

## GREEN

`scripts/check.sh T020` — **20 passed, 0 failed, 0 skipped**, with warning-as-error build. Includes both new browser cases, existing navigation-away consent regressions tagged T020, and existing task-ID matches elsewhere in the solution. Full suite is left to CI; no PR/CI run requested yet.

Both supported browsers verify:

- Native modal centered in a 1440×900 viewport, 520px wide, `aria-modal`, Inter and dimmed backdrop.
- Tinted info note and petrol/white primary Send; Cancel precedes Send.
- Initial Cancel focus; Tab/Shift+Tab stay in the dialog, including boundary wrapping; Escape and keyboard Cancel restore request focus and send nothing.
- Clicking outside does not send or dismiss; at 480px viewport the dialog stays inside 16px side margins.
- Keyboard Send produces exactly one Responses request and shows the summary.
- App-driven document change still discards the old confirmation and sends nothing. The regression uses a programmatic source activation because real background input is intentionally inert while the modal is open.

The first keyboard run exposed native Tab visiting browser chrome at the boundary; the local helper now wraps these boundaries explicitly. Final tests passed in both browsers.

## Visual evidence

Screenshots captured during the green browser run with a temporary capture statement (removed afterward; assertions unchanged):

- [Chromium](t020-chromium-confirmation.png)
- [Firefox](t020-firefox-confirmation.png)

Compared with mockup 04: centered rounded white surface, dimmed workspace, Inter, tinted note, right-aligned Cancel and primary Send. Existing account-identifying body text is retained, so it wraps to one more line than the mockup. The welcome card visible behind the scrim belongs to the existing fake first-sign-in fixture.

## Isolation and limitations

Tests use `TestHarness` temporary workspaces, repository fixture `article-en.pdf` and `FakeOpenAi`; no user workspace, real account or live model accessed. No server/API/state-machine changes, consent-policy changes or new dependencies. Delete confirmation is unchanged. Screen-reader use and real-plan smoke validation remain owner/manual work; this verifies browser semantics and keyboard operation, not assistive-technology output.
