# T017 — Consistent summary typography and PDF control ($154)

Owner direction takes precedence over screen 06's serif summary points: points and citations now use the interface's Inter stack. The PDF anchor follows the mockup's bordered secondary-control treatment with a decorative external-link arrow. Existing URLs, new tabs, `noopener`, citation treatment and source-list actions are unchanged. No $155/$156 layout work.

## Verification

- RED: `LOG=/tmp/opencode/t017-red.log scripts/check.sh T017` — 2 failed, 0 skipped; both browsers reported Source Serif 4 instead of the interface font.
- Control RED after the typography fix: `LOG=/tmp/opencode/t017-control-red.log scripts/check.sh T017` — 2 failed, 0 skipped; the unstyled PDF link lacked the secondary control's flex treatment.
- Initial GREEN attempt exposed CSS flex-item blockification (`inline-flex` computes to `flex`); corrected the computed-style expectation. That invocation timed out before completion.
- GREEN: `LOG=/tmp/opencode/t017-green.log scripts/check.sh T017 T003` — warning-as-error build passed; 81 tests passed, 0 failed, 0 skipped. Includes both browsers' typography, secondary-control border, hover, keyboard focus and decorative icon checks, plus existing PDF/citation new-tab and preserved-summary regressions.
- Full suite/CI: not run locally; no PR requested. CI remains required on the integrating PR.

Design interpretation: consistent typography means Inter for the points and citations, not a change to onboarding. The control remains secondary, not an accent-filled primary action. No unresolved implementation blocker; owner visual acceptance remains part of the manual release check.
