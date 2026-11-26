# Darwin ARM64 probe evidence

- Base: `origin/release/0.5.3` at `5b868c1`; probe originally prepared on
  `release/0.5.2` and cherry-picked without changing the original worktree.
- Local compilation: `dotnet fsi probes/darwin-arm64/probe.fsx /tmp /dev/null`
  compiled and then rejected the local x64 OS and process as intended.
- Native execution: initial GitHub push lacked the `workflow`
  scope; the refreshed credential permitted PR #196. The first Actions job
  was cancelled before any steps: GitHub reported configuration
  `macos-14-arm64-us-default` is not supported
  ([run](https://github.com/thyvini/lexscholar/actions/runs/45890378481)).
  The `macos-15` [run](https://github.com/thyvini/lexscholar/actions/runs/45891684359)
  reached Darwin 15.8, Apple M1 virtual, SDK 10.0.114, ARM64 OS and process.
  SDK headers reported `O_CREAT=512`, `O_EXCL=2048`, `O_RDWR=2`,
  `O_DIRECTORY=1048576`, `F_DUPFD=0`, `F_DUPFD_CLOEXEC=67`,
  `F_GETFD=1`, `FD_CLOEXEC=1`. Direct F# fixed-signature `fcntl` returned
  `EINVAL` (22) for `F_DUPFD_CLOEXEC`; the run stopped before directory fsync.
  The [follow-up run](https://github.com/thyvini/lexscholar/actions/runs/45891884260)
  passed on the same reported platform: direct fixed-signature F# `open` with
  `O_CREAT` and requested mode `0600` produced `UserRead` (`0400`), and direct
  fixed-signature `fcntl` again returned errno 22. A fixed-signature F# call to
  a C shim that invokes the variadic functions correctly created mode `0600`,
  duplicated the descriptor with `FD_CLOEXEC`, and read/wrote through the
  duplicate after closing the original. Directory `fsync` returned success.
  The direct calls are not validated for production; their results illustrate
  the ABI hazard on this runner, not a portable fallback.
- The probe prints constants from the installed SDK headers, checks the runtime
  architecture, exercises `open` with `O_CREAT` and mode, tests a close-on-exec
  `fcntl` duplicate after closing the original, and attempts directory `fsync`.
  Its eventual results apply only to the tested runner and filesystem; they do
  not establish general durability guarantees.
