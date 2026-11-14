# T001 shared pieces (risky: startup)

- RED: `scripts/check.sh T001` at b9f4bed: 21 tests, 20 failed. Configuration tests failed on `Expected InvalidSummarySetting` / `Expected a configured summary` (the stub always returned no summary section); contract tests failed on 404 vs the expected 503 and 501.
- GREEN: `scripts/check.sh T001`: `Tests: 21 total, 0 failed, 0 skipped.`
- CI: full suite on the PR (see PR checks).

## Decisions

- A blank `Endpoint` counts as not configured (an empty environment variable is the usual way to unset one). `Model` or `ApiKey` without `Endpoint` also leave the feature off.
- Startup errors name the key and a fixed reason; no value is ever echoed, so the API key (or credentials placed in the endpoint) cannot appear in the error text. The test sets an API key and a URL with a password and asserts neither appears.
- `Temperature` rejects NaN and values outside 0..2; numbers parse with the invariant culture.
- The outgoing handler is registered as an `HttpMessageHandler` singleton by the test harness; the endpoint will resolve it from DI in T003 when it first makes a call. T001 makes no outgoing connection.
- Fixtures are not byte-reproducible (PdfPig writes a fresh document id); hashes in `tests/Fixtures/README.md` describe the committed files.

## Limitations

- The 501 stub is replaced from T002 on.
