# Darwin ARM64 probe evidence

- Base: `origin/release/0.5.2` at `2885499`.
- Local compilation: `dotnet fsi probes/darwin-arm64/probe.fsx /tmp /dev/null`
  compiled and then rejected the local x64 OS and process as intended.
- Native execution: **not run**. GitHub rejected the branch push because the
  available OAuth token lacks the `workflow` scope. An SSH push also failed
  because no authorized public key was available. No PR or runner was launched.
- The probe prints constants from the installed SDK headers, checks the runtime
  architecture, exercises `open` with `O_CREAT` and mode, tests a close-on-exec
  `fcntl` duplicate after closing the original, and attempts directory `fsync`.
  Its eventual results apply only to the tested runner and filesystem; they do
  not establish general durability guarantees.
