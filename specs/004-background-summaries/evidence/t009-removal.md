# T009 evidence: removal from the project stops the job and deletes the stored summary

Risky (storage, concurrency). Covers FR-013. AR 1 and AR 3 apply.

## Environment

- Linux 7.3.3 (CachyOS), .NET SDK 10.0.114 (`global.json`), Release build with `-warnaserror`.
- Contract tests: in-process host (`TestHarness`) with `FakeChatService` (`Hold`, `Waiting`, `Cancelled` handshakes), temporary workspaces. Race test: Application level, no sleeps.

## Change

- `SummaryJobs.RemoveAssociation(key, delete)`: under the registry lock, the job is removed and its source cancelled, then `delete ()` (the association delete) runs and its result is returned. The stored row is not deleted separately: it goes with the association through the FK cascade, in one database operation, so a failed or interrupted removal never loses the summary of a document that stays in the project (gpt-review P0). `Stop` (dismiss) still deletes the row itself.
- `Start` and `StartFailed` already check the association inside the same lock, so a POST that passed its early checks and reaches `Start` after the removal gets `NotAssociated` (`not_found`), and starts no job.
- `LibraryEndpoints.map` takes the registry; `DELETE /api/projects/{p}/documents/{d}` goes through `RemoveAssociation`; the FK cascade also deletes the row. `Endpoints.fs` now creates the registry before `LibraryEndpoints.map` (it only needs the data directory).

## RED

`scripts/check.sh T009` at the RED commit (stub `RemoveAssociation` that stops, then deletes, outside the lock; endpoint not wired): `Tests: 60 total, 1 failed`: the removal while the fake holds timed out waiting for `Cancelled`. The Application tests pass on the stub (single-threaded they cannot tell the two apart); the race test is covered by the sensitivity check below. (An earlier first RED run hung because one of my Application tests waited unbounded on a job that never started; fixed in the test.)

## GREEN

`scripts/check.sh T009`: `Tests: 60 total, 0 failed, 0 skipped`. CI runs the full suite on the PR.

## Sensitivity check (uncommitted, reverted)

`RemoveAssociation` without the lock (`dropJob key; delete ()`): `a Start launched while the association is being deleted gets NotAssociated` failed 10 of 10 runs; with the lock it never fails. The test starts `Start` on another thread inside the `delete` callback and waits up to 200 ms for an early answer: only a `Start` that did not wait for the lock can answer in that time, so with the lock the wait just times out and the test goes on. This is a bounded timed wait (no polling of thread state); it cannot make the correct code fail, and a broken lock is detected whenever the racing `Start` is scheduled within 200 ms, which it was in every run. (A version without any wait detected the broken lock 0 of 20 times.) `a failed association delete leaves the stored summary alone` covers the P0 ordering.

## Limits

- The race is tested at the Application level; over HTTP a POST cannot be placed between its early checks and `Start` without sleeps.
- Removal vs finishing is correct by construction (same lock, job removed before cancel, id check in `Complete`, row deleted after); AR 3 applies.
- Removal does not cancel a request already past the HTTP client's token check; it is aborted in flight (AR 10).
