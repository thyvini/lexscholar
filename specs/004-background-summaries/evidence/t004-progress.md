# T004: progress while a summary runs and on return

- RED (`scripts/check.sh T004`): 2 failed. The contract test failed on the missing `pagesProcessed` in the running state; the `parseRemote` test failed with `RemoteRunning None` for a body with progress. The commit also carries the `RemoteRunning of Progress option` signature change, with the state tests that already hold for it.
- GREEN (`scripts/check.sh T004 T006`): 82 tests, 0 failed. The journey (`returning to a document while its summary runs...`, chromium and firefox) is covered by `scripts/check.sh T004`: 37 tests, 0 failed.
- Sensitivity: with polling removed from `Polled (RemoteRunning ...)` in `Summary.fs` (uncommitted), `scripts/check.sh T004` failed 4 tests (2 state tests, the journey in both browsers); restored afterwards.
- Full suite: CI run on the PR.
