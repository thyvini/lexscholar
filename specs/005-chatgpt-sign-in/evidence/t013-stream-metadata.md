# T013 evidence: ChatGPT's response.metadata stream event

## Cause

Found in T012 (Codecks $13z): every summary through the ChatGPT plan failed with "The summary service did not give a usable answer." The researcher's real stream, captured with a diagnostic script (event types only):

response.created, response.in_progress, response.output_item.added, response.content_part.added, 24x response.output_text.delta, response.metadata, response.output_text.done, response.content_part.done, response.output_item.done, response.completed

`parseEvent` in `ResponsesApi.fs` treated every unknown kind as `Malformed`, so `response.metadata` (not in OpenAI's docs) discarded a complete answer. `FakeOpenAi` never sent it.

## RED and GREEN

- RED (commit 3ed2101): `scripts/check.sh T013` gave `Tests: 27 total, 1 failed, 0 skipped.` `the real stream with response.metadata after the text returns the text` failed its assertion (expected `Ok "hello"`, got an error). T006's `unknown kind` case (`response.mystery`) contradicted the agreed rule, so it became `unknown delta kind` (`response.mystery.delta`).
- GREEN: `scripts/check.sh T013 T006 T007 T008 T011` gave `Tests: 170 total, 0 failed, 0 skipped.` That run includes the server and browser tests that use `FakeOpenAi`, which now sends `response.metadata`.
- Sensitivity of `an unknown delta kind is still unusable output`, which already passed before the fix: skipping every unknown kind (uncommitted) made it fail; the change was then restored.
- CI: full suite on the PR.
