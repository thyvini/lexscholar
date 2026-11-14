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
