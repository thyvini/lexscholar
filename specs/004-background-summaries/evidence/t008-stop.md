# T008 evidence: Stop, nothing more sent, nothing kept

Risky (concurrency). Covers FR-012. AR 3 and AR 10 apply.

## Environment

- Linux 7.3.3 (CachyOS), .NET SDK 10.0.114 (`global.json`), Release build with `-warnaserror`.
- In-process host (`TestHarness`) with `FakeChatService` (`Hold` and `Cancelled` handshakes, no sleeps); Chromium and Firefox via Playwright for the journey. Each test uses a temporary workspace.

## Change

- `SummaryJobs.Stop key` (replaces `Dismiss`): under the registry lock it removes the job, then (a running job) cancels and disposes its token source, then deletes the stored row. Removing first means a late `Complete`, `Fail` or `Progress` of the stopped job finds nothing (job-id check), so a stopped summary is never stored or shown. Cancelling the source cancels the job's token, which every service call receives, so no new call starts and one in flight is aborted; a plan run's token is linked to the source, so Stop reaches plan runs too.
- `DELETE .../summary` calls `Stop` and always answers `204` (idempotent), for running, finished and failed summaries.
- Client: new `Stop` message (only while `Running`) sends the DELETE and shows `Dismissing Running` ("Stopping…"); she cannot ask until it answers; a failed DELETE asks the server what is there (resumes polling) and says it could not be dismissed. The Running view shows a "Stop" button; the POST-in-flight (`Generating`) view keeps "Dismiss".

## RED

`scripts/check.sh T008` at the RED commit (stub `Stop` that only removes the job): `Tests: 25 total, 6 failed`: the token of a stopped job was not cancelled; `Stop` on a running summary stayed `Running` in the client; the held call was never cancelled (timeout waiting for `Cancelled`); the journey found no Stop button (Chromium, Firefox).

## GREEN

`scripts/check.sh T008`: `Tests: 25 total, 0 failed, 0 skipped`. CI runs the full suite on the PR.

## Sensitivity check (uncommitted, reverted)

Calling the service without the job's token (`Complete = fun messages _ -> complete messages CancellationToken.None` in `runJob`): the contract test `stopping a running multi-chunk summary cancels the held call...`, the Stop journey (both browsers) and F005's `signing out while the fake waits ends the request...` all fail. Reverted.

## Limits of the race tests

- Stop vs finishing is correct by construction (one lock, job removed before cancel, id check in `Complete`, row deleted after). `stopping while the last call is released...` releases the fake and sends DELETE together, so it exercises whichever ordering the scheduler produces on that run; both outcomes are acceptable (AR 3) and the test asserts what holds in both: `404`, no row, one request. It does not force each ordering.
- "No request after Stop returns" is asserted for the held call (cancelled) and for the multi-chunk job (request count unchanged after releasing). A request already past the HTTP client's token check when Stop lands is aborted in flight, not prevented (AR 10).
- Concurrent POST after Stop starts a new job; that is intended (she can ask again).
