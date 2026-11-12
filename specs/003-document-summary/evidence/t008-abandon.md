# T008 abandon on dismiss, document change or project change (risky: concurrency)

- RED: `scripts/check.sh T008` at the RED commit: 4 total, 2 failed. Dismiss and reader change while generating returned `None` where `Some (Cancel 1)` was expected. The contract test and the "second request ignored" test already held (the token was already passed to every service call by T006; `Summarize` already ignored a request while one ran).
- GREEN: `scripts/check.sh T008`: `Tests: 4 total, 0 failed, 0 skipped.`
- CI: full suite on the PR (see PR checks).

## Decisions and limits

- Client: `Summary.update` emits `Cancel request` when Dismiss or a reader change leaves a `Generating` state; `followReader` now returns that effect. `App.fs` keeps the `CancellationTokenSource` per request number and passes its token to the antiforgery call, the send and the line reads. Late events are dropped by the existing request-number match. Cancelling from `Confirming` has no request to abort.
- Server: the aborted request ends with an information log naming only the document id; `ask` throws on a cancelled token before any service call, so no further chunk is sent whatever the capability does with the token.
- Contract test uses handshakes on the fake (`Waiting`, `Cancelled`), no sleeps; it checks the fake saw the cancellation and received exactly two requests after the host stopped.
- Sensitivity check (behaviour already existed): with the endpoint's `summarize` call given `CancellationToken.None` (temporary, uncommitted), the contract test failed with a timeout waiting for the fake to see the cancellation; reverted, 4 pass.
- Not covered: the log line is not asserted (the harness has no log capture for the endpoint); the browser `fetch` abort itself is exercised only by manual validation (T012). The combine round cap (Greptile P2 from T006) still has no test: reaching it needs a service that shrinks the points by one per round, not cheap here.
