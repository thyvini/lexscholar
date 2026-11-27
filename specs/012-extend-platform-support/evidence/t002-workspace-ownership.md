# T002 — workspace ownership, partial native candidate evidence

## Candidate duplicate sensitivity checkpoint

- [Native RED run 45934436496](https://github.com/thyvini/lexscholar/actions/runs/45934436496),
  PR #198 head `ba4b8fcb45391594c9ba92a67c3da235f16991cb`: 10 passed,
  1 failed, 0 skipped on macOS 15.8.2 ARM64, .NET SDK 10.0.114.
  The disposable candidate deliberately omitted `LOCK_UN`; the corrected
  canonical-path observer found the candidate descriptor and its duplicate.
  Reacquisition failed with `DirectoryAlreadyInUse` while that duplicate was
  alive, demonstrating sensitivity to omitted explicit unlock. This is RED,
  not a failure of the unmutated candidate or native GREEN evidence.
- The same observer correction passed the previously run Linux targeted
  selection: 176 passed, 0 failed, 0 skipped.
- PR #198 head `93a0c54` restores explicit unlock for native GREEN; its result
  remains pending. The production branch keeps the Darwin fail-closed guard.
- A provider safety rejection was reported for earlier work; it was not
  retried or circumvented. A separate `dev-a` launch failed because the agent
  was not found; that failure was not a provider safety rejection.

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

## Original open gates (before the native candidate)

- Darwin ARM64 creation-mode/variadic ABI, SDK flag values, errno handling,
  no-follow, native contention and explicit-unlock semantics have **not** been
  verified here. This session executed only on Linux. Do not enable the Darwin
  production path from source-level assumptions.
- Native ARM64 probe against the actual candidate binding and compiler-built C
  reference remains required by `research.md` before Darwin implementation.
  T002 remains incomplete pending that evidence and subsequent native tests.
- PR #191 [CI full solution suite](https://github.com/thyvini/lexscholar/actions/runs/45868938946) passed on Ubuntu: 1,204 tests total. The run's PR head was `fbdf91e60b2ea29aab58c7bc89502f7d10f546c5`; checkout used the PR merge commit `1861f7fbe2e711b87e8fe7777d09337ef014a86d`. This is Linux evidence only, not a Darwin result.

## Shim-free Darwin candidate

On Darwin, create the persistent lock file with managed `CreateNew`, private
creation mode and read/write sharing; on `EEXIST` open the existing file with
two-argument `open`, `O_NOFOLLOW` and `O_CLOEXEC`. Explicit nonblocking
`flock`, explicit unlock and error handling remain; Linux uses its existing
native creation flags. The managed stream remains alive until unlock and
disposal. This avoids a Darwin ARM64 variadic mode argument and a native build
and distribution dependency. Fixed-signature variadic calls were rejected
after the [native ABI discovery](darwin-arm64-abi-discovery.md). The runner
probe in PR #196 separately exercised the individual managed and native
operations, including a raw same-description duplicate. The actual candidate
must still meet the remaining gates below.

- **RED:** [macOS run 45897372825](https://github.com/thyvini/lexscholar/actions/runs/45897372825),
  `dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj -c Release --filter-trait Category=NativeOwnership`:
  one assertion failed because both initial acquisitions threw the original
  platform-not-supported error. No production candidate was present.
- **GREEN:** [macOS run 45902452874](https://github.com/thyvini/lexscholar/actions/runs/45902452874),
  the same command against the candidate files: 10 passed, 0 failed, 0 skipped.
  This includes separate child-process contention, termination and clean
  reacquisition, same-process contention, concurrent initial acquisition,
  private file creation, persistent inode witness, unexpected I/O and existing
  and dangling symlinks. Its disposable verification PR #198 includes the
  candidate files byte-for-byte, plus a separate native-only workflow; it does
  not replace or weaken the ordinary Linux CI job.
- **Linux GREEN:** `scripts/check.sh T002 T006`: 153 passed, 0 failed,
  0 skipped, warning-as-error Release build.

Native runner: GitHub-hosted macOS 15.8.2 (24H232), ARM64, .NET SDK 10.0.114;
test executable reported `net10.0|arm64`. The runner's temporary filesystem
type was not captured; these results do not establish behavior on other
filesystems or machines, and are not a macOS support claim. Concurrent-start
coverage does not force a particular interleaving of creation and reopening.
The probe demonstrates explicit unlock with a live duplicate for individual
operations, but does not observe a duplicate of the candidate's own descriptor;
T003 owns that sensitive candidate-level check. The native test selection is
not the full solution or the eventual required two-platform CI gate.

As on Linux, an actor able to replace the lock pathname in the workspace can
create a different inode and bypass the original advisory lock. This candidate
does not claim to protect that boundary or record acceptance of a new risk.
