# T019 project browser journeys

## Scope

`tests/LexScholar.Browser.Tests/ProjectJourneyTests.fs` runs three journeys in headless Linux Chromium and Firefox (6 tests, traits `Task=T019` and `Category=Browser`):

1. **Empty state**: a fresh workspace shows "No projects yet. Create one to get started.", no project rows and no active marker.
2. **Create, select, active display**: create Alpha and Beta. Both are listed, the empty state disappears, and neither is active (creation does not activate). Selecting Beta marks it Active while Alpha offers Select. Selecting Alpha moves the marker.
3. **Restart restore**: create Alpha and Beta and select Beta. Stop the host, start a new host over the same data directory, and open a new browser context. Both projects are listed and Beta is Active.

Associated-document display after switching is out of scope here; T033 covers it.

## Environment

- OS: CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114.
- Microsoft.Playwright 1.64.1 (published 2026-10-22, pinned in `Directory.Packages.props`).
- Browsers: Playwright's own builds, not the distribution packages. Chromium headless runs on Chrome Headless Shell 156.0.8078.4 (playwright chromium v1248); Firefox is 157.0 (playwright firefox v1555). Playwright reported "OS not officially supported" and installed its Ubuntu 24.04 fallback builds, which ran without missing-library errors. The stock distribution browsers (Chromium 156.0.8078.79, Firefox 159.0.1) were not driven; manual evaluation in T046 covers them.
- Install command (one time, cached in `~/.cache/ms-playwright`):

```sh
dotnet build tests/LexScholar.Browser.Tests -c Release
B=tests/LexScholar.Browser.Tests/bin/Release/net10.0/.playwright
$B/node/linux-x64/node $B/package/cli.js install chromium firefox
```

## Host

The journeys reuse `TestHarness` (linked from `tests/LexScholar.Server.Tests`) with `serveClient = true`. `TestHarness.Start` now takes an optional caller-owned `TemporaryWorkspace`. When one is supplied, the harness reuses its root and leaves it in place on dispose, so a second host can start over the same SQLite database after the first host has stopped and released the data-directory lock. Existing callers keep their private, self-deleting workspaces. The restarted host gets a new ephemeral port, so the browser opens a new origin and the active project is restored from server-side state alone.

## RED → GREEN

RED: the journeys and harness link were written with `TestHarness.Start` accepting `workspace` but ignoring it, so each start still created a fresh workspace.

```sh
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj --configuration Release --filter-trait 'Task=T019' --output Normal
```

6 total, 2 failed, 4 succeeded. Both restart journeys (Chromium and Firefox) failed on `Locator expected to have count '2'` for `ul.projects li`: the restarted host showed no projects. Journeys 1–2 passed, as expected for characterizing UI that T018 already shipped.

Sensitivity check (temporary, not committed): with the Active marker in `src/LexScholar.Client/App.fs` replaced by `empty ()`, the same filter gave 4 failed (journeys 2 and 3 in both browsers) and 2 succeeded (empty state). `App.fs` was then restored from git.

GREEN: the harness uses the supplied workspace and deletes only workspaces it created. The same filter passed 6/6.

## Final verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

Restore and build succeeded with zero warnings. The full suite passed 177/177 (171 previously + 6 T019), zero skipped. No `lexscholar-tests` temporary directories remained after the run.

Browser tests are part of the default suite and fail, rather than skip, when Playwright browsers are missing. Exclude them with `--filter-not-trait 'Category=Browser'`. These automated checks are not participant usability evidence (SC-001 and T046).
