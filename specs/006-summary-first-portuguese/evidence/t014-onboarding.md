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

## Review corrections

Both P1 findings against `ca4280d` are **must fix**:

1. `localhost` session storage cannot follow the fixed `127.0.0.1` callback. Onboarding sign-in now hands off the active setup flag to numeric loopback before starting authorization, using a stripped UI-only query marker, then resumes the requested sign-in. Existing workspace visits, OAuth callback/state validation and server endpoints are unchanged. The fake sign-in helper now uses the actual issued `redirect_uri`, rather than substituting the current origin.
2. `ChatGpt.Unavailable` silently omitted the onboarding account action. That case now renders the existing Portuguese failure and retry control; retry reloads destinations and restores sign-in.

**RED**: `LOG=/tmp/opencode/f006-review-red.log scripts/check.sh T014` — **10 total, 4 failed, 0 skipped**. Both browsers lost welcome after starting from localhost, creating a project and following the numeric callback; both also lacked the expected account failure alert on a forced destinations 503. The original six cases passed.

**GREEN**: `LOG=/tmp/opencode/f006-review-green.log scripts/check.sh T014 T009 T002 T001 T007 T008` — **354 passed, 0 failed, 0 skipped**; Release `-warnaserror` build: **0 warnings, 0 errors**. The localhost tests verify signed-in setup on `127.0.0.1`, no sidebars, reload retention and explicit completion. Failure tests restore the destinations endpoint, retry and verify the sign-in button returns with no remaining alert, still within onboarding. Full-suite CI remains pending PR.

## Second review corrections

Both P2 findings against `25a4eb6` are **must fix**:

1. The handoff left the source `localhost` session marker active. Starting the handoff now marks the source finished before navigation, transferring ownership of active setup to numeric loopback. The existing cross-origin journey now finishes there, revisits the original localhost address, and asserts no welcome or missing sidebars.
2. The pending automatic sign-in intent remained after loading an already-signed-in account. Loading a signed-in account now consumes it; finishing setup cancels it even while account loading is pending. New journeys cover already-signed-in handoff → finish → sign-out without authorization, and finish while a held destinations request is pending → release signed-out result without authorization.

**RED**: `LOG=/tmp/opencode/f006-review2-red-confirm.log scripts/check.sh T014` — **12 total, 4 failed, 0 skipped**. Both browsers showed welcome again on the source origin; both lost the expected signed-out action after finishing and signing out because authorization started instead.

**Finish-cancellation sensitivity RED**: with the signed-in consumption/source handoff fixes present but finish cancellation deliberately omitted, `LOG=/tmp/opencode/f006-review2-finish-red.log scripts/check.sh T014` — **14 total, 2 failed, 0 skipped**. Both held-load cases started authorization after completion rather than retaining the enabled sign-in action. Restored finish cancellation before GREEN.

**GREEN**: `LOG=/tmp/opencode/f006-review2-green.log scripts/check.sh T014 T009 T002 T001 T007 T008` — **358 passed, 0 failed, 0 skipped**; Release `-warnaserror` build: **0 warnings, 0 errors**. Fourteen T014 browser cases included. Full-suite CI remains pending PR; no server/auth changes.
