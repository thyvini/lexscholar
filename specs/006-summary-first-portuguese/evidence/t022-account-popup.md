# T022 — Floating account popup

- **RED**: `scripts/check.sh T022` — build passed; both new Chromium/Firefox cases failed the assertion “Opening Conta must not expand the top bar” against the original in-flow disclosure. 12 selected tests total, 2 failed, 0 skipped. Log: `/tmp/lexscholar-check.oL5DGc`.
- **GREEN**: `scripts/check.sh T022` — warning-as-error build passed; 12 selected tests passed, 0 failed, 0 skipped, including both browser cases.
- **Coverage**: at 1440px and 1000px, pointer opening leaves header height and main-area position/height unchanged. Panel is below/right of the trigger, surfaced and bordered, with sign-out hit-testable above workspace content. Native Enter closes while retaining trigger focus; Space reopens, Tab reaches sign-out and Enter signs out successfully.
- **Scope**: markup/CSS only; native disclosure semantics, existing account text and guarded sign-out action retained. No custom menu protocol or authentication changes.
- **Full suite / CI**: pending the eventual PR; no PR opened or merge performed by this implementation slice.
