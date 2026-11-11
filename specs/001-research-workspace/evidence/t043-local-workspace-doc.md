# T043 local workspace documentation

## Scope

New user-facing page `docs/local-workspace.md`. It covers:
- the effective storage location and how it is resolved;
- the contents of the data directory, including `quarantine/`;
- data handling;
- supported platform and browsers;
- configured import limits;
- the controls not yet available (full-library export and deletion, and unavailable-document recovery), tracked in F002 (Codecks $12y).

It states that Constitution Principle II is not yet fully met (Constitution v2.0.0; FR-014).

It also adds one startup log line, `Using data directory {DataDirectory}.`, at Information level, in `src/LexScholar.Server/Program.fs` (`createHost`). The line is written after the lock and migrations and before startup cleanup. The page tells users to look for it. It goes to the local log only and never appears in an API response.

Following the lead's decision (2026-11-10):
- A status line near the top says that importing, the library and reading in the browser are still being built in F001. The page describes storage and data handling, not UI steps. The lead will ask for a recheck, and removal of the status line, once T038 merges.
- plan.md ("Linux local storage location") and research.md ("Security and privacy") say the app should *show* the effective location, but no task did. The user decided on 2026-11-10 to add one Information-level startup log line naming the resolved data directory, in this PR. The T043 text in `tasks.md` was amended to match.

`tasks.md` also records review follow-ups as notes:
- under T028, a non-blocking note with no change;
- under T037, two follow-ups that will be done in the reader-roles PR.

### Test

`tests/LexScholar.Server.Tests/WorkspaceStartupTests.fs`: "startup logs the effective data directory at Information level" (T043). It uses the existing `CapturingLoggerProvider` and builds the host with `Program.create` without starting it. It asserts that the Information entries contain `Using data directory <full path of the workspace>.`. The existing quarantine test still asserts that no *warning* contains the data directory.

## Claim-to-source check

Each statement was checked against `release/0.1.0` at `fa32db6`. If a claim depends on behavior not yet merged, the table names the task instead of code.

| Claim in the page | Source |
|---|---|
| Default `$XDG_DATA_HOME/lexscholar`, else `~/.local/share/lexscholar` (also when XDG is not absolute) | `src/LexScholar.Server/Configuration.fs`, `Configuration.load` |
| Override `LexScholar:DataDirectory` via env (`LexScholar__DataDirectory`) or command line | `Configuration.DataDirectoryKey`; `WebApplication.CreateBuilder(args)` in `Program.main` adds environment and command-line configuration |
| Path must be absolute, not root, not an existing file; startup refused with reason | `Configuration.validatePath`; `Program.describeStartupError`, `main` writes it to stderr and exits 1 |
| Folder created with mode 0700 | `src/LexScholar.Infrastructure/DataDirectoryLock.fs` (`Directory.CreateDirectory` with user rwx); `DocumentStore.ensureDirectories` for subfolders |
| `workspace.sqlite` holds projects, active project, library records, associations and roles | `WorkspaceDb.databasePath`; `WorkspaceRow`, `ResearchProjectRow`, `LibraryDocumentRow`, `ProjectDocumentRow` |
| `originals/`: exact bytes, read-only, generated names, never the client filename | `DocumentStore.promote` (mode 0400, `Guid.NewGuid` key, `File.Move` without overwrite) |
| `staging/` cleared at startup | `StorageCleanup.run`, called from `Program.createHost` before serving |
| `quarantine/`: unreferenced originals moved at startup, warning logged, never deleted | `StorageCleanup.run`; `Program.createHost` `LogWarning` |
| `.lexscholar.lock`; a second instance refuses to start; tabs share one instance | `DataDirectoryLock.acquire` (`flock` `LOCK_EX`/`LOCK_NB`); `LockFailure` message in `describeStartupError`; research.md "Runtime, UI, hosting, and support" |
| Local only, no cloud services | No outbound clients in `src/`; research.md "Security and privacy" |
| Listens only on 127.0.0.1, port 5000, `LexScholar:Port` | `HostSecurity.configure` (`options.Listen(IPAddress.Loopback, port)`, configuration endpoints dropped); `Program.create` |
| Rejects requests from other websites | `HostSecurity.useSecurity` Host/Origin checks and antiforgery |
| Identical bytes stored once, even under a different filename; SHA-256 only narrows candidates, then byte comparison | `src/LexScholar.Application/ImportDocument.fs` (`contentEquals`); `LibraryTests.fs` (T021); over HTTP `LibraryEndpoints.fs` and `LibraryContractTests.fs` (T031, merged in #22). The library UI is T032 |
| Filename for display only | `ImportDocument.sanitizeFileName`; data-model.md `OriginalFileName` |
| Removing a document from a project keeps it in the library | `Library.remove` and `LibraryRepository.removeAssociation`, `LibraryTests.fs` (T030, merged in #21); over HTTP `LibraryContractTests.fs` DELETE (T031, #22) |
| Unavailable on open (exists, readable, recorded size), records kept; same-size changes not detected | `src/LexScholar.Infrastructure/VerifiedDocumentContent.fs` and `ContentAvailabilityTests.fs` (T037, merged in #19); over HTTP `ReaderEndpoints.fs` 503 `document_unavailable` and `ReaderContractTests.fs` (T034/T038, merged in #24); research.md "Document availability on open" |
| Startup log line `Using data directory <path>.` (Information, local log only) | `Program.createHost` `LogInformation`; `WorkspaceStartupTests.fs` (T043) |
| Run from the publish directory or with `--contentRoot`, otherwise the browser app's files are not found | CLAUDE.md ("Commands"); `Program.createHost` serves the client from the content root (`StaticWebAssetsLoader`, `MapStaticAssets`) |
| Linux with Firefox and Chromium; browser's own viewer | research.md "Runtime, UI, hosting, and support" and "Document delivery and native viewer boundary" |
| Limits 100,000,000 bytes and 2,000 pages, keys `LexScholar:ImportLimits:MaxBytes`/`MaxPages` | `Configuration.MaxBytesKey`, `MaxPagesKey`, `Configuration.load` defaults |
| Non-integer, zero or negative limit stops startup with the setting named | `Configuration.limit`; `describeStartupError` (`InvalidLimit`, `NonPositiveLimit`) |
| Lowering a limit does not affect existing PDFs | `src/LexScholar.Domain/Documents.fs` (limits deliberately not checked); `ImportDocument` validates with current limits only on import |
| No export, no in-app deletion, no recovery screen; tracked in F002 ($12y) | plan.md "Constraints" and Principle II row; research.md "Security and privacy" |
| Principle II not fully met | Constitution v2.0.0 Principle II (deferral must not be represented as compliance); plan.md Constitution check |

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114. The branch was rebased on `fa32db6` (after #18 and #19 merged).

## RED

Commit `adfe8f2` (test only).

```sh
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T043'
```

1 total, 1 failed. The failure was `Assert.Contains() Failure: Item not found in collection`. The captured Information entries held only ASP.NET Core's own message (`Using ephemeral data protection provider…`), and the expected `Using data directory /tmp/lexscholar-tests/…` was not among them. This also shows that Information entries reach the capturing provider, so the failure is the missing line and not a log filter.

## GREEN

Commit `ee74774`. T043 filter: 1/1 passed.

Manual check of the published log format and the two override forms (built `LexScholar.Server.dll`, data directory in a scratch folder, `LexScholar:Port=0`, stopped after a few seconds):
- `--LexScholar:DataDirectory=<dir>` printed `info: LexScholar.Server[0]` followed by `Using data directory <dir>.` as the first message.
- `LexScholar__DataDirectory=<dir>` gave the same line.
- The new directory had mode `drwx------`.

## Verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

Restore and build succeeded with 0 warnings and 0 errors. The full suite passed 269/269, including the new T043 test, with 0 skipped, browser journeys included.

## Recheck after T038 (2026-11-11)

Commit `e04546b` ("recheck local workspace doc after T038") updated the claim rows below for T030, T031 and T038; the PR #25 review fix followed.

As planned when T038 merged (#24, `b96caa4`):
- the broad status line was replaced by a narrower one (PR #25 review). It now says the screens cover projects, and that importing, the shared library and reading are implemented in the server while their screens are still being built (T032, T039);
- the page now says to start LexScholar from the publish directory or with `--contentRoot` (PR #20 review follow-up);
- the T030, T031 and T038 claims now cite merged code;
- the recheck note under T043 in `tasks.md` was retargeted: after T032 and T039 merge, remove the status line and recheck the user-facing sentences against the screens.

Docs only: the full suite was re-run to confirm nothing changed (see the PR description).

## Limitations

- The environment-variable and command-line forms were checked once by hand (see GREEN). The test covers only the log entry, not console formatting.
- The library and reader screens (T032, T039) are not built yet. The server implements the storage and data handling this page describes, and the status line says the screens are still in progress. The page must be rechecked against the screens after T032 and T039 merge.
