# T001 evidence: shared pieces

- RED (`b073f6b`): `scripts/check.sh T001` gave 7 failed of 28; contract tests failed on `Expected: OK / NotImplemented, Actual: NotFound` (destinations and the sign-in routes did not exist). The `FakeOpenAi` self-test passed.
- GREEN (`8c8ff5b`): `scripts/check.sh T001` gave `Tests: 28 total, 0 failed, 0 skipped.` (includes F003's tests that share the `T001` tag).
- Startup: no change to startup order or middleware; the new routes are mapped in `Endpoints.map` after the summary route. A client sending Host `127.0.0.1:<port>` is accepted (test `a client sending Host 127.0.0.1 with the port is accepted`); the test harness already supports the `summaryHandler` seam, so `TestHarness.fs` is unchanged.
- Full suite: CI run on the PR.
