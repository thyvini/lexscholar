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
  A follow-up tests a C variadic-call shim as a control and records direct open
  mode separately. Its result is pending.
- The probe prints constants from the installed SDK headers, checks the runtime
  architecture, exercises `open` with `O_CREAT` and mode, tests a close-on-exec
  `fcntl` duplicate after closing the original, and attempts directory `fsync`.
  Its eventual results apply only to the tested runner and filesystem; they do
  not establish general durability guarantees.
