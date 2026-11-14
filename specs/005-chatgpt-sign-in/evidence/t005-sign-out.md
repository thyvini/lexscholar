# T005 sign-out and one-time welcome

- RED: `scripts/check.sh T005` on 501 stubs: 51 tests, 7 failed (sign-out and dismiss answered 501, expected 200/204).
- GREEN: `scripts/check.sh T005 T004`: 81 tests, 0 failed.
- CI full suite: see the PR.

## Decisions

- `signOut` runs under `Vault.Locked`: `SignInSession.Clear()` first (drops the pending attempt, marks a mid-exchange attempt superseded, cancels its token), then read the record, `Delete` it, then revoke from memory (best effort, never throws), so a stop during revocation cannot leave a stored but revoked record (see the GPT follow-up). Lock order is vault, then session.
- A callback mid-exchange has its calls linked to the session token; when the exchange errors because of sign-out or a newer start the outcome is `cancelled`, not `failed`. The final check-and-save already ran under the vault lock and tests `Superseded`, so an attempt that finished its exchange just before sign-out is still not saved (it is revoked).
- Welcome: `markShown` creates the marker; sign-out never removes it.

## Sensitivity check

Commenting out `session.Clear()` in `signOut` (uncommitted) made `a sign-in pending at sign-out cannot finish afterwards` and `a callback mid-exchange when she signs out is never saved` fail; restored.

## Limitation

The ChatGPT summary run's own cancellation on sign-out (research section 11) arrives with T008.

## Review follow-up (Greptile P1: cleanup revocation used the cancelled token)

Valid: after a rejected save the callback revoked with the session-linked token, which sign-out had cancelled. Cleanup revocations in `completeSignIn` now use `CancellationToken.None` (`OpenAiAuth.revoke` has its own 5-second limit). The window (exchange and ID-token check finished, vault lock not yet taken) cannot be held open from the contract tests, because every call before it honors the cancellation; a test needs the Application test harness (`ChatGptAccessTests.fs`), outside this slice's files. Covered below by an Application-level test. `scripts/check.sh T005 T004`: 81 tests, 0 failed.

## Review follow-up (GPT, two P1s)

- Issued tokens when verification is cancelled: any path after the exchange that does not save the tokens now revokes them with `CancellationToken.None`. RED: `a sign-in cancelled by sign-out while its ID token is checked still revokes the issued tokens` (no revocation); GREEN after.
- Sign-out order: delete first, then revoke from memory. RED: `sign-out deletes the record before it asks the service to revoke` (record still stored when revoking); GREEN after. `sign-out whose revocation is stopped has already deleted the record` guards the stop case.
- `scripts/check.sh T005 T004`: see the PR for the final count.
