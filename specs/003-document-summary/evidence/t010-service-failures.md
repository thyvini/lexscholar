# T010: service failures and retry

Risk: touches the outgoing call (body-read timeout), so it gets a GPT review.

## RED (commit "test(f003): service failure details, stalled body, retry")
`scripts/check.sh T010` -> 23 total, 5 failed: the failure details were one generic text (no Model/Temperature/MaxOutputTokens for a 400, unreachable and timeout indistinguishable), the client state/text test, and a stalled response body left the call pending ("the call is still pending after its timeout", 10 s guard).

## GREEN
`scripts/check.sh T010` -> 23 total, 0 failed, 0 skipped.
- `Summarize.failureDetail`: LexScholar's own text per failure; only a 400 names `Model`, `Temperature`, `MaxOutputTokens`; never the service body (contract test also checks the body text is absent).
- `ChatCompletions.send`: the body read is bounded by `client.Timeout` (linked token), so a mid-body stall is `TimedOut`; the caller's cancellation still propagates as a cancellation.
- Client: `Failure.ServiceFailed` carries the server's detail; the failed state shows it with a "Try again" button that starts a new request.
- Follow-up (b): the endpoint logs readable/unreadable page counts with the document id only.

## Sensitivity (uncommitted breaks, both reverted)
Tests that cover behaviour that already existed: breaking the cancellation arm in `ChatCompletions.send` fails "a caller's cancellation while the body stalls..."; letting `Responded` be accepted outside `Generating` fails "abandoning a request is never shown as a failure...".

## Limitations
- Follow-up (c): the combine-round cap of 5 has no test. A scripted service that shrinks the points by exactly one per round across 5 rounds needs a long fixture; not cheap, left untested.
- The body timeout reuses `HttpClient.Timeout` (100 s default); it is not configurable.
- Full-suite CI run: see the PR.

## Greptile (2 x P2)
- Stalling test server never ended its connection: must fix, done (cancelled on dispose).
- No browser coverage of the parsed `detail` and the "Try again" button: must fix, added a Chromium/Firefox journey (400 first, then a summary); `scripts/check.sh T010` -> 25 total, 0 failed.

## GPT review (1 x P2)
Stalled test server's disposal could hang if the client never connected or left early: must fix, done. The stop token now also cancels the accept, header read and write, and the listener is stopped before the loop is awaited.
