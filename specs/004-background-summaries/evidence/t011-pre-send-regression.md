# T011: pre-send rules unchanged (FR-006)

Tests only: the behavior already exists, so a sensitivity check replaces RED.

- Tests: `PreSendRegressionTests.fs` (T011: the three local refusals on the configured path and on the plan, 409 for a service elsewhere and for the plan without confirmation, a local service needing none; each refusal: no job (`GET` 404, empty `GET /api/summaries`), zero fake requests) and `PreSendJourneyTests.fs` (confirmation open on A, open B, return: idle, zero requests; Chromium and Firefox).
- Also: `LexScholar.Server.Tests.fsproj` compiles `PreSendRegressionTests.fs` after `ChatGptContractTests.fs` (it reuses its sign-in helpers).
- GREEN: `scripts/check.sh T011` -> `Tests: 35 total, 0 failed, 0 skipped.`
- Sensitivity check (uncommitted, reverted): skipped the confirmation checks (`SummaryEndpoints.fs` configured-service check and `ChatGptAccess.prepareSend`). `scripts/check.sh T011` -> `Tests: 35 total, 11 failed`: the plan-without-confirmation test, all five elsewhere-confirmation cases, and the browser journey in both browsers fail (also two older tests that cover the same rule). Source restored with `git checkout src`.
- Full suite: CI run on the PR.
