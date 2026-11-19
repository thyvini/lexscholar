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

## PR #134 review follow-up

All reported findings are **must fix**:

- P1 navigation during POST admission: retain per-pair submission identities across navigation, restore submitting state when returning, and consume the eventual response with a fresh watch. Both response-before-saved-fetch and response-after-saved-fetch permutations have deterministic state regressions.
- P1 failed cancellation / uncertain POST transport: a lost answer cannot prove no job exists. Fence pending admission with nondestructive POST `/cancel`; retain per-pair uncertainty across navigation until cancellation succeeds. Discovery alone cannot clear that uncertainty (a pending admission may not be visible yet). Failed cancellation offers another Stop, not another regeneration. Only after a successful fence and replacement-aware reconciliation can new feedback be submitted.
- P1 completion/cancellation race: successful cancellation now requests `/regenerate` through a replacement-aware observation, not a legacy fetch ignored while `Shown`. A completed atomic replacement is adopted even if completion won the race.
- P1 draft loss during discovery: observing the already displayed durable version preserves a newly typed draft; actual replacement with a different version clears it.
- P2 terminal failure deletion: terminal replacement errors no longer disable explicit deletion; its existing confirmation/cancel behavior remains intact. Uncertain/running operations remain protected.

RED: `scripts/check.sh T025` — **six deterministic assertion failures**, 23 tests, zero skips (`/tmp/lexscholar-check.SC7eSc`), covering all four original review findings plus uncertain POST and discovery draft loss.

GREEN: `scripts/check.sh T025 T023 T024 T004 T008` — warning-as-error build, **185 passed, zero failures/skips**. Additional deterministic coverage keeps uncertainty fenced across navigation even when discovery sees the old saved version; both browser journeys also verify terminal failure deletion is enabled and cancellation preserves the summary.

Full-suite CI and fresh GPT review remain required on the pushed PR head. No merge performed.
