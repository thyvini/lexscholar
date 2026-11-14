# T003 evidence: sign-in service client

Command: `scripts/check.sh T003` (Infrastructure tests tagged T003, `FakeOpenAi` linked from the Server tests).

- RED (commit acf7af3, stubs returning errors): 59 total, 13 failed on assertions (request fields never sent, no grant, no `Ended`, nothing confirmed, valid ID token refused).
- GREEN: 59 total, 0 failed, 0 skipped.
- Full suite: CI run on the PR (link added when green).

## Sensitivity check (risky: credentials)

Uncommitted, the signature result was ignored (`|| true`): `a tampered payload fails the signature check` failed (1 of 59). Reverted.

## Notes and limitations

- `AuthClient.Exchange` now returns the named record `TokenGrant` (tokens, expiry, scopes).
- Only `https://auth.openai.com` is contacted; the discovered `revocation_endpoint` is used only when it is HTTPS on that host. The handler is created with `AllowAutoRedirect = false`; the unit test checks the flag and that a 302 is an error.
- Other 4xx answers to a refresh (no terminal code) are treated as `Unreachable`, so the record is kept.
- Wiring the real `HttpClient` into `AuthClient` happens in T004.

## Review follow-up (Greptile, two P2)

- P2 a JWKS that is not an object (`null`, `[]`, a string) made `findKey` throw: must fix. It now checks the root is an object first. RED: 3 of the new cases failed with the old code; GREEN: 66 total, 0 failed.
- P2 missing tests for unsafe discovered revocation addresses: must fix. Added (HTTP, foreign host, relative address: nothing is posted).
- P2 missing test for the 5-second revoke limit: overkill. A test would need a real 5 s wait (no sleeps rule) or a new time seam for one constant; the limit is a single `CancelAfter` line.
