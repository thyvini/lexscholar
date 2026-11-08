# F001 foundation tests — T005 and T006 RED evidence

**Date**: 2026-11-08

**Branch / baseline**: `test/f001-foundation-red` / `e6071e3` (`release/0.1.0`).

**Environment**: Linux x64, .NET SDK `10.0.114`, xUnit v3 / Microsoft.Testing.Platform.

## Scope and outcome

Only T005 and T006 test work is complete. No production files were changed.
`Configuration.load` and `DataDirectoryLock.acquire` still throw
`NotImplementedException`, and `Program.create` still does not validate limits or
acquire ownership. T007, T008, and subsequent tasks remain incomplete.

The complete solution builds with zero warnings/errors. The full suite executes
47 cases: **12 pass, 35 fail, 0 skipped**. All 10 existing smoke cases and both
new subprocess-control smoke cases pass. The 35 failures are requirement-specific
xUnit assertions, not build, fixture, host-setup, timeout, or protocol failures.

## Exact verification commands

From repository root:

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build --filter-not-trait 'Task=T005' --filter-not-trait 'Task=T006' --output Detailed
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T005' --report-xunit-trx --report-xunit-trx-filename t005-red.trx --results-directory /tmp/opencode/lexscholar-foundation-red --output Detailed
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T006' --report-xunit-trx --report-xunit-trx-filename t006-red.trx --results-directory /tmp/opencode/lexscholar-foundation-red --output Detailed
dotnet test --solution LexScholar.sln --configuration Release --no-build --report-xunit-trx --results-directory /tmp/opencode/lexscholar-foundation-red/full-suite --output Detailed
dotnet test --solution LexScholar.sln --configuration Release --no-build --output Detailed
```

| Check | Result |
|---|---|
| Restore | Exit 0 |
| Release build | Exit 0; zero warnings/errors |
| Smoke-only run | Exit 0; 12 passed |
| T005 | Exit 2 (expected RED); 30 failed |
| T006 | Exit 2 (expected RED); 5 failed |
| Complete suite | Exit 2 (expected RED); 12 passed, 35 failed |
| Complete suite repeated | Same result; 12 passed, 35 failed |

Detailed targeted TRX reports, `full-suite.log`, and `full-suite-repeat.log` were retained under
`/tmp/opencode/lexscholar-foundation-red/` in the verification environment, not
committed as machine-specific artifacts. Both targeted reports were checked:
every failure message begins with `T005 /` or `T006 /`, identifying the assertion
and requirement rather than an unrelated exception. Authoring-time build errors
were fixed before collecting RED evidence and are not counted as RED results.

## T005 — exact failing cases (30)

Module: `LexScholar.Server.Tests.ConfigurationTests`.

### Defaults and positive overrides (6 failures)

Requirement: [specification](spec.md) clarification on PDF limits, FR-003 and
SC-008; [plan](plan.md) configuration constraints.

| Test method / inputs | Expected byte/page limits | Observed failure |
|---|---|---|
| `absent limits default to 100000000 bytes and 2000 pages` | 100,000,000 / 2,000 | Loader unimplemented |
| `positive overrides are honored and unspecified limits keep defaults`, `"1", "1"` | 1 / 1 | Loader unimplemented |
| Same method, `"250000000", "5000"` | 250,000,000 / 5,000 | Loader unimplemented |
| Same method, `"9000000000", "7"` | 9,000,000,000 / 7 (64-bit byte limit) | Loader unimplemented |
| Same method, absent bytes, `"3"` pages | 100,000,000 / 3 | Loader unimplemented |
| Same method, `"42"` bytes, absent pages | 42 / 2,000 | Loader unimplemented |

The helper catches only `NotImplementedException` and fails an assertion with
`T005 / FR-003, SC-008: Configuration.load is unimplemented; expected configured
limits, not a placeholder failure.` It does not fabricate a configuration result.
The tests also require the explicitly supplied temporary data directory to be
preserved.

### Invalid loader input and startup (24 failures)

Each row below is exercised by **both** methods:

1. `invalid limits produce a validation failure rather than a placeholder`
2. `invalid configuration prevents the application from starting`

Requirement: [plan](plan.md) Constraints and Data and Request Flow step 2;
[research](research.md) PDF validation and limits / Remaining technical notes:
malformed, zero, and negative configuration must fail startup. Numeric overflow
is invalid for the existing `int64` byte / `int` page signatures.

| Configuration key | Value | Loader observation | Subprocess startup observation |
|---|---|---|---|
| `LexScholar:ImportLimits:MaxBytes` | `abc` | Placeholder exception, not validation | Incorrectly reports `ready` |
| `LexScholar:ImportLimits:MaxBytes` | `1.5` | Same | Same |
| `LexScholar:ImportLimits:MaxBytes` | empty string | Same | Same |
| `LexScholar:ImportLimits:MaxBytes` | `9223372036854775808` | Same | Same |
| `LexScholar:ImportLimits:MaxBytes` | `0` | Same | Same |
| `LexScholar:ImportLimits:MaxBytes` | `-1` | Same | Same |
| `LexScholar:ImportLimits:MaxPages` | `abc` | Same | Same |
| `LexScholar:ImportLimits:MaxPages` | `1.5` | Same | Same |
| `LexScholar:ImportLimits:MaxPages` | empty string | Same | Same |
| `LexScholar:ImportLimits:MaxPages` | `2147483648` | Same | Same |
| `LexScholar:ImportLimits:MaxPages` | `0` | Same | Same |
| `LexScholar:ImportLimits:MaxPages` | `-1` | Same | Same |

Loader assertions reject `NotImplementedException` as a valid validation result.
Startup assertions require rejection, a nonzero exit, and a nonempty diagnostic;
a placeholder exception is not an acceptable rejection. Currently, every child
reaches a real loopback listener, making missing startup validation observable.

## T006 — exact failing cases (5)

Module: `LexScholar.Server.Tests.ProcessLockTests`.

Requirement: [plan](plan.md) Data and Request Flow step 1 and single-instance
lock risk; [research](research.md) Process exclusivity and tabs: OS-held exclusive
ownership, refusal of a second process, release after exit, independent roots,
and tabs sharing the same process. Normal exit and forced termination exercise
both release paths requested for T006.

| Test method | Sequence exercised | Observed failure |
|---|---|---|
| `a second operating system process cannot own the same directory` | Live owner, distinct-PID contender, same root | Contender incorrectly reports `ready` |
| `normal owner exit releases actual exclusive ownership` | Contend while owner lives, gracefully stop owner, start successor without altering root | Contender was not refused while owner lived |
| `termination releases actual exclusive ownership without deleting lock artifacts` | Contend, kill/reap owner, restart against untouched root | Contender was not refused while owner lived |
| `independent directories have independent exclusive owners` | Two live owners on distinct roots, then a contender per root | Contenders incorrectly start; assertion fails on first refusal check |
| `two tab clients share the running owner without acquiring another lock` | Two concurrent HTTP sessions against one owner, then a contender | Sessions reach the host, but contender incorrectly starts |

Every failure is the assertion `T006 / plan process exclusivity: a second OS
process served requests while the same data directory already had an owner.`
No first-owner startup, child handshake, or shutdown failure was counted as RED.

The release tests execute shutdown and successor startup **before** asserting the
earlier contention result, so the whole lifecycle executes even in RED. They
cannot pass merely because an unlocked directory can be reopened. Actual lock
release is **not verified yet**, because exclusive ownership is unimplemented.

## Test-only mechanism and isolation

- The existing test executable re-executes itself in `--foundation-host` mode;
  there is no extra project, package, external script, or production entry point.
  Its normal xUnit/MTP dispatch is preserved.
- Children use the real `Program.create` / ASP.NET Core startup. No validation or
  locking is performed on the application's behalf by the tests.
- A flushed JSON startup handshake includes the child PID and actual listener
  address, or the startup exception. OS-assigned loopback ports avoid reservation
  races. There are no fixed sleeps or readiness polling loops.
- Startup/HTTP/shutdown waits are bounded to 10 seconds. A 45-second child stdin
  watchdog/EOF handles an abandoned parent. `use` cleanup gracefully stops live
  children and kills/reaps them if necessary; forced termination kills the process
  tree. stderr is drained asynchronously.
- Temporary data ownership is separated from host lifetime so contenders and
  successors share only the explicitly supplied test root. Host configuration
  sources are cleared and replaced with test settings; developer configuration,
  production paths, and inherited environment overrides are not used.
- Existing harness checks still verify real temporary SQLite connectivity,
  independent roots, listener shutdown, HTTP 501 stubs, and cleanup. The new child
  controls prove HTTP startup, distinct OS PID, normal shutdown, and kill/reap work.
- Before/after temporary-root snapshots and `/proc` argument checks around both
  complete runs found **zero new directories left behind and zero remaining
  `--foundation-host` processes**. No repository or production data was modified.

## Limitations and assumptions

- The plan does not prescribe configuration key spellings. The tests use
  `LexScholar:ImportLimits:MaxBytes` / `MaxPages`, matching the T045 configuration
  record, alongside the existing `LexScholar:DataDirectory`. An explicitly empty
  value is treated as malformed, not as an absent/default setting.
- Two independent `HttpClient` sessions model browser tabs for host ownership.
  No browser automation, working UI, or domain response is claimed; routes remain
  HTTP 501 stubs.
- Lifecycle and independent-root assertions include exclusivity to avoid vacuous
  passes. Reacquisition and tab transport currently work without a lock, which is
  not evidence of correct OS ownership or its release.
- Tests use the shared production host construction function, not deployment or
  static-WASM hosting. No schema, imports, application behavior, or T009 security
  implementation is added. GREEN requires separate approval.
