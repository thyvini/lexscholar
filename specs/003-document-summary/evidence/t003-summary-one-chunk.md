# T003 evidence: summary of a one-chunk document

- RED (`scripts/check.sh T003`, commit dbb08b0): `Tests: 33 total, 27 failed`. `parsePoints` and `summarize` were stubs and the endpoint answered 501, so tests failed on the asserted points/omitted counts and on `Expected: summary, Actual: not_implemented`-style event assertions.
- GREEN (`scripts/check.sh T001 T002 T003`): `Tests: 67 total, 0 failed, 0 skipped`.
- Covered: `parsePoints` (think blocks, fences, pages 0/negative/non-integer/empty/out-of-set, empty or over-long text, unparseable output); map prompt with `[Page n]` markers for readable pages only; default request body has only `model` and `messages`, `temperature`/`max_tokens` only when set, Bearer key; `summary` event shape with pages covered, unreadable pages, omitted count, host, model, time; `failed` events (`not_enough_text`, `service_failed` for 500/400/302, unparseable output, response over 1 MB) without the service's body.
- Limits: documents are sent in one request whatever their size (chunking is T006); no redirect, proxy or loopback guards (T009); no 400 detail, timeout or retry (T010); nothing is logged in this slice.
- Full suite: CI run on the PR.
