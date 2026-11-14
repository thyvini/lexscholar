# T004: sign in, once, in her browser

- RED: `scripts/check.sh T004` on stubs (`completeSignIn` returned `Failed`, sign-in routes answered 501): 13 of 24 failed on assertions (expected `SignedIn`, got `Failed`; expected 200 or 302, got 501; the query string `state=SECRETSTATE&code=SECRETCODE` found in a log line).
- GREEN: `scripts/check.sh T004`: 25 tests, 0 failed (Application workflow tests, contract tests with `FakeOpenAi`, the vault test). CI runs the full suite on the PR.

## What the tests show

- The stored record has every field of data-model.md; `confirmedAt` is absent.
- Start gives an address with state, nonce, challenge, host id and the redirect `http://127.0.0.1:<port>/auth/callback`; a second start while signed in is `409 already_signed_in`; a new start replaces the pending attempt.
- Callback outcomes: `signed_in`; `cancelled` (access_denied, unknown, reused or missing state, no attempt; the fake saw no token exchange for a bad state); `no_plan_access` (one revocation, no file); `failed` (token error, wrong audience, empty code, missing issued client id). No unsuccessful outcome leaves `chatgpt-signin.json`.
- The record survives a host restart.
- The callback's query string is not in any log line: `Program.fs` raises `Microsoft.AspNetCore.Hosting.Diagnostics` to Warning because its request-start line includes the query.

## Decisions and limits

- The issued client id is read from the callback's `client_id` query parameter (one constant, `IssuedClientIdParameter` in `ChatGptEndpoints.fs`). Unverified against the real service: confirm in T012. When it is absent the outcome is `failed`.
- The lock lives in Application only (`Vault`); every caller of `ChatGptCredentials.read/save/delete` goes through it, so the T002 follow-up (a read deleting the temp file of a save in progress) is satisfied without an Infrastructure change.
- Sensitivity check for the vault: removing the wait on the lock made `the vault runs one change at a time` fail; reverted.
- Real `HttpClient`: `OpenAiAuth.createHandler` (no redirects) unless a handler is injected through DI (tests).

## Review follow-up (Greptile)

- P1 (older sign-in overwrites a newer one): the save now checks under the lock that no record exists; a second finisher is revoked and reports `cancelled`. Test: `a sign-in that finishes second does not replace the first and is revoked`.
- P2 (error callback cancels an unrelated attempt), verdict must fix: an error ends only the attempt whose `state` it carries. Test: `an error without the current state leaves the pending attempt alone`.

## Review follow-up (GPT, P1 and P2)

- P1: a new start now supersedes an attempt that is already mid-exchange (`SignInSession.Superseded`, checked under the vault lock before saving); the stale finisher is revoked and reports `cancelled`. RED: `a callback in flight is superseded by a newer start and is never saved` failed (the older attempt was saved); GREEN after the change.
- P2: an error callback gives `cancelled` unless its state matches the live attempt and the error is not `access_denied` (then `failed`). RED: `any error with an unknown, reused or missing state is cancelled` failed; GREEN after the change. `scripts/check.sh T004`: 29 tests, 0 failed.

## Review follow-up 2 (GPT, P1: check and save not atomic with a start)

- `startSignIn` now checks "signed in?" and replaces the pending attempt inside `vault.Locked`, the lock the callback's check-and-save holds, so the two serialize. RED: `a callback that reaches the vault while a start is replacing the attempt is never saved` runs the older callback from the start's clock (inside the window, no sleeps); it was saved before the fix. GREEN after. `scripts/check.sh T004` below.
