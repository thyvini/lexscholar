# Darwin ARM64 probe evidence

- Base: `origin/release/0.5.3` at `5b868c1`; probe originally prepared on
  `release/0.5.2` and cherry-picked without changing the original worktree.
- Local compilation: `dotnet fsi probes/darwin-arm64/probe.fsx /tmp /dev/null`
  compiled and then rejected the local x64 OS and process as intended.
- Native execution: **not run yet**. Initial GitHub push lacked the `workflow`
  scope; the refreshed credential permitted PR #196. The first Actions job
  was cancelled before any steps: GitHub reported configuration
  `macos-14-arm64-us-default` is not supported
  ([run](https://github.com/thyvini/lexscholar/actions/runs/45890378481)).
  The probe now requests `macos-15`, still with explicit native architecture
  checks.
- The probe prints constants from the installed SDK headers, checks the runtime
  architecture, exercises `open` with `O_CREAT` and mode, tests a close-on-exec
  `fcntl` duplicate after closing the original, and attempts directory `fsync`.
  Its eventual results apply only to the tested runner and filesystem; they do
  not establish general durability guarantees.
