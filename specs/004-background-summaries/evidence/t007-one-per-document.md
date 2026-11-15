# T007 evidence: one summary per document in a project; several at once

Risky (concurrency). Covers FR-011, FR-005.

## Environment

- Linux 7.3.3 (CachyOS), .NET SDK 10.0.114 (`global.json`), Release build with `-warnaserror`.
- In-process host (`TestHarness`) with `FakeChatService` (`Hold` handshake) and `FakeOpenAi`; each test uses a temporary workspace.

## Change

`SummaryEndpoints.fs`: before the vault read and destination resolution, a running or finished (stored included) summary for a pair still in the project answers `200` with its state, so no destination, confirmation or sign-in check applies. A failed summary is not returned (asking again replaces it). `Start`'s `AlreadyRunning`/`AlreadyFinished` under the registry lock already answered `200` and remain the guard for a job that appeared after the early check.

`src/LexScholar.Client/Summary.fs` is unchanged: `Accepted` and `parseRemote` already treat `200` and `202` alike (`App.fs` only tests `IsSuccessStatusCode`), so a `200` already shows the returned state.

## RED

`scripts/check.sh T007` at commit 64e3cbd: `Tests: 67 total, 2 failed`, on the assertions: a running summary's repeat POST to a service elsewhere without confirmation got `409` (expected `200`); a finished plan summary's repeat POST after sign-out got `503` (expected `200`). The other new tests pass already (behavior from T002/T006) and are covered by the sensitivity checks below.

## GREEN

`scripts/check.sh T007`: `Tests: 67 total, 0 failed, 0 skipped`. (The trait also selects F005's tests tagged T007.) CI runs the full suite on the PR.

## Existing test changed

`ChatGptContractTests` "sign-out, then sign-in asks for the confirmation again": after the first document's summary finished, its repeat ask now answers `200` first, so the post-sign-out asks use another document (`article-pt.pdf`). Intent unchanged.

## Sensitivity checks (uncommitted, reverted)

- Early check limited to running summaries (finished ignored): `a finished plan summary answers a repeat POST with 200, even signed out or unconfirmed` fails.
- `AlreadyRunning`/`AlreadyFinished` branches removed from `SummaryJobs.StartWith`: `two POSTs racing for one document start one job` fails (the race passes the early check, so only the lock under `Start` prevents a second job).

## Limitations

The racing test sends two concurrent POSTs; the sensitivity check failed it on the run made, but a race can pass by luck, so it is a detector of the lock, not a proof. Browser journeys are not part of this slice.
