# T009 evidence: removal from the project stops the job and deletes the stored summary

Risky (storage, concurrency). Covers FR-013. AR 1 and AR 3 apply.

## Environment

- Linux 7.3.3 (CachyOS), .NET SDK 10.0.114 (`global.json`), Release build with `-warnaserror`.
- Contract tests: in-process host (`TestHarness`) with `FakeChatService` (`Hold`, `Waiting`, `Cancelled` handshakes), temporary workspaces. Race test: Application level, no sleeps.

## Change

- `SummaryJobs.RemoveAssociation(key, delete)`: under the registry lock, `Stop key` (job removed, source cancelled, stored row deleted) and then `delete ()` (the association delete), returning its result.
- `Start` and `StartFailed` already check the association inside the same lock, so a POST that passed its early checks and reaches `Start` after the removal gets `NotAssociated` (`not_found`), and starts no job.
- `LibraryEndpoints.map` takes the registry; `DELETE /api/projects/{p}/documents/{d}` goes through `RemoveAssociation`; the FK cascade also deletes the row. `Endpoints.fs` now creates the registry before `LibraryEndpoints.map` (it only needs the data directory).

## RED

`scripts/check.sh T009` at the RED commit (stub `RemoveAssociation` that stops, then deletes, outside the lock; endpoint not wired): `Tests: 60 total, 1 failed`: the removal while the fake holds timed out waiting for `Cancelled`. The Application tests pass on the stub (single-threaded they cannot tell the two apart); the race test is covered by the sensitivity check below. (An earlier first RED run hung because one of my Application tests waited unbounded on a job that never started; fixed in the test.)

## GREEN

`scripts/check.sh T009`: `Tests: 60 total, 0 failed, 0 skipped`. CI runs the full suite on the PR.

## Sensitivity check (uncommitted, reverted)

`RemoveAssociation` without the lock (`this.Stop key; delete ()`): `a Start launched while the association is being deleted gets NotAssociated` failed 20 of 20 runs. With the lock: 0 of 15 failures. The test starts `Start` on another thread inside the `delete` callback and continues once that thread has either finished or blocked on the lock (thread-state handshake, bounded to 30 s), so the lock is what decides the outcome. Without that handshake the same test failed 0 of 20 against the broken code, so the handshake is what makes it sensitive. The test never fails with the correct code.

## Limits

- The race is tested at the Application level; over HTTP a POST cannot be placed between its early checks and `Start` without sleeps.
- Removal vs finishing is correct by construction (same lock, job removed before cancel, id check in `Complete`, row deleted after); AR 3 applies.
- Removal does not cancel a request already past the HTTP client's token check; it is aborted in flight (AR 10).
