# T004 — Summary in the main area

- RED: `scripts/check.sh T004` — 39 tests, two failures, zero skipped. Both browsers found the embedded iframe when the new test expected none.
- GREEN: `scripts/check.sh T004` — 49 tests passed, zero skipped; build with `-warnaserror`.
- A broader affected-task run was stopped by its 120-second timeout, with no test summary; it is not counted as a pass.
- The reader's content endpoint remains covered by `ReaderContractTests.fs` (including missing and unknown documents and range requests); role PATCH coverage is retained in `RoleApiTests.fs`. Reader-only frame, navigation and pre-check cases were removed with those behaviors.
- CI full suite: pending PR.
