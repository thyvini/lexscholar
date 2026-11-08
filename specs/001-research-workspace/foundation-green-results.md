# F001 foundation — T007 and T008 GREEN evidence

**Date**: 2026-11-08

**Branch**: `test/f001-foundation-red`; original T005/T006 RED commit `afa54b5`.

**Environment**: Linux x64, .NET SDK 10.0.114, xUnit v3 / Microsoft.Testing.Platform.

The original [RED report](foundation-red-results.md) is preserved unchanged. Its
statements about unimplemented configuration and locking describe that historical
RED run, not the current GREEN state.

## Typed-boundary RED before implementation

Before implementing configuration or locking, the stubs were changed only to
expose the intended `Result` signatures and discriminated unions, and host
construction was wrapped in `Ok`. Existing behavioral assertions were adapted
to require the corresponding typed cases, and checks for invalid directories,
actual typed contention, and unrelated filesystem errors were added. The
subprocess handshake records typed startup cases separately from unexpected
exceptions. No fake validation or test-side lock was added.

```sh
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build --output Detailed --report-xunit-trx --results-directory /tmp/opencode/lexscholar-typed-red
```

Build passed with zero warnings/errors. The revised suite produced **12 passed,
42 failed, 0 skipped**, exit 2: 35 T005 and 7 T006 cases. T005 defaults,
overrides, and invalid-path/limit checks failed because the loader was still a
placeholder; invalid settings still allowed a real host to start. T006 contention
checks failed because competing hosts were both ready; the new functional-boundary
checks reported the unimplemented lock rather than typed contention/real I/O.
All failures concerned missing behavior, not compilation, host setup, or process
timeouts. Detailed output was retained as `/tmp/opencode/lexscholar-typed-red.log`
in the verification environment; it is not committed as a machine-specific file.

## GREEN verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T005'
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T006'
dotnet test --solution LexScholar.sln --configuration Release --no-build --filter-not-trait 'Task=T005' --filter-not-trait 'Task=T006'
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

| Check | Result |
|---|---|
| Restore | Passed, exit 0 |
| Release build | Passed, zero warnings and errors |
| Full solution | 63 passed, 0 failed, 0 skipped |
| T005 configuration | 38 passed |
| T006 locking | 13 passed |
| Smoke / subprocess controls | 12 passed |
| Full solution repeat | 63 passed, 0 failed, 0 skipped |

Before/after snapshots around the filtered and repeated runs found no new test
directories and no remaining `--foundation-host` processes. Logs are in
`/tmp/opencode/lexscholar-foundation-green-final/` in the verification environment.

An additional bounded subprocess check ran the actual Release server executable,
not just the test host: `LexScholar__ImportLimits__MaxBytes=0` exited 1 with a
limit-specific diagnostic before creating a lock file; a second server using the
same temporary directory exited 1 with an actionable ownership diagnostic; after
SIGTERM to the owner a successor reached its loopback listener. Its temporary root
and all subprocesses were cleaned up.

## Error and ownership decisions

- `ConfigurationError` distinguishes malformed/out-of-range `InvalidLimit`,
  `NonPositiveLimit`, and `InvalidDataDirectory`. `Configuration.load` returns
  `Result`; the host maps only these expected errors to configuration diagnostics.
  Defaults are 100,000,000 bytes and 2,000 pages. Missing data-directory
  configuration resolves to `$XDG_DATA_HOME/lexscholar` when XDG is absolute,
  otherwise `~/.local/share/lexscholar`; an override must be a nonempty absolute
  path and cannot be the filesystem root or an existing file.
- `LockError.DirectoryAlreadyInUse` is returned only when Linux `flock` reports
  `EWOULDBLOCK` for the explicit nonblocking exclusive lock attempt. File open,
  permission, unsupported-filesystem, and other lock errors remain exceptions;
  tests verify unrelated I/O and a lock-file symlink are not misclassified.
- Linux's advisory `flock` is called on a `SafeFileHandle` opened with close-on-exec
  and no-follow flags. This explicit syscall is necessary because the [.NET Unix
  FileShare implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.Unix.cs)
  can ignore non-contention locking failures; merely opening a file would not prove
  that a lock was acquired. No additional package or lock-manager framework was
  added. The file is not deleted on release, avoiding stale-file and inode-replacement
  races among cooperating instances.
- Configuration is validated before acquiring the lock; the lock is acquired
  before host construction, database use, or HTTP serving. Host-owned disposal and
  `ApplicationStopped` release the safe handle on disposal or normal shutdown;
  OS descriptor cleanup releases it after forced termination. Tests cover a built
  but unstarted host, same-process and cross-process contenders, separate roots,
  normal stop, SIGKILL/reacquisition, and two HTTP clients sharing one host.

## Limitations

- The lock is Linux-only and advisory: it coordinates cooperating LexScholar
  instances, not unrelated processes that ignore locks or a party that removes or
  replaces the lock file. Locking semantics on network filesystems were not tested.
- Newly created data directories and lock files use owner-only modes (subject to
  umask). Existing directory permissions are not changed; operators should choose
  a private data directory. Storage/web-root security and other T009 work are not
  implemented here.
- Tabs are modeled as independent HTTP clients; no browser UI or domain behavior
  is claimed. The temporary SQLite database remains test scaffolding; T010 schema
  and migrations are not implemented. Other application stubs remain unchanged.
