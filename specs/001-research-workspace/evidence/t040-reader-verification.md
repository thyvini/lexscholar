# T040 — reader verification (complete with owner-reported manual acceptance)

## Automated results, 2026-11-11

Tested release base `4bf2f27` on Linux 7.3.3-1-cachyos, x64, UID 1000, .NET SDK 10.0.114. Playwright's installed browser builds reported Chromium 156.0.8078.4 (Chrome for Testing, revision 1248) and Firefox 157.0 (revision 1555). The journeys launch them headlessly; version output is from their corresponding cached browser executables.

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj -c Release --no-build --filter-trait 'Task=T040'
dotnet test --solution LexScholar.sln -c Release --no-build
```

Restore passed; build passed with **0 warnings and 0 errors**. The existing T040 journeys passed **12/12, 0 failed, 0 skipped** (six cases per browser). The full solution passed **386/386, 0 failed, 0 skipped**.

The journeys cover project-specific role changes and project isolation, reader frame open/close and closing on project switch, and clear missing, wrong-length and unreadable-file feedback with records preserved. The open journey checks frame attributes and independently requests exact original bytes. It does **not** establish native PDF rendering or page navigation. The unreadable test verifies that mode 000 actually prevents a read; it ran as an unprivileged user.

## Native viewer navigation — owner-reported manual PASS, 2026-11-11

The automated testing agent did **not** have interactive browser/computer-use capability; its 12 browser journeys check page integration and PDF delivery, not native rendering or navigation. The project owner separately performed manual desktop checks and confirmed the required reader interaction succeeded in **both** supported browsers:

| Desktop browser | Version reported by owner | Page 1 → page 2 | Out-of-range navigation | Result |
|---|---|---|---|---|
| Mozilla Firefox | 159.0.1 | Successful | Could not navigate before page 1 or beyond page 2 | PASS (owner-reported) |
| Chromium (Arch Linux) | 156.0.8078.79 | Successful | Could not navigate before page 1 or beyond page 2 | PASS (owner-reported) |

The owner first confirmed both viewers worked, then explicitly confirmed successful navigation from page 1 to page 2 **and** that navigation stays within the first/last page bounds. This record reflects the owner's testimony, not observations made by an AI agent. A screenshot, exact page-indicator text, display environment, and step-by-step per-browser logs were not supplied; they are not claimed as evidence. The automated test-browser versions above differ from the installed desktop browsers.

T040 combines these manually reported acceptance observations with the separately recorded 12/12 automated journeys. **T040 is complete on that basis.** The observation was performed by the project owner, not by unassisted researcher participants, and **does not satisfy T046**.

### Reproduction procedure

1. On a Linux desktop, open LexScholar in Firefox and Chromium using each browser's native PDF viewer, not an external PDF application. Start LexScholar with an isolated workspace.
2. Create a project, import `tests/Fixtures/valid.pdf` (two pages, SHA-256 `0fcd00dc1ad92711575dfde326cda6c56b8fd83b0e01462cab6bfd012dcbe93b`), associate it, and click **Open**.
3. Confirm PDF pages render, navigate from page 1 to page 2, and attempt navigating before page 1 and after page 2. The viewer must keep navigation within pages 1–2.
4. Record the actual desktop browser versions and observed results separately from the Playwright/API results.

## Subsequent disposition: T029 failure wording

At the time of the original T040 investigation, T029's literal wording conflicted with the already approved exception for post-promotion insert errors; see [historical decision](t021-t029-import-staging-and-cleanup.md) and [post-promotion regression evidence](fix-import-insert-failure-investigation.md). The project owner approved reconciliation on 2026-11-11 and PR #33 updated `tasks.md` and `plan.md` to match the safe recovery behavior. No runtime import behavior was changed by that documentation resolution.
