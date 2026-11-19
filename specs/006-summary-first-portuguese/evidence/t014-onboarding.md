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

## PR #123 P1 correction — interruption is not completion

Verdict: **must fix**. The previous source-marker fix prematurely marked setup finished on departure. A handoff now retains active setup at the source and destination, recording only a session-scoped handoff marker. An accepted explicit skip/finish stores finished locally, then navigates to the other loopback origin with `?onboarding=finished` to synchronize its session marker. The receiver strips that UI-only marker and clears handoff state. This deliberately moves the tab once on explicit completion; no server/auth changes, new endpoint or cross-origin frame is needed.

**RED**: `LOG=/tmp/opencode/f006-review3-red.log scripts/check.sh T014` — **16 total, 2 failed, 0 skipped**. New regressions in Chromium and Firefox interrupt the numeric-origin navigation before its app loads, go Back to localhost and reload. Expected welcome is missing: the user enters the workspace despite never choosing skip/finish.

**GREEN**: `LOG=/tmp/opencode/f006-review3-green.log scripts/check.sh T014 T009 T002 T001 T007 T008` — **360 passed, 0 failed, 0 skipped**; Release `-warnaserror`: **0 warnings, 0 errors**. All sixteen T014 browser cases pass, including the interrupted handoff, revisit after explicit completion, already-signed-in consumption, finish while destinations load, and account retry regressions. PR CI and required GPT review will run on the pushed correction; not merged.

## PR #123 P1 correction — remove cross-origin completion

Verdict: **must fix**. Clearing handoff state before the completion-navigation receiver ran left the other origin unfinished if navigation was interrupted. Rather than add retry/acknowledgment state, onboarding now uses only the callback origin: an empty localhost workspace moves to numeric loopback before welcome/actions or persisting setup. Existing localhost workspaces stay on localhost. Completion is a single local session write, with no navigation to interrupt. Removed the handoff marker/query protocol and pending automatic sign-in intent entirely.

**RED**: `LOG=/tmp/opencode/f006-review4-red.log scripts/check.sh T014` — **18 total, 2 failed, 0 skipped**. Both browsers create a project from the localhost entry, sign in, prevent the other origin's app from receiving completion, return to the completing origin and then revisit localhost. Welcome reappears despite explicit completion.

**Initial GREEN**: `LOG=/tmp/opencode/f006-review4-green.log scripts/check.sh T014 T009 T002 T001 T007 T008` — **362 passed, 0 failed, 0 skipped**; Release `-warnaserror`: **0 warnings, 0 errors**. Eighteen T014 browser cases pass.

Prior scenario assertions remain: source revisits after completion; interrupted navigation plus Back/reload; sign-out after reloading already-signed-in setup without authorization; finish while account loading is pending; account failure/retry; optional import and real callback URL; skip/finish/reload. Navigation interruption is now tested before initial welcome because the localhost-to-numeric transition must occur before project creation. Tests no longer manufacture the removed handoff query protocol. Added assertions that localhost first run reaches numeric loopback before project creation and an existing localhost workspace stays on localhost. The interrupted-completion regression remains unchanged and green because completion no longer initiates any synchronization navigation. CI/GPT review will rerun on the pushed head; not merged.

**History RED**: `LOG=/tmp/opencode/f006-review4-history-red.log scripts/check.sh T014` — **20 total, 2 failed, 0 skipped**. New browser history regressions showed that a non-replacing initial redirect allowed Back to return to the source workspace after project creation. Changed the initial redirect to replace its history entry. Interrupted-navigation tests use an explicit previous-page sentinel and Back/Forward, avoiding Chromium/Firefox differences in initial blank-page history.

**Final GREEN**: `LOG=/tmp/opencode/f006-review4-final-green2.log scripts/check.sh T014 T009 T002 T001 T007 T008` — **364 passed, 0 failed, 0 skipped**; Release `-warnaserror`: **0 warnings, 0 errors**. All twenty T014 browser cases pass, including Back/Forward/reload with a project created but setup unfinished.

## PR #123 P1 correction — canonicalize every localhost entry

Verdict: **must fix**. Restricting canonicalization to empty-workspace detection still allowed a same-tab localhost revisit to bypass active numeric-loopback setup once a project existed. Removed that exception. Every localhost entry now replaces its history entry with the same-port numeric-loopback address at initialization, before loading projects or reading setup state. The query is preserved. There is exactly one workspace/setup origin; no origin synchronization, marker transfer or automatic sign-in intent is needed. Existing workspace data and actions are unchanged; localhost addresses normalize too.

**RED**: `LOG=/tmp/opencode/f006-review5-red.log scripts/check.sh T014` — **22 total, 2 failed, 0 skipped**. Both new browser cases create a project without finishing setup, explicitly revisit localhost in the same tab, and fail to find welcome because the workspace opened instead.

**GREEN**: `LOG=/tmp/opencode/f006-review5-green.log scripts/check.sh T014 T009 T002 T001 T007 T008` — **366 passed, 0 failed, 0 skipped**; Release `-warnaserror`: **0 warnings, 0 errors**. Twenty-two T014 cases pass. The new cases revisit localhost while active, go Back/Forward while unfinished, explicitly finish, revisit localhost again and go Back twice through earlier entries; completed setup never reappears. Existing-workspace coverage verifies preserved project data at the normalized address. Earlier interruption, sign-in, account-retry and completion regressions remain green. CI/GPT review will rerun on the pushed head; not merged.
