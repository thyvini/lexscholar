# T005: failures kept and shown on return

No RED: T002-T004 already keep every failure as the job's `Failed` state (server) and render it on return (client), so these tests describe existing behavior and a sensitivity check replaces RED. No production file changed in this slice.

- Tests (`scripts/check.sh T005`): 62 tests, 0 failed, 0 skipped. They cover a service error (code, detail, no service body), `not_enough_text` with the omitted count, a plan usage limit from `FakeOpenAi`, a new POST after a failure starting a new job (both services), every failure code read from a `GET`, a failure found on return with its actions, and the journey (fail while on B, return to A, message shown, Try again) in Chromium and Firefox.
- Sensitivity 1 (uncommitted): `StartWith` treats a failed job like a finished one (no replacement). 4 tests failed, including both retry contract tests; restored.
- Sensitivity 2 (uncommitted): the endpoint stores an empty detail for a service failure, and the client's `Polled (RemoteFailed ...)` goes to `Idle`. 4 tests failed (service error detail, failure on return, journey in both browsers); restored.
- Limitation: the journey cannot order the failure before the return (the fake's error answers at once); the assertion holds whichever comes first, since the failure belongs to the job. The "she is away when it fails" ordering is covered by the contract tests, which read the state after the job ended with no client involved.
- Full suite: CI run on the PR.
