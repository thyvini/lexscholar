# T005 sign-out and one-time welcome

- RED: `scripts/check.sh T005` on 501 stubs: 51 tests, 7 failed (sign-out and dismiss answered 501, expected 200/204).
- GREEN: `scripts/check.sh T005 T004`: 81 tests, 0 failed.
- CI full suite: see the PR.

## Decisions

- `signOut` runs under `Vault.Locked`: `SignInSession.Clear()` first (drops the pending attempt, marks a mid-exchange attempt superseded, cancels its token), then revoke (best effort, never throws), then `Delete` always. Lock order is vault, then session.
- A callback mid-exchange has its calls linked to the session token; when the exchange errors because of sign-out or a newer start the outcome is `cancelled`, not `failed`. The final check-and-save already ran under the vault lock and tests `Superseded`, so an attempt that finished its exchange just before sign-out is still not saved (it is revoked).
- Welcome: `markShown` creates the marker; sign-out never removes it.

## Sensitivity check

Commenting out `session.Clear()` in `signOut` (uncommitted) made `a sign-in pending at sign-out cannot finish afterwards` and `a callback mid-exchange when she signs out is never saved` fail; restored.

## Limitation

The ChatGPT summary run's own cancellation on sign-out (research section 11) arrives with T008.
