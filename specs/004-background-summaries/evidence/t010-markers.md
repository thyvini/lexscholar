# T010 evidence: markers for running, finished and failed summaries

Covers FR-008, SC-008. AR 4 and AR 5 apply.

## Change

- Domain: `Summaries.statusOf` (job state to marker) and `Summaries.rollUp` (any running, else any failed, else any finished).
- `SummaryJobs.Snapshot listStored`: under the registry lock, every job's status plus every stored summary without a job (finished).
- `GET /api/summaries`: that snapshot with `SummaryRepository.listKeys`, as `[{projectId, documentId, status}]`.
- Client: `SummaryStatus.updateStatuses` runs one polling loop per `Watch` number: `Refresh` starts it, each answer continues it after 2 s while any entry runs and ends it otherwise; a newer refresh supersedes the old loop. Refresh at start, on a change of the active project or open document (`followView`), and after a summary POST or DELETE was answered. `App.fs` shows `small.summary-marker` ("Summary running/finished/failed") next to each project document and on each project (roll-up).

## RED

`scripts/check.sh T010` at the RED commit (stubs returning wrong values, endpoint still 501): `Tests: 57 total, 17 failed` on assertions (e.g. `Expected: OK / Actual: NotImplemented`, `Expected: Running / Actual: Failed`).

## GREEN

`scripts/check.sh T010`: `Tests: 57 total, 0 failed, 0 skipped` (Domain, Contract, state and the two journeys on Chromium and Firefox). CI runs the full suite on the PR.

## Limits

Markers lag by up to the 2 s polling interval (plan point 5); a tab with nothing running does not see another tab's changes until its next view change (AR 4).

## Review fix (gpt-review P1)

Removing a document with a finished or failed summary left the project's marker stale when the view did not change. The client now also refreshes the statuses after a removal. Journey test added; RED before the fix: 2 failed (marker still present, Chromium and Firefox); after: `scripts/check.sh T010`: `Tests: 59 total, 0 failed, 0 skipped`.

## Review fix 2 (gpt-review P1)

A failed first fetch after a POST, with no running entry known, stopped the loop and lost the new summary's marker until the next view change. A failed fetch is now tried again up to 3 times in a row (unlimited while a summary is known to run); a success resets the count. State tests replace the old failed-fetch test; the old code returned no effect at the first assertion of the new one. `scripts/check.sh T010`: `Tests: 60 total, 0 failed, 0 skipped`.

## Review fix 3 (gpt-review P1)

A POST whose answer is lost may still have started the job; the client now refreshes the statuses after `Responded` too. Journey test (the route lets the POST reach the server, then aborts it); RED before the fix: 2 failed (marker never appears); after: `scripts/check.sh T010`: `Tests: 62 total, 0 failed, 0 skipped`.
