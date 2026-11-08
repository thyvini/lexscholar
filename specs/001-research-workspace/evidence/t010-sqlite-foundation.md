# T010 SQLite foundation evidence

Environment: Linux CachyOS x86_64, .NET SDK `10.0.114`, EF Core SQLite `10.0.14`. Commands were run from the repository root on 2026-11-08. This is foundation verification, not the later F001 persistence/100-restart acceptance test.

## Test-first RED

A compile-ready `WorkspaceDb` scaffold opened a real SQLite file but had no migrations or mapping conventions; the host had not yet been wired to initialize it. The test-only parent/child tables were created in isolated files and are not production entities. After a clean Release build, the following commands produced assertion failures rather than fixture, compile, or startup setup failures:

```sh
dotnet test --project tests/LexScholar.Infrastructure.Tests/LexScholar.Infrastructure.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T010' --output Detailed
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T010' --output Detailed
```

Infrastructure: **9 total, 7 failed, 2 passed**. The migration history was empty; UUIDs were stored in uppercase (`FEDC...`); UTC `DateTime` was stored without `T`/`Z`; unspecified timestamps were accepted; and corrupt-file initialization did not fail. Native SQLite version and the bundled library's default foreign-key behavior passed. Server: **3 total, 3 failed** because no database was initialized before listening and a deliberately unopenable database did not prevent startup.

An additional test for `DateTimeOffset` was run before its converter was implemented:

```sh
dotnet test --project tests/LexScholar.Infrastructure.Tests/LexScholar.Infrastructure.Tests.fsproj --configuration Release --filter-display-name '*offset timestamps normalize*' --output Detailed
```

Result: **1 failed**; expected `2026-05-17T07:04:56.1234567Z`, actual `2026-05-17 12:34:56.1234567+05:30`.

## GREEN

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release
dotnet test --project tests/LexScholar.Infrastructure.Tests/LexScholar.Infrastructure.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T010' --output Normal
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T010' --output Normal
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T005' --filter-trait 'Task=T006' --filter-trait 'Task=T009' --output Normal
```

Restore succeeded; Release build had **0 warnings and 0 errors**; the complete solution passed **110/110** with no skips. Focused T010 passed **13/13 infrastructure** and **3/3 server** tests. The existing T005/T006/T009-focused tests passed **82/82**. Tests cover real files, migration history and idempotency, existing data without history, test-only future migration, independent directories, enforced SQLite foreign keys, nullable UUID foreign keys, exact raw TEXT values and timestamp round trips, corrupt database cleanup, initialization before listening, and lock release/retry after failure. On Linux, the corrupt-file test also checks `/proc/self/fd` for leaked database handles.

## Actual application lifecycle probe

```sh
python3 specs/001-research-workspace/evidence/t010-startup-probe.py
```

The probe runs the Release server executable in Production with port `0` and a temporary private data root, inspects the real database/history, checks the existing HTTP stub, tests a second process, sends SIGTERM, restarts the server, checks an isolated test marker, and removes the temporary directory and SQLite files. Result:

```text
first startup: migration history present; no domain tables
GET /api/projects: 501 stub
second process: rejected by data-directory lock
host shutdown: exit 0
restart: marker preserved; migration applied exactly once
GET /api/projects: 501 stub
host shutdown: exit 0
temporary workspace and SQLite files removed
```

## Conventions and limitations for future schema work

- The normal host acquires its exclusive directory lock before calling `WorkspaceDb.initialize`; the context applies EF migrations and is disposed before the host can listen. SQLite pooling is disabled so connections do not outlive initialization. Factory connection strings explicitly enable foreign keys. Any later connection factory must preserve that setting.
- The initial migration and model snapshot are intentionally empty. No Workspace, ResearchProject, LibraryDocument, ProjectDocument, or PendingImport production table was introduced. A test-only migration demonstrates a subsequent upgrade. The baseline/snapshot were authored in F# without adding an EF design-time generator or `dotnet-ef`; future schema work must choose its migration-authoring tooling and update the snapshot.
- EF `Guid` and `Nullable<Guid>` properties use lowercase `D`-format TEXT. F# domain `Guid option` values are not automatically EF entity properties; translate them at the future persistence boundary. Direct SQL writes are not protected by a UUID-format CHECK constraint.
- EF `DateTime` and `DateTimeOffset` properties use round-trip UTC TEXT with seven fractional digits and `Z`. UTC values preserve ticks, local `DateTime` values are converted to UTC, offsets are normalized to zero, and `DateTimeKind.Unspecified` is rejected. These choices should be reviewed when the later entity schemas are introduced.
- Application APIs and domain schemas remain stubs; this does not claim T011, project/document persistence, browser journeys, or the 100-cycle acceptance criterion.
