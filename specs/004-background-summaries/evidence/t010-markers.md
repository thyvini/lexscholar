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
