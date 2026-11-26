# T002 — Linux ownership checks; Darwin blocked

Original test and sensitivity base: `607c545` (`docs/platform-expansion-phase3`).
Integrated onto `598dd97` (release baseline after #190). This is partial T002
evidence, not completion of the task or a macOS support claim.

## Changes

- Tag the existing applicable process-lock checks with `Task=T002`, retaining
  `T006` for historical checks. They cover same-process and child-process
  contention, no-follow, private creation modes, persistent lock file,
  unexpected filesystem/startup I/O, clean shutdown, termination and reacquisition.
- Request a different port for the second child, held by a test listener.
  Require the typed workspace-ownership rejection, not a bind error.
- Add a Linux inode-lifetime observer. A raw read-only, close-on-exec open
  deliberately holds the existing inode without taking a managed sharing lock.
  `/proc/self/fd` must still identify the linked pathname during ownership,
  release and reacquisition. Check original bytes and 0600 mode as well.
  Repeated disposal of the old owner must leave its successor exclusive.
- Production `DataDirectoryLock.fs` is unchanged: explicit unlock, Linux flags,
  no-follow and persistent inode behavior remain intact. No Darwin interop,
  shim, fallback or general portability layer was introduced.

## Sensitivity / RED

Command: `scripts/check.sh T002`.

Temporary, uncommitted mutation: after disposing the owning handle, unlink the
lock file, recreate it with the same test bytes, and restore mode 0600.

Result: **87 tests, 1 failed, 0 skipped**. The new inode test failed its pathname
assertion because the held descriptor identified `.lexscholar.lock (deleted)`.
The existing file-existence/mode test still passed. This demonstrates detection
of inode replacement, not merely disappearance or changed permissions.
Local failure log: `/tmp/lexscholar-check.oQx31s`.

Earlier observer iterations exposed managed file-sharing interference; the
final test uses raw Linux open and `RandomAccess.Read` on that handle so the
observer neither owns the lock nor opens a competing managed stream.

## GREEN

Removed the entire mutation, restoring production code byte-for-byte.

Command: `scripts/check.sh T002 T006`.

Result: **151 tests, 0 failed, 0 skipped**, Release solution build with
`-warnaserror`. Counts include other features sharing these task identifiers;
this was the targeted selection, not the full suite. The same command passed
again with **151 tests, 0 failed, 0 skipped** after integration onto `598dd97`.

Environment: Linux `7.3.5-2-cachyos`, x86-64, .NET SDK `10.0.114`.
The worktree is on tmpfs; tests use the harness's isolated temporary workspaces.
The new descriptor observer is Linux-specific and is not a Darwin observer.
The inherited-description test remains unchanged under historical `T006`;
T003 owns its future Darwin extension and explicit-unlock sensitivity.

## Open gates

- Darwin ARM64 creation-mode/variadic ABI, SDK flag values, errno handling,
  no-follow, native contention and explicit-unlock semantics have **not** been
  verified here. This session executed only on Linux. Do not enable the Darwin
  production path from source-level assumptions.
- Native ARM64 probe against the actual candidate binding and compiler-built C
  reference remains required by `research.md` before Darwin implementation.
  T002 remains incomplete pending that evidence and subsequent native tests.
- CI full-suite result: pending PR checks. No `tasks.md` edits, release, risk
  acceptance or support announcement.
