# T002 evidence: sign-in rules and credential file

Command: `scripts/check.sh T002` (Domain and Infrastructure tests tagged T002).

- RED (commit 2d71f53, stubs returning placeholders): 31 total, 16 failed on assertions (values, missing file, no exception on a failed write).
- GREEN: 31 total, 0 failed, 0 skipped.
- Full suite: CI run on the PR (link added when green).

## Sensitivity check (risky: permissions)

Uncommitted, the file creation mode was widened to include `OtherRead`: `the file is owner-only and no temporary file is left` failed (1 failed of 31). Reverted.

## Notes and limitations

- `validateIdToken` checks claims only; the RS256 signature check belongs to T003 (`verifyIdToken`), which passes its payload on.
- The authorization endpoint `https://auth.openai.com/api/accounts/authorize` is the `authorization_endpoint` of the discovery document `https://auth.openai.com/.well-known/openid-configuration`, verified by a plain GET on 2026-11-13. My first guess (`/oauth/authorize`) was wrong and is replaced.
- The failed-write test makes the data directory read-only; it would not fail as intended when run as root.

## Review follow-up (Greptile)

- P1 interrupted save leaves a token-bearing `.tmp` file: must fix. `delete` now also removes `chatgpt-signin.json.*.tmp`. Test added; failed before the fix, passes after (33 total, 0 failed). Taking the same lock around cleanup arrives with the lock in T004.
- P2 out-of-range `exp` throws: must fix. Now `Error Malformed`; test covers both bounds.
- CI `build-and-test`: passed on the first head (run 43487999804).

## Review follow-up (GPT, two P1s)

- P1 `read` with no record left a token-bearing `.tmp` file: must fix. `read` now runs the same cleanup (`delete`) when the record is absent. RED: `reading with no record still removes a temporary file...` failed on `Assert.Empty`; GREEN after the fix.
- P1 empty fields and scopes without plan usage accepted: must fix. A record is now malformed (signed out, deleted) when any of `clientId`, `hostId`, `subject`, `account`, `accessToken`, `refreshToken`, `idToken` is empty, `clientId` is `dynamic_agent_client`, `hostId` is not `urn:uuid:...`, or `scopes` is empty or lacks `chatgpt.tokens.use.direct`. Missing or mistyped fields and bad dates already failed parsing. One test case per rule. RED (`scripts/check.sh T002`): 35 total, 2 failed (`accepted: empty clientId`, leftover not removed). GREEN: 35 total, 0 failed, 0 skipped.
