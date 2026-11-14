# T008 evidence: failure states and credential hygiene

## RED and GREEN

- RED (commit 4dfad1d): `scripts/check.sh T008` gave `Tests: 13 total, 7 failed, 0 skipped.` Failures are assertions: expected `usage_limit_reached` / `signed_out` / `no_plan_access`, got `service_failed`; the sign-out test timed out waiting for the run to end; the hygiene flow found her still signed in after a refused token.
- GREEN: `scripts/check.sh T008 T007` gave `Tests: 74 total, 0 failed, 0 skipped.`
- Sensitivity of the hygiene test: logging the access token and the account in `summarizeViaPlan` (uncommitted) failed `no secret reaches a log line, a response or a file in any flow` (`Found: "ACCESSSECRET-B"`); restored. Not sensitivity-checked: the response, header and file scans (same code path, log scan only).

## Design notes

- Run cancellation lives on `Vault` (`BeginRun`/`EndRuns`), so no shared file changed. `prepareSend` registers the run inside its locked step, so a sign-out after `prepareSend` returns cannot be missed. Sign-out, `endSignIn` and a terminal refresh error call `EndRuns` under the lock.
- `endSignIn` deletes the record only when it is still the sign-in the run used (same `HostId`); unit test `ending a sign-in never deletes a newer sign-in...`.
- Failure mapping: usage limit keeps the sign-in; `SignedOut` and `NoPlanAccess` delete the record (destinations show signed out). A failed run only ever writes progress and one `failed` event: the `summary` event is written only on success, and is replaced by `failed signed_out` when the run token was cancelled after the last call. Test: call 1 completes, call 2 is `response.failed` usage limit on `long-40.pdf`: no `summary` event, 2 Responses requests.
- Sign-out while the fake waits (handshake `FakeOpenAi.Waiting`, then `Cancelled`, no sleeps): `failed signed_out`, exactly one Responses request on a 40-page document.
- Terminal refresh error: T007's 503 `chatgpt_signed_out` problem becomes a 200 stream with `failed signed_out` (T007 test updated). **T010 must treat the `chatgpt_signed_out` problem and the `signed_out` event alike.**

## Limitations

- Sign-out still waits for the vault lock while a refresh is in flight (up to the client timeout; accepted).
- The hygiene scan of the data directory skips `.lexscholar.lock` (held open by the host, no content).
- T007 follow-up (d), unknown stream event kinds being `UnusableOutput`, is left to T012.

CI run for the full suite: see the PR.
