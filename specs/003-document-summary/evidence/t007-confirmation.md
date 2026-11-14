# T007 confirmation before text leaves the machine (risky: privacy, host security)

- RED: `scripts/check.sh T007` at 310ea45 (stub `classify` returns Local, no 409, no dialog state): 28 tests, 16 failed. Classifier cases failed on `Expected Elsewhere, Actual Local`; the contract tests on `Expected Conflict, Actual OK` (the fake received the text); the state tests on the missing `Confirming` state and `Send` effect.
- GREEN: `scripts/check.sh T007 T001 T002 T003 T004 T006 T009`: `Tests: 168 total, 0 failed, 0 skipped.`
- CI: full suite on the PR (see PR checks).
- This slice closes the GPT P0 from the T003 review (a service elsewhere received text without confirmation).

## Decisions and limits

- One classifier: `ServiceLocation.classify` (Domain). `ChatCompletions.createHandler` uses it; its private `isLocalHost` is gone.
- The 409 is returned after the availability, page-count and readability checks and before `summarizeOnce`, the only code that creates an outgoing client, so no service call can precede it. Tests assert the fake recorded zero requests.
- `confirmSend` counts only when the body is a JSON object with the boolean `true`. A missing, malformed, oversized (over 4 KB) or non-boolean value is not a confirmation. A local service ignores it.
- The 409 body has `code`, `serviceHost` (the endpoint authority, host and port) and `model`; the log line for it has host and model only.
- A service elsewhere keeps the default proxy behaviour (the researcher confirmed that send); redirects stay off.
- Nothing is remembered: each request is judged by its own `confirmSend`, and the client's Cancel emits no effect.
- Not covered here: the manual `strace` check of SC-006 (T012).
- Greptile P2 "no browser coverage": must fix, done. A journey (Chromium and Firefox) with an elsewhere endpoint checks the dialog text, Cancel sends nothing (zero fake requests), asking again asks again, and Send gives the summary with exactly one request. `scripts/check.sh T007`: `Tests: 30 total, 0 failed, 0 skipped.`
- Greptile P2 "focus and aria-describedby on the dialog": overkill for this slice (no accessibility requirement in the spec; needs JS interop for focus); the dialog is a native-button flow with `role=alertdialog` and a label.

## Follow-up: confirmation bound to the service shown (GPT P1)

- RED at the follow-up test commit: `scripts/check.sh T007`: 37 tests, 7 failed (bare `confirmSend: true`, wrong or missing or non-string host/model: `Expected Conflict, Actual OK`; the state test: effect carried an empty host and model).
- A request is confirmed only with `confirmSend: true` and `confirmedHost` and `confirmedModel` equal to the current configured authority and model; otherwise 409 with the current values and zero service requests. Contract, FR-014 wording and research §2 updated (owner-approved).
- Greptile P2 (non-object body `null`, `[]`, `true` threw `InvalidOperationException`, 500): must fix, done. RED: 42 tests, 5 failed (500 instead of 409/200); GREEN after checking `ValueKind` first.
