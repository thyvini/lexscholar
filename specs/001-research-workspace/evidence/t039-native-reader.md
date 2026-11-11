# T039 — native reader and project-specific role controls

## Scope and environment

Implemented on `feat/f001-reader-ui` only in `.claude/worktrees/dev-a`, continuing approved preexisting commits `cb6618f` (T039 RED tests/stub) and `6c34323` (T040 RED tests). Path, branch and clean status were checked before edits. Linux 7.3.3-1-cachyos; .NET SDK 10.0.114. Verification used Debug builds and isolated temporary workspaces through the existing harness.

- `Reader.fs`: pure opening/check/failure/close state and project-scoped role effects. Content responses are request-numbered; project switches, closing and association removal invalidate pending checks. Role errors from a previous project visit are ignored, including A→B→A; successful completion refreshes associations if its project is currently active. One role mutation remains in flight at a time.
- `App.fs`: Open and role-toggle controls on project associations; native PDF iframe with filename title, Close and same-content-URL new-tab link (`noopener`). Successful role mutations refresh only the current project's associations. Reader state follows project selection and accepted association-list responses.
- Availability is checked with `GET` and `Range: bytes=0-0`, not an iframe load event or a full-PDF pre-download. 503 unavailable, 404 missing document/association, invalid role, and generic network failures have distinct safe messages. No re-import, repair, or deletion is suggested or performed.
- Role PATCH uses the existing same-origin, fresh-antiforgery-token request path. The 503 server title now also names a file of the wrong recorded size.
- The client project already registered the reader stub in dependency order; that registration is preserved and documented. `Library.ReloadProject`, already declared for role refresh, now issues its project fetch.

## RED before implementation

```sh
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj --filter 'Task=T039'
```

Against the preexisting reader stub: **13 total, 11 failed, 2 passed, 0 skipped**, exit 2. Meaningful assertion failures included the expected `SendRole (alpha, paper, ReferenceSource)` versus `None`, and expected HTTP failure mapping versus `RequestFailed`. This was not a build/setup failure. Raw session output: `/tmp/opencode/t039-red.log` (not a repository artifact).

Added a regression for a delayed role failure after A→B→A, then reran the same command before its fix: **15 total, 1 failed, 14 passed, 0 skipped**. Assertion: expected `None` error, actual `Some AssociationNotFound` (`/tmp/opencode/t039-aba-red.log`). Added pending-removal and HTTP-adapter checks tagged T039 as well.

## GREEN and verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln --no-restore -warnaserror
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj --no-build --filter 'Task=T039'
dotnet test --solution LexScholar.sln --no-build
git diff --check
```

- Restore succeeded; final build **0 warnings, 0 errors**.
- T039: **19 passed, 0 failed, 0 skipped** (15 state checks and 4 HTTP-adapter cases). Checks cover content gating/stale results, closing/removal/switching, role scoping, failure mapping, a one-byte range request, network failure, and token-bearing project-specific PATCH.
- Full solution: **381 passed, 0 failed, 0 skipped**, including the existing server 503-title assertions and existing Chromium/Firefox journeys. Full run duration: 42 seconds.
- `git diff --check`: clean. Final logs are `/tmp/opencode/t039-build.log`, `t039-green.log`, and `t039-suite.log` (session-local).

## Boundaries and outstanding work

- **T040 remains unchecked and unchanged.** Its preexisting automated tests happened to pass in the full regression run; this does not complete T040. No T040 tests, manual-navigation evidence, or status were added/extended. Native viewer page navigation/rendering and researcher usability are not established by state tests, iframe attributes, or successful byte delivery; manual supported-browser navigation evidence belongs to T040, usability to T046.
- The availability preflight and the native viewer's subsequent request are separate requests. If the original becomes unavailable after preflight, the browser viewer can show its own failure rather than the app's preflight message. The preflight is not a guarantee of continued availability or a PDF-rendering assertion. Each server content request still performs its own availability check; no custom viewer, polling, or recovery workflow was introduced.
- Full-library export/deletion and unavailable-document recovery remain deferred; no claim of full Constitution Principle II compliance is made.

## P2 review follow-up — delayed successful PATCH after A→B→A

The reviewer identified that ignoring every stale role result also ignored a successful PATCH for the currently active project after returning to it. An association GET on the return visit could finish before the PATCH committed and leave an old role displayed indefinitely.

Added a T039-tagged assertion regression before changing production code: start a role change in A, switch A→B→A, accept the return visit's associations, open another document, then deliver the successful PATCH. It requires `Some RefreshProject` while preserving the new visit's document, project, content-check generation and error state, and clearing the completed mutation.

RED command:

```sh
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj --filter 'Task=T039'
```

**20 total, 1 failed, 19 passed, 0 skipped**, exit 2: expected `Some RefreshProject`, actual `None` (`/tmp/opencode/t039-aba-success-red.log`). This was an assertion failure, not a build/setup failure.

Small fix: when the mutation's project is currently active, success emits the existing refresh effect even after a project switch. Stale errors remain ignored; a result for a different active project still emits no refresh. No response data is applied directly to the current association list or reader. App's existing refresh wiring starts a newly numbered `Library.ReloadProject` GET; earlier association responses cannot overwrite that request's result.

Verification (same environment and Debug configuration as above):

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln --no-restore -warnaserror
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj --no-build --filter 'Task=T039'
dotnet test --solution LexScholar.sln --no-build
git diff --check
```

Restore succeeded; build **0 warnings, 0 errors**. T039 **20/20 passed**, full solution **382/382 passed**, both with **0 skipped**. Existing stale-error and different-project completion checks also pass. Logs: `/tmp/opencode/t039-aba-success-{build,green,suite}.log`. Diff whitespace check clean. No T040 expansion/status change, manual-navigation claim, or P3 documentation work was introduced; the limitations above remain.
