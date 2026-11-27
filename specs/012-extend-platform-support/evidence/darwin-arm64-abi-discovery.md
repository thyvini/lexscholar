# Darwin ARM64 ABI discovery — partial, not task acceptance

Disposable probe PR [#196](https://github.com/thyvini/lexscholar/pull/196)
tested a private runner-local fixture only; no application code was exercised.
It is not a proposed production binding or permission to add a native shim.

## Hosted observations

- [Failed run 45891684359](https://github.com/thyvini/lexscholar/actions/runs/45891684359/job/140155634748):
  PR head `fbc00e568a9a3e24996e7017c39a4936a418cefd`, checked-out
  PR merge `5549d1e` into release `447c0d4`; image
  `macos-15-arm64/20261123.0558`. Direct F# fixed-signature
  `fcntl(fd, F_DUPFD_CLOEXEC, 0)` failed with errno 22 (`EINVAL`);
  the run stopped before directory fsync and did not inspect created mode.
- [Control run 45891884260](https://github.com/thyvini/lexscholar/actions/runs/45891884260/job/140156284443):
  PR head `6814c3a4553f5c8f9ea50a742b54a1bc9fb224ce`, checked-out
  PR merge `c35607e` into release `447c0d4`; image
  `macos-15-arm64/20261118.0550`. Direct F# `open` requested `0600`
  but the file mode was `0400` (`UserRead`); direct `fcntl` again returned
  `EINVAL`. Through a compiler-built C control shim, `open` created mode
  `0600` and `fcntl` returned a duplicate with `FD_CLOEXEC` set. After closing
  the original, writing, flushing and reading through the duplicate succeeded.
  `fsync` on an opened temporary directory returned success. The job passed
  because the direct-call failure was recorded as a finding, not an assertion.

Both jobs reported macOS 15.8, Apple M1 (Virtual), Darwin `arm64`,
`osx-arm64`, F# process and OS architecture ARM64, and SDK 10.0.114.
The exact macOS build and filesystem type were not recorded. Header constants
in both logs: `O_CREAT=512`, `O_EXCL=2048`, `O_RDWR=2`,
`O_DIRECTORY=1048576`, `F_DUPFD=0`, `F_DUPFD_CLOEXEC=67`,
`F_GETFD=1`, `FD_CLOEXEC=1`.

An earlier [macos-14 run](https://github.com/thyvini/lexscholar/actions/runs/45890378481/job/140151280591)
was cancelled before any steps because GitHub reported configuration
`macos-14-arm64-us-default` unsupported; it supplies no runtime evidence.

## Reproduction and limits

The exact disposable source at the successful head is
[`probe.fsx`](https://github.com/thyvini/lexscholar/blob/6814c3a4553f5c8f9ea50a742b54a1bc9fb224ce/probes/darwin-arm64/probe.fsx),
[`constants.c`](https://github.com/thyvini/lexscholar/blob/6814c3a4553f5c8f9ea50a742b54a1bc9fb224ce/probes/darwin-arm64/constants.c),
[`shim.c`](https://github.com/thyvini/lexscholar/blob/6814c3a4553f5c8f9ea50a742b54a1bc9fb224ce/probes/darwin-arm64/shim.c),
and [workflow](https://github.com/thyvini/lexscholar/blob/6814c3a4553f5c8f9ea50a742b54a1bc9fb224ce/.github/workflows/darwin-arm64-probe.yml).
On a native ARM64 Mac with SDK 10.0.114, use a fresh private temporary
directory and `umask 077`; compile the constants program with
`clang -std=c11 -Wall -Wextra -Werror`, the shim with
`clang -dynamiclib -Wall -Wextra -Werror`, write the constants program output
to a file, then run the F# script with the fixture path and constants file,
setting `DYLD_LIBRARY_PATH` to the fixture directory. The workflow gives the
exact commands. The C shim wraps `open(path, flags, (mode_t)mode)` and
`fcntl(fd, command, argument)`; both F# direct declarations instead supply a
fixed third argument to the native variadic function.

This supports an ABI concern, not a general claim about every direct binding or
Mac configuration. No no-follow, contention, live-duplicate unlock,
descriptor-observer sensitivity, anchored unlink, injected sync failure,
power-loss durability, browser, application, or physical-owner smoke was tested.
T002, T003 and T005 remain gated by their candidate-specific requirements in
[research](../research.md) and [tasks](../tasks.md).
