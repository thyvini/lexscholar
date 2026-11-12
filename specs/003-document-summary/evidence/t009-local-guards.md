# T009 local-service guards and log hygiene (risky: host security)

- RED: `scripts/check.sh T009` at 751292c (stub handler without guards, logger unused): 37 tests, 4 failed. Guard tests failed on `Expected Error Unreachable` (the plain handler connected to the server), the proxy test on `UseProxy` true for a loopback endpoint, the log test on a missing host in the captured log. Redirect and positive-path tests already held (AllowAutoRedirect was false since T003).
- GREEN: `scripts/check.sh T009`: `Tests: 37 total, 0 failed, 0 skipped.`; `scripts/check.sh T003` (contract tests through the changed endpoint wiring): `Tests: 35 total, 0 failed, 0 skipped.`
- CI: full suite on the PR (see PR checks).
- This slice closes the GPT P1 from the T003 review (proxies allowed, addresses not restricted to loopback).

## Decisions and limits

- Greptile P1 (IPv4-only service with `::1` listed first): fixed; the connect tries each checked loopback address in order (test added, RED at the follow-up commit then GREEN, 38 tests).
- Local means host `localhost` or a loopback IP literal, decided in `ChatCompletions.fs` for now; T007 replaces it with `ServiceLocation.classify`.
- For a local endpoint the connect callback resolves the name and refuses it unless every address is loopback; the test injects the resolver to simulate `localhost` resolving elsewhere.
- A non-local endpoint keeps the default proxy behaviour and no connect guard (confirmation is T007).
- Logs: host, model, message count, outcome and duration only. Page counts are not visible in Infrastructure; T010 may add them.
- Body-read timeout after headers is T010.
