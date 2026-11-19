# T025 — Regenerate a summary with optional feedback

## Implementation

- Portuguese bounded transient feedback on the saved summary, with fresh explicit sending consent on every replacement. Cancelled consent retains the draft and sends nothing.
- Reuses T023's `instruction` input on T024's `/regenerate` endpoint. No backend change was needed: the shared workflow already forwards this input through single/map/intermediate/final combine, with Portuguese/source-grounding/citation requirements and privacy tests.
- Keeps the saved summary in `Shown` while the independent replacement lifecycle runs/fails/cancels. Returning to a document reads the durable version first and resumes replacement-aware polling. Only successful validated completion swaps the displayed version.
- Stop uses antiforgery-protected POST `/cancel`, never legacy DELETE. An in-flight POST is drained with another cancellation after its answer; outstanding cancellations block another regeneration, avoiding cancellation of newly requested work.
- Existing citation rendering and account/usage failure handling are reused. No raw feedback persistence, history, logging, new package or schema change.

## RED / GREEN

- RED: `scripts/check.sh T025` — warning-as-error build passed; two browser assertions failed (Chromium and Firefox): the existing summary had no feedback field. 12 tests, two failures, zero skips; `/tmp/lexscholar-check.ggneDb`.
- GREEN: `scripts/check.sh T025 T023 T024 T004 T008` — warning-as-error build; **177 passed, zero failures/skips**. Includes both-browser fresh-consent cancellation, held replacement with library navigation, atomic success, invalid-citation rejection, upstream failure and nondestructive stop; state tests include late responses, bounded feedback, failed cancellation and in-flight POST draining.
- T023/T024 inherited prompt/privacy and HTTP integration tests run in the same command, covering instruction forwarding for configured and plan regeneration and all prompt stages. Browser tests assert the actual feedback reaches the Responses request and no DELETE occurs during replacement.
- CI/full suite: pending lead handoff; no PR opened and no merge performed. CI must run the full suite on the eventual PR.

## Environment / limits

- Linux, pinned .NET SDK, Playwright Chromium and Firefox, isolated `TestHarness` workspaces and fake OpenAI responses. No researcher data or real upstream service used.
- Tests establish prompt propagation, privacy boundaries and validated citation handling, not real-model adherence to feedback or Portuguese.
- Based on `22b49d5` from #132, whose tree matches squash `909171a` on `release/0.4.0`. The task branch is rebased onto that release commit for handoff so only T025 remains in its diff.
