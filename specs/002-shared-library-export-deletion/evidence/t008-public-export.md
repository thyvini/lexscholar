# T008 — Public export and Portuguese download feedback

## Environment and scope

- Linux x64, pinned .NET 10 SDK; Playwright's Chromium and Firefox, isolated temporary workspaces and fixture PDFs only.
- T004–T007 are merged prerequisites. Production maps the same complete builder, file lease and private transfer lifecycle; no deletion admission, retry or recovery is activated.
- Browser delivery uses local fetch with the existing antiforgery header and rejects redirects. Complete ZIP receipt/type/length checks precede object-URL download; no archive bytes enter WASM interop. See research R6 for browser resource tradeoffs.

## RED / GREEN

- RED: `scripts/check.sh T008` — 34 tests, 7 assertion failures, zero skips: missing export control/explanations in both browsers, and public empty export returned 404 instead of 409. Log `/tmp/lexscholar-check.8Th5Gx`.
- First GREEN attempt found an F# indexing syntax error in the expanded ZIP inspection test; corrected before executing tests. This is not claimed as behavioral RED.
- Sensitivity: `scripts/check.sh T008` — the public export antiforgery contract failed when protection was deliberately bypassed; protection restored.
- GREEN: `scripts/check.sh T008 T004 T005 T006 T007` — 351 passed, zero skipped.
- CI full suite: pending coordinator PR; this task does not push or open a PR.

## Coverage and limitations

- State tests cover duplicate clicks, navigation during export, explicit receipt versus failure, retry, stray results, and safe problem parsing.
- Both browsers inspect an actual downloaded ZIP containing an unassociated PDF and a PDF shared by two projects with distinct roles and stored summaries, citations and readable generated/unverified notices. Entry allowlisting excludes credential/settings canaries; export performs no browser external requests and preserves library records.
- Both browsers exercise empty-library and real missing-original errors; connection abort and an HTTP 200 body interrupted after its first bytes never offer a download or receipt notice. Public contract checks retain Host/Origin/header-antiforgery protections and confirm deletion admission remains unmapped.
- Server bounded-memory archive construction, private cleanup, storage failures, snapshot consistency and coordinated races are covered by the targeted T004–T007 regression run.
- Browser-owned Blob resources scale with archive size; no large-library browser resource benchmark is claimed. The app cannot verify a user's final browser download/save-to-disk choice, so Portuguese feedback says only that the archive was received and asks the researcher to check the browser download.
- Owner/manual validation in Firefox/Chromium, native PDF citation inspection and final F002 acceptance remain T018; automated download/ZIP inspection is not represented as owner validation. F002 and Principle II are not declared complete.
