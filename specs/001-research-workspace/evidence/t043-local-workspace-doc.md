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

Following the lead's decision (2026-11-10):
- A status line near the top says that importing, the library and reading in the browser are still being built in F001. The page describes storage and data handling, not UI steps. The lead will ask for a recheck, and removal of the status line, once T038 merges.
- plan.md ("Linux local storage location") and research.md ("Security and privacy") say the app should *show* the effective location. No task does that, and the app currently neither displays nor logs it. This gap has been raised with the user. The page documents how the location is resolved, and no code was added.

There is no RED/GREEN: nothing executable changed (one Markdown page, this evidence file and the `tasks.md` tick).

## Claim-to-source check

Each statement was checked against `release/0.1.0` at `752ba8a`. If a claim depends on behavior not yet merged, the table names the task instead of code.

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
| Unavailable on open (exists, readable, recorded size), records kept; same-size changes not detected | Depends on **T037** (PR #19, not merged) and **T038** (HTTP 503, not merged); research.md "Document availability on open" |
| Linux with Firefox and Chromium; browser's own viewer | research.md "Runtime, UI, hosting, and support" and "Document delivery and native viewer boundary" |
| Limits 100,000,000 bytes and 2,000 pages, keys `LexScholar:ImportLimits:MaxBytes`/`MaxPages` | `Configuration.MaxBytesKey`, `MaxPagesKey`, `Configuration.load` defaults |
| Non-integer, zero or negative limit stops startup with the setting named | `Configuration.limit`; `describeStartupError` (`InvalidLimit`, `NonPositiveLimit`) |
| Lowering a limit does not affect existing PDFs | `src/LexScholar.Domain/Documents.fs` (limits deliberately not checked); `ImportDocument` validates with current limits only on import |
| No export, no in-app deletion, no recovery screen; tracked in F002 ($12y) | plan.md "Constraints" and Principle II row; research.md "Security and privacy" |
| Principle II not fully met | Constitution v2.0.0 Principle II (deferral must not be represented as compliance); plan.md Constitution check |

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, base commit `752ba8a`.

## Verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

Restore and build succeeded with 0 warnings and 0 errors. The full suite passed 254/254 with 0 skipped, browser journeys included. This matches the base commit, since nothing executable changed.

## Limitations

- The command-line and environment-variable examples were checked against the configuration code and ASP.NET Core's default configuration sources, not by starting the published app with each form.
- Claims marked as depending on T030, T037 or T038 describe the specified behavior, which is not yet observable through the app. The page must be rechecked when T038 merges.
