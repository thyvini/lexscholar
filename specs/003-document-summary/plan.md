# Implementation Plan: Document Summary with Page References

**Branch**: `docs/f003-plan` | **Date**: 2026-11-12 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-document-summary/spec.md`

## Summary

While a project document is open in the reader, the researcher asks for a summary. The host checks the document locally (availability, at most 100 pages, readable text per page), asks for confirmation when the configured service is not on her machine, then sends page-marked text in chunks to an OpenAI-compatible Chat Completions endpoint of her choice. The model's JSON answer is validated: points whose citations are missing or not readable pages of the document are omitted and counted. Progress and the result stream back to the client as NDJSON; nothing is stored. A citation re-opens the reader frame at `#page=N`. Decisions: [research.md](research.md); endpoint: [contracts/summary-api.yaml](contracts/summary-api.yaml).

## Technical Context

**Language/Version**: F# on .NET 10 (SDK pinned in `global.json`)

**Primary Dependencies**: existing only: ASP.NET Core, Bolero, PdfPig 0.1.16 (`ContentOrderTextExtractor`), `System.Net.Http`, `System.Text.Json`. **No new packages.**

**Storage**: none new. No migration. Settings extend the existing configuration (`LexScholar:Summary:*`).

**Testing**: xUnit v3 in the existing test projects; a fake Chat Completions handler; generated, public-domain reference PDFs in `tests/Fixtures/summary/`; Playwright for the citation journey.

**Target Platform**: Linux, Firefox and Chromium (as F001).

**Project Type**: local web application (ASP.NET Core host + Bolero WebAssembly client).

**Performance Goals**: a 40-page article summarized within 2 minutes on her configured service (SC-007, manual).

**Constraints**: incoming host security unchanged; outgoing calls only to the configured endpoint, no redirects, no proxy for a local service, connection guard to loopback for a local service; no document text, prompt, output or API key in logs or responses; at most 100 pages.

**Scale/Scope**: one researcher, one summary at a time, documents up to 100 pages.

## Constitution Check

*Checked before research and again after design: passes, no exceptions.*

| Principle | How the design meets it |
|---|---|
| I. Integrity, verifiable evidence | Every shown point cites readable pages that were sent; invalid points are omitted and the count shown (FR-004). Page numbers come from page positions, never from model text. |
| II. Privacy and control | Local by default when she configures a local server; anything elsewhere needs a per-summary confirmation naming host and model, enforced by the server (FR-014). Nothing stored, so no new export or deletion obligation; F002 (`$12y`) tracking is unchanged. Data handling documented in the quickstart. |
| III. Human oversight | Summary labelled "Generated summary — check against the cited pages", dismissable, shown beside the reader with one-click citations. |
| IV. Testability, reproducibility | Reference set with no-text, partial and little-text cases; fake service for deterministic tests; each summary shows document, pages covered, unreadable pages, time, service host and model (FR-008). |
| V. Simplicity | One endpoint, one protocol client, no packages, no storage, no background jobs. Chunking is justified by typical local context sizes (research §4). |
| VI. Decisions recorded | research.md. |

## Design

**Domain** (`src/LexScholar.Domain/Summaries.fs`, pure):
- `ServiceLocation = Local | Elsewhere` and `classify : Uri -> ServiceLocation`.
- `PageText = { Number: int; Text: string }`; `readability : PageText list -> Result<ReadablePages, NoTextReason>` (readable and unreadable pages, `no_text`, `not_enough_text`).
- `chunk : int -> PageText list -> Chunk list` (consecutive whole pages, long pages split, page numbers kept).
- `parsePoints : allowedPages: Set<int> -> string -> Result<ValidatedPoints, Unparseable>` with `ValidatedPoints = { Points: SummaryPoint list; Omitted: int }`, `SummaryPoint = { Text: string; Pages: int list }`.

**Application** (`src/LexScholar.Application/Summarize.fs`): the workflow over capability functions: `ExtractPages` (document id → pages or unavailable/too many pages), `Complete` (messages → cancellation → `Task<Result<string, ServiceError>>`), `Report` (progress event). It builds prompts (map, combine), runs chunks in order, validates each answer and returns `DocumentSummary` or a failure union. Pre-send checks return before `Complete` is ever called.

**Infrastructure**: `PdfText.fs` (verified open + PdfPig per-page extraction, page-count check first); `ChatCompletions.fs` (request/response JSON, `HttpClient` with the handler rules from research §1–2).

**Server**: `SummaryConfiguration` in `Configuration.fs` (optional section, validated at startup); `SummaryEndpoints.fs` (route, problem mapping, NDJSON writing, `RequestAborted` linked to the service calls; outgoing handler from DI if registered, else the real one). Registered in `Endpoints.fs`.

**Client**: `Summary.fs` state machine: `Idle | Confirming | Generating (request, processed, total) | Shown | Failed`, request numbers, `Busy` guard, `Cancel` effect; follows the reader so a document or project change abandons the request. `Reader.fs` gains `ShowPage n` (frame `src` `#page=n`, keyed by a counter). `App.fs`: streaming fetch (`SetBrowserResponseStreamingEnabled`), NDJSON parsing, summary panel beside the reader.

No `data-model.md`: nothing is stored; the transient types above and the contract are the whole model.

## Project Structure

```text
specs/003-document-summary/
├── plan.md  research.md  quickstart.md
├── contracts/summary-api.yaml
└── tasks.md                     # next: /speckit-tasks

src/LexScholar.Domain/Summaries.fs                    # new
src/LexScholar.Application/Summarize.fs               # new
src/LexScholar.Infrastructure/{PdfText,ChatCompletions}.fs   # new
src/LexScholar.Server/{Configuration,Endpoints,Program}.fs   # changed
src/LexScholar.Server/SummaryEndpoints.fs             # new
src/LexScholar.Client/Summary.fs                      # new
src/LexScholar.Client/{Reader,App}.fs                 # changed
tests/Fixtures/summary/*.pdf, tests/Fixtures/README.md        # reference set + provenance
scripts/make-summary-fixtures.fsx                     # regenerates the reference set
tests/LexScholar.*.Tests/…Summary…Tests.fs            # per layer
docs/agents/codebase.md                               # new modules and the fake service
```

## Size check

The design fits the budget as about 13 vertical slices; `/speckit-tasks` produces the real list:

1. Setup: settings section and validation, shared types, route and registration, fake service and harness option, reference fixtures and script.
2. Per-page extraction and the local refusals (unavailable, too many pages, no text, not enough text).
3. Summary of a short document through a local service, validated citations, NDJSON result (server).
4. Client: request, generated-summary panel with context (FR-006/008), dismiss.
5. Citation opens the cited page.
6. Chunking and combine for long documents, with progress events and the progress view.
7. Confirmation for a service elsewhere (server 409, client dialog, decline).
8. Not-configured message.
9. Abandon on dismiss and on document or project change (client cancel, server cancellation).
10. Local-service guards (classifier, no proxy, loopback connect guard, no redirects) and log hygiene.
11. Service failures and retry.
12. Reference-set checks SC-001 and SC-005.
13. Manual validation: SC-002, SC-003 in both browsers, SC-004, SC-006 (`strace`), SC-007.

## Complexity Tracking

None.
