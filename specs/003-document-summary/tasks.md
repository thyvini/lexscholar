# Tasks: F003 — Document Summary with Page References

**Feature**: F003 | **Inputs**: [spec](spec.md), [plan](plan.md), [research](research.md), [contract](contracts/summary-api.yaml), [quickstart](quickstart.md)

Each task is one vertical slice and one PR: tests first (meaningful RED, then GREEN), tagged `Trait("Task", "Txxx")`, run with `scripts/check.sh Txxx`. Design decisions are in `research.md` (§n below); don't re-decide them. **Risky** slices (AGENTS.md: storage, startup, host security, concurrency) also get the GPT review. `[P]`: file list overlaps no slice that can run at the same time.

## Phase 1: Setup

- [x] T001 Shared pieces. **Files**: `src/LexScholar.Server/Configuration.fs` (optional `LexScholar:Summary` section: `Endpoint` absolute http/https without user info, query or fragment; `Model` required with `Endpoint`; `ApiKey`; `MaxChunkCharacters` ≥ 4,000, default 12,000; `Temperature` 0–2; `MaxOutputTokens` > 0; §1), `src/LexScholar.Server/Program.fs` (startup error texts; outgoing handler taken from DI when registered), `src/LexScholar.Domain/Summaries.fs` (types from plan Design, no rules yet), `src/LexScholar.Application/Summarize.fs` (capability types, failure union), `src/LexScholar.Server/SummaryEndpoints.fs` (route from the contract; with no service configured answers `503 summary_service_not_configured`, otherwise `501` stub), `src/LexScholar.Server/Endpoints.fs`, the three `.fsproj` files, `tests/LexScholar.Server.Tests/FakeChatService.fs` and `TestHarness.fs` (`summaryHandler` option; §7), `scripts/make-summary-fixtures.fsx`, `tests/Fixtures/summary/*.pdf` and `tests/Fixtures/README.md` (reference set, sources, licences, SHA-256; §7), `docs/agents/codebase.md`. **Tests**: `tests/LexScholar.Server.Tests/ConfigurationTests.fs` (defaults, valid values, each invalid value fails startup, API key never in the error text); `SummaryContractTests.fs` (not configured → 503). Covers FR-010. **Risky** (startup). Evidence: [t001-shared-pieces.md](evidence/t001-shared-pieces.md).

## Phase 2: User Story 1 — Summarize the open document and check each point (P1)

**Independent test**: with the fake service configured, request a summary of a reference document; every shown point cites readable pages, and a citation shows that page.

- [x] T002 [US1] Local checks before anything is sent (§3). **Files**: `src/LexScholar.Domain/Summaries.fs` (`readability`), `src/LexScholar.Infrastructure/PdfText.fs` (verified open, page count before extraction, `ContentOrderTextExtractor`), `src/LexScholar.Server/SummaryEndpoints.fs` (404 `not_found`/`document_not_found`, 503 `document_unavailable`, 422 `too_many_pages`/`no_text`/`not_enough_text`). **Tests**: `tests/LexScholar.Domain.Tests/SummaryRulesTests.fs`, `tests/LexScholar.Infrastructure.Tests/PdfTextTests.fs` (page numbers by position, unreadable pages of `partial.pdf`), `tests/LexScholar.Server.Tests/SummaryContractTests.fs` (each refusal, fake records zero requests). Covers FR-001, FR-007, FR-011. Evidence: [t002-local-checks.md](evidence/t002-local-checks.md).
- [x] T003 [US1] Summary of a one-chunk document from a local service (§1, §5). **Files**: `src/LexScholar.Domain/Summaries.fs` (`parsePoints` with an allowed-page set), `src/LexScholar.Infrastructure/ChatCompletions.fs` (body only `model` and `messages`, optional `temperature`/`max_tokens`, Bearer key, 1 MB response cap), `src/LexScholar.Application/Summarize.fs` (map prompt, validation), `src/LexScholar.Server/SummaryEndpoints.fs` (NDJSON `summary` event with pages covered, unreadable pages, omitted count, time, host, model). **Tests**: Domain `parsePoints` cases (think blocks, fences, invalid pages, empty text, out-of-set page), `tests/LexScholar.Application.Tests/SummarizeTests.fs`, contract test (default request body, event shape). Covers FR-002, FR-003, FR-004, FR-008, SC-001 (rules). **Risky** (outgoing calls, untrusted input). Evidence: [t003](evidence/t003-summary-one-chunk.md).
- [x] T004 [US1] Researcher requests, sees and dismisses a summary. **Files**: `src/LexScholar.Client/Summary.fs` (model, `Busy` guard, request numbers), `src/LexScholar.Client/App.fs` (streaming fetch, NDJSON parsing, panel labelled as generated with its context, plain-text points, dismiss, not-configured message), client `.fsproj`. **Tests**: `tests/LexScholar.Browser.Tests/SummaryStateTests.fs`, `SummaryJourneyTests.fs` (Chromium, Firefox). Covers FR-001, FR-006, FR-008, FR-009 (dismiss), FR-010 (message), FR-013. Evidence: [t004](evidence/t004-summary-panel.md).
- [x] T005 [US1] Citation shows the cited page (research "Citation navigation"). **Files**: `src/LexScholar.Client/Reader.fs` (`ShowPage`, navigation counter), `src/LexScholar.Client/App.fs` (keyed frame `src` `#page=N`, citation links). **Tests**: `tests/LexScholar.Browser.Tests/ReaderStateTests.fs`, `SummaryJourneyTests.fs` (frame URL ends with `#page=N`, also for a repeated click). Covers FR-005, SC-003 (automated part). Depends on T004. Evidence: [t005-citation-navigation.md](evidence/t005-citation-navigation.md).
- [x] T006 [US1] Long documents: chunks, bounded combine rounds, progress (§4, §5). **Files**: `src/LexScholar.Domain/Summaries.fs` (`chunk`, combine grouping), `src/LexScholar.Application/Summarize.fs` (map, rounds that must shrink, at most 5, combine citations limited to the group's cited pages), `src/LexScholar.Server/SummaryEndpoints.fs` (`progress`, `combining` events), `src/LexScholar.Client/Summary.fs` and `App.fs` (pages processed out of total). **Tests**: Domain chunking (page split keeps its number), Application rounds (non-shrinking round → `service_failed`; combine citing outside its group omitted), contract test with `long-40.pdf`, state test. Covers FR-011, FR-012, FR-004. Evidence: [t006](evidence/t006-long-documents.md).
- [x] T007 [US1] Confirmation for a service elsewhere (§2). **Files**: `src/LexScholar.Domain/Summaries.fs` (`ServiceLocation.classify`), `src/LexScholar.Server/SummaryEndpoints.fs` (`409 confirmation_required` with host and model after local checks), `src/LexScholar.Client/Summary.fs` and `App.fs` (dialog, Send, Cancel). **Tests**: classifier edge cases, contract (elsewhere without confirmation → 409 and zero fake requests; with confirmation → summary; local ignores `confirmSend`), state test (Cancel emits no effect). Covers FR-014, SC-006 (automated part). **Risky** (privacy, host security). Evidence: [t007-confirmation.md](evidence/t007-confirmation.md)
- [x] T008 [US1] Abandon on dismiss, document change or project change (§6). **Files**: `src/LexScholar.Client/Summary.fs` (follows reader, `Cancel` effect), `src/LexScholar.Client/App.fs` (`CancellationTokenSource` per request), `src/LexScholar.Server/SummaryEndpoints.fs` and `src/LexScholar.Application/Summarize.fs` (`RequestAborted` into every service call, no further chunk; log the cancellation with document id only). **Tests**: state tests (late events dropped, second request for same document ignored), contract test (abort → fake sees cancellation, no further request). Covers FR-009. **Risky** (concurrency). Evidence: [t008-abandon.md](evidence/t008-abandon.md).
- [x] T009 [P] [US1] Local-service guards and log hygiene (§1, §2). **Files**: `src/LexScholar.Infrastructure/ChatCompletions.fs` (`AllowAutoRedirect = false`; for local: `UseProxy = false`, loopback-only `ConnectCallback`; logs only host, model, page counts, duration). **Tests**: `tests/LexScholar.Infrastructure.Tests/ChatCompletionsTests.fs` (connect guard refuses non-loopback, redirect → failure, captured logs contain no text, prompt, output or key). Covers SC-006, FR-014. **Risky** (host security). Depends on T003. Evidence: [t009-local-guards.md](evidence/t009-local-guards.md).
- [ ] T010 [US1] Service failures and retry. **Files**: `src/LexScholar.Application/Summarize.fs` and `src/LexScholar.Server/SummaryEndpoints.fs` (`failed` event: unreachable, error status, timeout, unusable output; 400 → `detail` about `Model`, `Temperature`, `MaxOutputTokens`, never the service body; no valid points → `not_enough_text`), `src/LexScholar.Client/Summary.fs` and `App.fs` (message, try again). **Tests**: contract tests per failure, state test for retry. Covers edge cases (service failure), FR-004. Depends on T006, T008.
- [x] T011 [P] [US1] Reference-set checks. **Files**: `tests/LexScholar.Server.Tests/SummaryReferenceSetTests.fs` (new). Every reference document through the endpoint with the fake returning out-of-range and unreadable citations: all shown pages exist and were readable, omitted counts, `no_text` for `scanned.pdf` without a service call; database rows and original hashes unchanged before and after request, confirm, decline and abandon. Covers SC-001, SC-005, FR-013. Sensitivity check required (behaviour exists). Depends on T007, T008. Evidence: [t011-reference-set.md](evidence/t011-reference-set.md).

## Phase 3: Polish

- [ ] T012 Manual validation on the researcher's real setup (quickstart "Manual validation"): SC-002 (9 of 10 points supported), SC-003 in Firefox and Chromium, SC-004, SC-006 with `strace`, SC-007 (40 pages within 2 minutes, recording hardware), FR-002 with `article-pt.pdf`. **Files**: `specs/003-document-summary/evidence/t012-manual-validation.md`.

## Dependencies

T001 → T002 → T003 → {T004, T009} ; T004 → T005 ; T003 + T004 → T006 → T007 → T008 → T010 ; T007 + T008 → T011 ; all → T012.
Parallel: T009 with T004/T005 (Infrastructure only vs client only); T011 with T010 (new test file only). T004–T008 and T010 share `Summary.fs`/`App.fs` or `SummaryEndpoints.fs`, so they run in order.

**MVP**: T001–T005 (a local service, documents of one chunk, citations to pages).

## Coverage

| Requirement | Tasks |
|---|---|
| FR-001 | T002, T004 |
| FR-002 | T003, T012 |
| FR-003 | T003 |
| FR-004 | T003, T006, T010 |
| FR-005 | T005 |
| FR-006 | T004 |
| FR-007 | T002 |
| FR-008 | T003, T004 |
| FR-009 | T004, T008 |
| FR-010 | T001, T004 |
| FR-011 | T002, T006 |
| FR-012 | T006 |
| FR-013 | T004, T011 |
| FR-014 | T007, T009 |
| SC-001 | T003, T011 |
| SC-002 | T012 |
| SC-003 | T005, T012 |
| SC-004 | T012 |
| SC-005 | T011 |
| SC-006 | T007, T009, T012 |
| SC-007 | T012 |
