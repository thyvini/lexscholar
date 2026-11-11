# T040 — reader verification (incomplete)

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

## Native viewer navigation — not performed

No interactive browser/computer-use tool was available in this session. No viewer navigation, page boundary or manual result was observed. Consequently T040 remains unchecked; passing the automated journeys is not evidence that either native viewer meets the navigation acceptance behavior. This is not T046 participant usability evidence.

### Reproducible procedure for a future observer

1. On a Linux desktop with Firefox and Chromium, record browser names and exact versions. Use the default native PDF viewer in each browser, not an external PDF application. Start LexScholar using an isolated temporary data directory; for example, from the repository root, run `LexScholar__DataDirectory="$(mktemp -d)" dotnet run --project src/LexScholar.Server` and open the reported loopback URL. Do not use a research workspace for this check.
2. Create and select a project, import `tests/Fixtures/valid.pdf`, associate it with the project, and select **Open**. The fixture has two pages and SHA-256 `0fcd00dc1ad92711575dfde326cda6c56b8fd83b0e01462cab6bfd012dcbe93b`.
3. In the embedded native viewer, visually confirm page 1 renders. Navigate using the viewer controls to page 2, and confirm page 2 renders and the page indicator reflects it. Attempt navigation before page 1 and past page 2 using the viewer's available controls; record the actual resulting page/indicator and whether navigation remains within 1–2. Record any inability to interact with or see the viewer rather than assuming success.
4. Repeat from a fresh isolated workspace for the other browser. Record exact versions, date, display mode, actions, observed rendering and boundary behavior, and pass/fail for each. Preserve evidence separately from API and automated-test results. Do not mark T040 complete unless both supported browsers meet the acceptance behavior.

## Separate decision: T029 failure wording

T029 says failed imports add no stored originals, while historical [T021–T029 evidence](t021-t029-import-staging-and-cleanup.md) records owner approval to retain a promoted orphan until startup quarantine. Insert acknowledgement failure may also occur after a row has committed, so indiscriminate deletion could remove referenced bytes. Reconciliation of the wording and intended semantics requires project-owner approval. This verification does not change the approved import behavior or resolve that decision.
