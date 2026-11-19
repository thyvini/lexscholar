# T014 — Focused onboarding

Fixes [$14z](https://thyago-cunha-dev.codecks.io/card/14z) and [$151](https://thyago-cunha-dev.codecks.io/card/151), following the owner-approved onboarding slice. Branch: `fix/f006-onboarding`, based on `release/0.4.0`.

## RED

`scripts/check.sh T014`

Result: **4 failed, 0 skipped**. Both new journeys failed in Chromium and Firefox: expected zero `aside` menus on the welcome screen, found two. Full RED log: `/tmp/lexscholar-check.rc9Nj5`. An earlier cold-worktree build attempt exceeded the command timeout before producing test results; it is not the RED evidence.

## GREEN

`LOG=/tmp/opencode/f006-onboarding-green.log scripts/check.sh T014 T009 T002 T001 T007 T008`

Result: **350 passed, 0 failed, 0 skipped**; Release build with `-warnaserror`: **0 warnings, 0 errors**. Includes six T014 cases (three journeys in both browsers) plus the existing project, shared-pieces, summary, ChatGPT, pre-send and background-summary regressions affected by their entry helpers.

Checks: focused welcome without either sidebar or the document-empty prompt; first project creation keeps onboarding; import and same-tab ChatGPT sign-in work; setup survives reload and the sign-in callback; explicit skip with no project and finish with optional steps omitted; completion survives reload; an existing workspace in a fresh browser context bypasses onboarding.

Existing workspace journeys now explicitly skip optional setup through `Ui.enterWorkspace`, rather than relying on project creation to exit setup. Their workspace behavior assertions remain. The T009 first-run journey now checks the inline PDF chooser and retained steps instead of expecting navigation to the library.

## Scope and limitations

- Linux, pinned .NET SDK and Playwright Chromium/Firefox; isolated temporary workspaces and fake OpenAI only.
- Session storage holds only `active`/`finished`; no credentials, source contents or project names. Server endpoints, security, authentication, project selection and import association behavior are unchanged.
- If browser storage is unavailable, setup remains in memory but cannot survive navigation with an already-created project. New sessions with projects skip setup; new sessions without projects offer it again (R10).
- The centered welcome, numbered cards, descriptions, typography and controls follow mockup 05. The explicit completion action is the approved addition. The broader manual visual audit and real-plan owner smoke check remain T012/T013.
- CI full-suite run: **pending PR**. No PR opened or merge performed by this slice.
