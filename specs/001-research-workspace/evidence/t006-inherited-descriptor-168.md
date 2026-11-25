# T006 / $168: ownership release with an inherited descriptor

## Incident and conclusion

[CI run 45660667811, attempt 1](https://github.com/thyvini/lexscholar/actions/runs/45660667811/attempts/1)
failed at `RestartTests.fs:107`: constructing the next in-process host returned
`LockFailure (DirectoryAlreadyInUse ...)`. The test had run for 56.665 seconds.
Attempt 2 passed on the same head (`22b3c18a0662d546706c83118cfc18347e7aa2e2`).
A rerun is not a correction.

**Proven defect:** closing the owner's descriptor does not release `flock`
ownership while a forked child retains the same open file description.
`O_CLOEXEC` closes the child's reference at **exec**, not at **fork**. An
unrelated process launch can therefore transiently retain a disposed host's
ownership and reject the next host.

**Historical attribution remains uncertain:** the CI log has neither descriptor
traces nor the identity of the process retaining this particular lock. The
reproduced mechanism explains the observed symptom, but does not prove which
child, if any, caused that occurrence.

## Investigation

- `TestHarness.Dispose` awaits host stopping and disposal. The restart test
  disposes each iteration's harness before constructing the next. No missing
  await or shared workspace between tests was found.
- Production registers release on `ApplicationStopped` and DI disposal, and
  releases ownership on initialization failure. Previously, all these paths
  only closed the `SafeFileHandle`.
- Linux open flags already include `O_CLOEXEC` and `O_NOFOLLOW`; acquisition is
  an atomic, nonblocking exclusive `flock`. The persistent lock file is never
  unlinked.
- [.NET 10 native process launch](https://github.com/dotnet/runtime/blob/v10.0.0/src/native/libs/System.Native/pal_process.c)
  uses fork/vfork followed by descriptor redirection and `execve`. vfork blocks
  its calling thread, not the other thread disposing/restarting a host.

Two local diagnostics established the mechanism without scheduling sleeps:

1. A native helper forked while the production lock was held, leaving the child
   blocked on a pipe. After owner disposal, production acquisition returned
   `DirectoryAlreadyInUse`; after releasing and reaping the child, it succeeded.
2. An `LD_PRELOAD` diagnostic shim intercepted `execve("/usr/bin/true", ...)`
   during a real .NET `Process.Start`. It signalled arrival through one FIFO and
   awaited release through another. Another managed thread disposed the owner
   and attempted acquisition while the child was held before exec. The result
   was again `DirectoryAlreadyInUse`, followed by success after the child
   proceeded. The shim was diagnostic only, not added to production or CI.
   With the corrected binary, the same pre-exec barrier produced `Ok` for
   immediate acquisition while the child was still held, and acquisition after
   child exit also succeeded.

The committed regression avoids native helper compilation and managed execution
after fork: `fcntl(F_DUPFD_CLOEXEC)` retains the same open file description
deterministically. Like inheritance at fork, this leaves a second reference
alive when the owner is disposed. It asserts live-owner exclusion, immediate
successor acquisition with that reference still open, and exclusion after
repeated old-owner disposal.

## Correction

Explicit `flock(LOCK_UN)` precedes descriptor closure on successful leases.
Disposal is serialized and guarded by `IsClosed`, so concurrent/repeated release
cannot unlock a reused descriptor or a successor. Closure still runs if unlock
fails, and unexpected unlock errors propagate as I/O failures. Acquisition,
permissions, startup order, persistent inode and live-owner exclusion are
unchanged. No sleeps, retries, test exclusions or weakened locks were added.

## Verification

Environment: Linux 7.3.5-2-cachyos, .NET SDK 10.0.114, non-root local workspace,
based on `origin/release/0.5.1` at `9390ab4`.

- RED: `LOG=/tmp/opencode/168-red.log scripts/check.sh T006` — 76 tests,
  1 failed, 0 skipped. The new regression failed with
  “Disposed owner retained ownership through an inherited descriptor”.
- A concurrent baseline command,
  `scripts/check.sh T041 T006 T029 T030`, also included the newly added
  regression: 88 tests, that same single failure, 0 skipped. It was not a clean
  pre-test baseline.
- First GREEN command (`scripts/check.sh T006 T041 T010 T022 T043 T029 T030 T003
  T007 T014`) was terminated by the tool's 120-second deadline during browser
  coverage, with no reported assertion failures so far. This is not a passing
  result. Reissued with the tool deadline disabled, not a test retry policy.
- GREEN: `LOG=/tmp/opencode/168-green-complete.log scripts/check.sh T006 T041
  T010 T022 T043 T029 T030 T003 T007 T014` — build passed with
  `-warnaserror`; 429 tests passed, 0 failed, 0 skipped. This covers the new
  descriptor regression, existing live-owner/process-exit checks, 100 in-process
  restarts, startup/migration failure release, storage cleanup and selected
  coordination/export/recovery concurrency coverage. Shared task IDs also
  select tests from other features and projects.
- Full-suite CI: pending a future PR; no PR opened by this workstream.

## Limits and impact

The demonstrated impact is a spurious startup refusal during a concurrent
process launch, including in-process restart checks. This evidence does not
establish data corruption or a historical live-owner exclusion failure.
The fix does not change the existing fallback of kernel descriptor closure
after abrupt process termination; existing process-termination coverage remains
applicable. A descendant that deliberately retains an inherited descriptor after
an abrupt owner death is outside the normal close-on-exec launch lifecycle.
The incident card must remain open until the lead accepts the correction and
the required PR checks pass.
