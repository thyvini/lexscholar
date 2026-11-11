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
| Identical bytes stored once, even under a different filename; SHA-256 only narrows candidates, then byte comparison | `src/LexScholar.Application/ImportDocument.fs` (`contentEquals`); `LibraryTests.fs` (T021). Through the UI: depends on T031/T032 |
| Filename for display only | `ImportDocument.sanitizeFileName`; data-model.md `OriginalFileName` |
| Removing a document from a project keeps it in the library | Depends on **T030** (not merged); data-model.md "Association removal" |
| Unavailable on open (exists, readable, recorded size), records kept; same-size changes not detected | `src/LexScholar.Infrastructure/VerifiedDocumentContent.fs` and `ContentAvailabilityTests.fs` (T037, merged in #19); over HTTP depends on **T038** (not merged); research.md "Document availability on open" |
| Startup log line `Using data directory <path>.` (Information, local log only) | `Program.createHost` `LogInformation`; `WorkspaceStartupTests.fs` (T043) |
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

## Limitations

- The environment-variable and command-line forms were checked once by hand (see GREEN). The test covers only the log entry, not console formatting.
- Claims marked as depending on T030 or T038 describe the specified behavior, which is not yet observable through the app. The page must be rechecked when T038 merges.
