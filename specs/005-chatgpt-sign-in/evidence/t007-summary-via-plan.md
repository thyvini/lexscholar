# T007 evidence: summary through her ChatGPT plan

## RED and GREEN

- RED (commit 905c39c): `scripts/check.sh T007` gave `Tests: 58 total, 15 failed, 0 skipped.` The failures are assertions (confirmation answered 503 instead of 409, the plan was never used, `serviceHost` was `127.0.0.1:9`, and so on).
- GREEN: `scripts/check.sh T007` gave `Tests: 59 total, 0 failed, 0 skipped.` F003 `SummaryContractTests` and `SummaryReferenceSetTests` still pass (54 tests).
- Sensitivity of the save-failure test: making `completeSignIn` rethrow instead of revoking (uncommitted) failed `a sign-in whose record cannot be saved fails and revokes the issued tokens` (no revocation); restored.

## Design notes and deviations

- `accessToken` and `confirm` are one function, `ChatGptAccess.prepareSend`, approved by the lead: confirmation check, refresh and save happen in one `Vault.Locked` block on one record. A sign-out and a new sign-in cannot slip an unconfirmed record between "confirmed" and "send". Confirmation is checked before any network call, and is recorded (in the same save as a refresh) only when the run goes on, so a failed refresh does not record it.
- A refresh holds the vault lock, so sign-out waits for it and then deletes. The refresh also saves only if the record still exists with the same refresh token. Race test: the refresh is held at the token endpoint (handshake when the refresh request arrives, no sleeps), sign-out starts, the refresh is released: she is signed out and no record exists.
- Neither destination available gives `503 summary_service_not_configured` even when `chatgpt` is named (lead's decision). A terminal refresh error before the stream gives `503 chatgpt_signed_out`; T008 owns the `failed` event codes.
- Refresh answers: network failure, 5xx, 4xx without a terminal code and a 401 keep the record and end the run with a `service_failed` event (theory test, three cases).
- `ChatGptEndpoints.map` returns the `Vault`; `Server.fsproj` compiles `ChatGptEndpoints.fs` before `SummaryEndpoints.fs` and `Endpoints.fs` passes the vault along.
- T005 follow-up (d), done: if `store.Save` throws in `completeSignIn` the issued tokens are revoked and the outcome is `failed`.

## Limitations

- Sign-out between `prepareSend` and the send is not cancelled here (T008).
- SummaryContractTests.fs is unchanged: F003's not-configured test still passes as is, and "signed in with nothing configured goes through the plan" is covered in ChatGptContractTests (`confirming sends the summary through the plan...`, which has no configured service).

CI run for the full suite: see the PR.

## Review follow-up (Greptile, two P2)

- P2 local summary waited for the vault (must fix): a request naming `configured` while a configured service exists no longer reads the vault. Test: `a summary naming the configured service does not wait for a refresh in flight` (refresh held at the fake). Sensitivity: with the fix disabled (uncommitted) it fails on the 10 s guard; restored. Accepted limit: a request that names nothing still reads the vault to know whether she is signed in.
- P2 race test cannot prove sign-out reached the vault (accepted risk): the refresh holds the lock from before the token request, so the end state is the same whenever the sign-out request arrives; a comment in the test says so. Observing a wait on the lock would need a hook in `Vault`, not worth it.
