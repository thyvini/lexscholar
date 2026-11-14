# T006 evidence: Responses client and model listing

- RED (`c7bc83f`): `scripts/check.sh T006` gave `Tests: 40 total, 15 failed` (stubs returned `UnusableOutput`; assertions such as `Expected: Error UsageLimitReached` and the empty request list failed).
- GREEN: `scripts/check.sh T006` gave `Tests: 40 total, 0 failed, 0 skipped.` (includes F003 tests that share the tag).
- Full suite: CI run on the PR.
- Covered: request has exactly `model, instructions, input, store, stream` and the Bearer token; system messages become `instructions`; deltas joined; each error code before the stream and as `response.failed`; `response.incomplete`, an ended stream, an empty answer and a stream over 1 MB give `UnusableOutput`; redirect not followed; unknown refusals give only the sanitized message (token and newlines removed); stalled before and after the first event gives `TimedOut`; cancellation stays a cancellation; logs carry only model, count, outcome, duration; model list read in server order; new failure texts.

## Limitations

- The models response shape `{"models":[{"slug","visibility"}]}` is **unverified** against the live endpoint. Public sources (Codex's bundled model catalog) use `models` with `slug` and `visibility` (`list`, `hide`, `none`), but no document shows the raw `GET /v1/models` envelope. Confirm in T012; an unreadable list is `UnusableOutput`, which the summary reports as an unusable service answer.
- A mid-stream `response.failed` with an unknown code reports `ErrorStatus (200, message)`, since the stream's HTTP status was 200.

## Greptile P1 (fixed)

The decoder's character buffer was as large as the byte buffer, so a multi-byte character split across two reads could overflow it. RED (`test(f005): multi-byte character split across reads`): `ArgumentException: The output char buffer is too small`. GREEN: buffer sized with `Encoding.UTF8.GetMaxCharCount`; `scripts/check.sh T006` gave `Tests: 41 total, 0 failed, 0 skipped.`

## GPT review P2 (fixed): one deadline

The body read got a fresh timeout after the headers, so a call could take nearly twice the limit (F003's `ChatCompletions` has the same pattern; not changed here, outside this slice). RED: `the time limit is one deadline for the headers and the stream together` failed with `the call took 1500 ms` (headers after 500 ms, 1 s limit, stalled stream). GREEN: one linked deadline created before the send covers headers and stream; `scripts/check.sh T006` gave `Tests: 42 total, 0 failed, 0 skipped.` (That wall-clock test was replaced, see the next section.)

## GPT review round 2 (fixed)

- P1, malformed text events: a data line that is not JSON, or a `response.output_text.delta` without a string `delta`, was ignored, so a later `response.completed` could return partial text. Now it is `UnusableOutput`. RED: `a malformed text event is unusable output and never a partial summary` failed (partial text returned as success).
- P2, deterministic deadline: `completeWithDeadline` takes the deadline token (`complete` supplies one from `client.Timeout`; the same deadline covers headers and stream). The two tests fire the deadline at a handshake (stream stalled after the first event; request waiting for headers) and assert `TimedOut`, with no wall-clock margin; a 10 s guard only stops a hang. RED: both failed on the guard (the stub ignored the deadline).
- GREEN: `scripts/check.sh T006` gave `Tests: 44 total, 0 failed, 0 skipped.`

## GPT review round 3 (fixed)

- P1, whole stream parser hardened: an event is now `event:` name plus `data:` lines, complete at its blank line or at the end of the stream. The kind is the JSON `type`; the name must agree. A text delta needs the exact kind and a string `delta`. Events that name or type a text delta incompletely, whose kind is undeterminable or unknown, whose name and type disagree, or whose data is not a JSON object are `UnusableOutput`; only known harmless kinds (lifecycle `.added/.done/.created/.in_progress/.queued`, ping, comments, reasoning and tool-argument deltas) are skipped. RED: the table-driven test `an event that is, may be or cannot be told apart from a text delta is unusable output` (12 cases) failed on the first case (name says text delta, JSON has no type). Guard tests for harmless events and an unterminated final event passed before and after.
- P2, a deadline reached while reading a refusal's body was swallowed as `ErrorStatus`; it is now `TimedOut` (cancellations are no longer caught there). RED: `the deadline reached while reading a refusal's body is a timeout` failed.
- GREEN: `scripts/check.sh T006` gave `Tests: 47 total, 0 failed, 0 skipped.`
