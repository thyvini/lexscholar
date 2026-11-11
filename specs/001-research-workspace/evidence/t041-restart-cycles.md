# T041 restart cycles and lowered limits

## Scope

- **New tests** in `tests/LexScholar.Server.Tests/` (Trait `Task=T041`), each with a `Compile` line in `LexScholar.Server.Tests.fsproj`. No product code changed.
  - **`RestartTests.fs`**, "100 consecutive restarts keep every project, document, association and the active project" (SC-005):
    - **Setup,** over HTTP on one `TemporaryWorkspace`: 2 projects (A, B), `valid.pdf` and `distinct.pdf`, 4 associations, one role changed by PATCH (A/valid to `ReferenceSource`), one association removed (A/distinct), project A selected.
    - **What survives:** A/valid as `ReferenceSource`, B/valid as `FocusedSource` and B/distinct as `ReferenceSource`. Both roles survive, and the same document has a different role in each project. The test asserts these roles once before taking the snapshot, so a role reset in either direction is detected.
    - **100 cycles:** each starts a new `TestHarness` on the same workspace and reads, through the API, the active project, the projects, the library listing, each project's associations, and the SHA-256 of each document's served content. All must equal the setup snapshot, and the active project must equal the one selected in the previous cycle.
    - **Alternating selection:** each cycle then selects the other project, so restoration of the last active project is checked on every restart.
    - **Failures** name the first failing cycle.
  - **`LimitRestartTests.fs`**, "lowered limits keep an imported PDF readable and reject new over-limit imports" (FR-003, FR-011, SC-008):
    1. Import `valid.pdf` (753 bytes, 2 pages) under the default limits.
    2. Restart with `MaxBytes = 700` and `MaxPages = 1`. Content returns 200 with identical bytes, the library listing is unchanged, and `distinct.pdf` (753 bytes) gets 413 `upload_too_large`.
    3. Restart with the default byte limit and `MaxPages = 1`. Re-uploading `valid.pdf` gets 422 `page_limit_exceeded`, because limits are rechecked on every import, including for bytes already stored.
    4. After each rejection (413 and 422), the existing PDF's served bytes equal the fixture again. After each restart, the library rows and the files in `originals/` are unchanged.

    As the lead decided, no new fixture was added.
- **`tasks.md`:** T041's path is reworded from `tests/LexScholar.Infrastructure.Tests/RestartTests.fs` to `tests/LexScholar.Server.Tests/RestartTests.fs`, described as in-process host restarts over one workspace. This follows the user's decision (2026-11-10) that the cycles are `TestHarness` stop/start inside the normal suite; `TestHarness` lives in Server.Tests, which Infrastructure.Tests cannot reference. The lead approved the change; it touches `tasks.md` only.
- **`verification.md`:** the SC-005 and SC-008 automated results are recorded. SC-003, SC-004 and SC-007 stay "not yet recorded".

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, Microsoft.Playwright 1.64.1. Written on `21f1fde` (release/0.1.0 after #23) and rebased onto `f651c5b` (after #25 and #27). Tests ran as uid 1000.

## No committed RED

Like T042, these tests check existing behavior (T016/T017 persistence and selection, T031 import limits, T038 content), and they passed on their first run. A committed RED would need a deliberately false assertion. Instead, these temporary, uncommitted sensitivity checks show the tests can fail. Each file was restored and the solution rebuilt afterwards; `git status` showed only the new tests and the fsproj lines.

| Temporary change | Result |
|---|---|
| `ProjectRepository.select` saves only every other selection | `RestartTests` failed: "Restart cycle 2: the active project changed." |
| The content route returns 503 when a document's `ByteLength` exceeds the current `MaxBytes` (applying import limits on open) | `LimitRestartTests` failed: `Expected: OK` for the existing PDF's content after lowering the limits. |

A first attempt at the selection check didn't compile and ran stale binaries; it was discarded and redone, and only the redone run is recorded above.

## Verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T041' --output Detailed
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

- Restore and build: 0 warnings, 0 errors.
- T041 filter: 2/2 passed. `RestartTests` took 12.3 s and `LimitRestartTests` 2.2 s.
- Full suite: 308/308 passed with 0 skipped, browser journeys included (306 on `21f1fde` plus 2 new), in 44 s.
- The 100 cycles add about 12 s to the full suite.
- After the rebase onto `f651c5b`: 328/328 passed with 0 skipped (326 plus the 2 new tests). Total suite times vary between runs on this machine (19 to 44 s) because test projects run in parallel.

## Review fixes (PR #28)

Two Greptile P2 findings, both valid, and one non-blocking request:
- **Role coverage:** in the original setup every surviving association was `ReferenceSource`, so a reset to that role would have gone unnoticed. The removed association is now A/distinct, and B/valid starts as `FocusedSource`. The surviving roles are asserted once before the snapshot (see Scope).
- **Bytes after rejection:** `LimitRestartTests` now compares the existing PDF's served bytes with the fixture after each rejection (413 and 422), not just rows and file names.
- **ProcessHarness wording:** verification.md now says that real child-process restarts cover lock release and startup only, not persistence.

Sensitivity check (temporary, not committed; `Program.fs` restored and rebuilt): resetting every association's role to `ReferenceSource` at startup made `RestartTests` fail with "Restart cycle 1: the associations or roles changed." With the original setup, that reset would have been invisible, because every surviving role was already `ReferenceSource`.

The branch merged `origin/release/0.1.0` at `730c0f0` (after #26) without a force-push. T041 filter: 2/2 passed (`RestartTests` 9.1 s, `LimitRestartTests` 1.7 s). Restore and build: 0 warnings. Full suite after the merge, including browser journeys: 352/352 passed, 0 skipped.

## Limitations

- **In-process restarts:** the restarts are in-process host stop/start, per the user's decision. A crash, or a kill of a real process, is not part of these 100 cycles; child-process lock and startup behavior is covered by the `ProcessHarness` tests.
- **What each cycle checks:** each cycle checks state through the HTTP API and the served bytes, not the database file directly. The database rows are checked directly only in `LimitRestartTests`.
- **One active-project change per cycle:** projects, documents and associations are created once and then only read, so the cycles test persistence of a fixed dataset plus one selection change per cycle, not mutations across many restarts.
