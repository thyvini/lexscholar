# Implementation Plan: Research Workspace and PDF Library

**Branch**: `001-research-workspace` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

**Feature ID**: F001 | **Codecks**: [$12s — F001 Research Workspace and PDF Library](https://thyago-cunha-dev.codecks.io/card/12s)

**Input**: Approved specification from `specs/001-research-workspace/spec.md`

**Architecture decision adopted**: 2026-11-08 — [functional-first F# architecture](proposals/functional-first-architecture.md); product scope unchanged.

## Summary

Deliver the approved single-researcher workspace for projects, a shared PDF library, project-specific document roles and associations, and page reading. Implement as one locally hosted Linux ASP.NET Core deployment with a Bolero WebAssembly UI, SQLite metadata, and original PDF bytes in a private local filesystem directory. Official initial support is Linux with Firefox and Chromium. Use UUID identifiers consistently in F# domain types, SQLite TEXT columns, and HTTP paths/bodies. Keep document lookup and byte streaming behind a storage abstraction so the native browser PDF viewer is only a consumer of the document content endpoint. Identify duplicate imports using SHA-256 as an efficient index, then compare bytes before reusing a record to satisfy the spec's byte-for-byte identity rule. Validate uploads with signature checks and PdfPig parsing before publishing them to the library. Enforce configurable limits defaulting to 100,000,000 bytes and 2,000 pages, and acquire an exclusive process lock per data directory; multiple tabs share the same process and data.

## Technical Context

**Language/Version**: F# 10 on .NET 10 LTS (latest servicing SDK pinned at implementation start)

**Primary Dependencies**: Bolero 0.25.x with Elmish; ASP.NET Core 10 minimal endpoints and static asset hosting; EF Core 10 SQLite provider; PdfPig 0.1.16 stable for structural parsing and encryption detection; xUnit.net v3 for automated tests. Exact patch versions should be pinned centrally and updated with security patches.

**Storage**: SQLite for workspace, projects, documents, associations, and persisted active project; local filesystem for read-only original PDF bytes and request-owned staging files. On each document open, check that the original exists, is readable, and has its recorded length; content is not rehashed. Storage root is configurable, outside the public web root, and private to the current user.

**Testing**: xUnit v3 unit, SQLite-backed repository/integration tests, ASP.NET Core in-memory HTTP contract tests, and browser-level end-to-end tests against temporary filesystem/database roots. Tests start with the first increment.

**Target Platform**: Linux with officially supported Firefox and Chromium; ASP.NET Core binds to loopback by default. Linux data-directory resolution is configurable.

**Project Type**: Single-deployment full-stack web application (Bolero WebAssembly frontend and integrated ASP.NET Core backend).

**Performance Goals**: No latency SLO is stated in the spec. Avoid buffering entire PDFs in memory for viewing; stream filesystem content and support HTTP byte ranges. Hash and validate uploads incrementally, enforcing configurable defaults of 100,000,000 bytes and 2,000 pages. Document opens check existence, readability, and length only, so range requests from browser viewers do not trigger full-file hashing.

**Constraints**: Single researcher; Linux with Firefox and Chromium; local processing only; no required cloud services; one application process per data directory, with multiple browser tabs supported; no user-controlled filesystem paths; no public static exposure of originals; PDF import rejects encrypted/password-protected input; exact byte identity and original bytes preserved; configurable defaults of 100,000,000 bytes and 2,000 pages; malformed, zero, or negative limit configuration fails startup; no project deletion, rename, collaboration, OCR, search, bibliography, or AI scope. Full library export/deletion and unavailable-document recovery UI are deferred.

**Scale/Scope**: One workspace, multiple projects, shared PDF library, per-project roles, import/reuse, association removal, PDF page reading, and persistence. File size/page limits are configurable; defaults are 100,000,000 bytes and 2,000 pages.

Import limits apply to each import request, including re-imports, using the current configuration. Lowering either limit does not invalidate existing library records or restrict reading a previously accepted PDF, including after restart.

## Constitution Check

| Principle | Gate | Plan response |
|---|---|---|
| I. Academic Integrity and Verifiable Evidence | Pass | F001 only provides source management and reading; it makes no generated evidence claims. Page display remains the original PDF content. |
| II. Privacy and User Control | Incremental delivery; obligation outstanding | Constitution v2.0.0 permits tracked, disclosed deferral. F001 preserves unassociated documents and defers full-library export/deletion to F002 ([Codecks $12y](https://thyago-cunha-dev.codecks.io/card/12y)); user-facing documentation must identify unavailable controls and design must not unnecessarily block them. Full compliance is not claimed. |
| III. Human Oversight of Generated Content | Pass | No generated content is in scope. |
| IV. Testability and Reproducibility | Pass | Acceptance criteria are mapped to automated tests below and in `quickstart.md`; test fixtures include readable, corrupt, encrypted, identical, and distinct PDFs. |
| V. Simplicity and Incremental Development | Pass | One deployable app, one relational metadata store, one filesystem document store, and no external services. Narrow capability boundaries separate persistence and document storage/content from domain behavior; these do not require CLR interfaces or additional architectural layers. |
| VI. Requirements and Implementation Decisions | Pass | The approved spec remains the source of behavior; this plan records technology choices and trade-offs without adding product requirements. |

**Pre-design gate**: Pass. Confirmed stack choices map to F001 and no scope additions are proposed.

**Post-design gate**: Constitution v2.0.0 permits the tracked incremental delivery provided its disclosure and future-design conditions are met. F002's full-library export/deletion obligation remains outstanding; F001 does not claim full Principle II compliance.

## Architecture and Project Structure

Single process and deployment: ASP.NET Core serves the Bolero WebAssembly application and same-origin JSON endpoints. F# application workflows own project and document use cases. Persistence and physical-file access remain behind narrow capability boundaries. References to storage or document-content "interfaces" in this plan describe those boundaries, not necessarily CLR interfaces: ordinary functions, records of functions, interfaces, or classes may express the required contract.

Originals are written once by promotion, never overwritten, and stored read-only. On each document open, the server checks that the original exists, is readable, and has its recorded byte length, without rehashing or reparsing. The server guarantees delivery of the stored bytes and consistent missing/unreadable/wrong-length errors; same-length changes made outside LexScholar are not detected (see [research](research.md#document-availability-on-open)). Browser-native viewer controls, page rendering, page labels, and navigation UX are browser behavior; Linux Firefox and Chromium are supported, but their controls need not be identical.

### Functional-first F# design

- Prefer immutable records, discriminated unions, modules, and pure functions for domain state and rules. Keep domain behavior independent of ASP.NET Core, EF Core, physical storage, and other framework concerns; pass required values explicitly rather than reading ambient state.
- Design application workflows by composing small operations with clear boundaries between domain decisions and effects such as database access, file I/O, time, and ID generation. Make dependencies explicit and pass only the capabilities each operation needs. Domain rules can be unit-tested independently; effectful workflows are verified with integration tests.
- Use an ordinary function for a simple operation and a small record of functions when related capabilities need to travel together. Interfaces or classes are also appropriate when they better express a contract, generic operations, encapsulated state, resource ownership, or interoperability. Neither service class/interface pairs nor function records are mandatory for every operation.
- Use F# computation expressions such as `task { ... }` or `async { ... }` where they clarify effect sequencing and resource lifetimes. Keep cancellation and disposal responsibilities explicit. Prefer readable named steps over elaborate generic workflow machinery.
- Use object-oriented interoperability where beneficial, especially for ASP.NET Core hosting, EF Core contexts, streams, and disposable resources such as the test harness. Keep mutable state encapsulated at effectful boundaries rather than exposing it to domain behavior. Framework dependency injection is acceptable where useful for integration; it must not hide application dependencies behind a service locator.
- Prefer idiomatic F# and the simplest implementation that satisfies the requirements. Do not enforce functional purity dogmatically or add unnecessary abstractions, generic frameworks, architectural layers, or excessive indirection. Direct calls to stable pure functions do not need artificial dependency wrappers.

This is LexScholar's contextual design decision, informed by *Domain Modeling Made Functional*, *Stylish F#*, Microsoft F# guidance, and F# for Fun and Profit; it is not a claim that those references prescribe a single mandatory architecture. See [research and references](research.md#functional-first-f-architecture) and the [approved, incorporated proposal](proposals/functional-first-architecture.md).

### Project layout

```text
src/
├── LexScholar.Server/             # ASP.NET Core host, routing, startup, configuration
├── LexScholar.Client/             # Bolero/Elmish UI, project selection, library, reader
├── LexScholar.Domain/             # Workspace, project, document and association types/rules
├── LexScholar.Application/        # Use cases, validation orchestration, storage capabilities
└── LexScholar.Infrastructure/     # EF Core SQLite, migrations, filesystem store, PdfPig adapter

tests/
├── LexScholar.Domain.Tests/       # Naming, role, association and identity rules
├── LexScholar.Application.Tests/  # Import/association use-case behavior with fakes
├── LexScholar.Infrastructure.Tests/ # Real SQLite and temporary filesystem tests
├── LexScholar.Server.Tests/       # HTTP contracts, range responses, safe error behavior
└── LexScholar.Browser.Tests/      # Main user journeys in a real browser

specs/001-research-workspace/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/
    └── http-api.yaml
```

**Structure Decision**: One solution split into a small number of F# projects following host/client, domain/application, and infrastructure boundaries. The document content abstraction is deliberately independent of the browser viewer. Avoid a separate deployment or microservice; no external integration is required.

## Data and Request Flow

1. Acquire a per-data-directory exclusive process lock before opening SQLite or serving requests. Additional tabs attach to that process; a second process using the same data directory fails with an actionable startup message.
2. Stream the upload to a request-owned private staging file with a generated name while counting bytes and calculating SHA-256. Enforce configurable size and page limits (100,000,000 bytes and 2,000 pages by default). Malformed, zero, or negative limit configuration fails startup. Validate `%PDF-`, readability, and encryption with PdfPig. Do not expose a library record or association until validation succeeds.
3. After validation, enter a single in-process commit section (one import commits at a time; the data-directory lock guarantees one process). Find SHA-256 candidates and compare bytes exactly. For identical bytes, reuse the existing UUID document and delete the staged file; every valid simultaneous identical import therefore succeeds and returns the same document without retries. For new bytes, flush the staged file to disk, atomically rename it without overwriting to an opaque final key on the same filesystem, then insert the document row. A rejected import or failure before promotion discards staging and leaves no new library row or stored original. For an insertion error after promotion, propagate the error and retain the promoted read-only bytes: the error alone does not establish whether SQLite committed. An unreferenced original may remain in `originals/` until startup cleanup, which runs under the lock before serving, deletes staging contents and moves unreferenced originals to `quarantine/` with a warning; it never deletes referenced data. If insertion committed before reporting an error, the library row and referenced bytes remain even though the request reported failure (see [T029 post-promotion investigation](evidence/fix-import-insert-failure-investigation.md)).
4. Store the sanitized original filename only as display metadata, never as a path. Project associations hold per-project role. Removing one deletes only that association; a document with no associations remains in the library.
5. On each document open, check that the original exists, is readable, and has its recorded byte length, without rehashing or reparsing. A missing, inaccessible, unreadable, or wrong-length file returns 503 `document_unavailable`, leaving metadata and associations unchanged. The reader requests `/api/documents/{uuid}/content`; the host resolves UUID to a storage key and streams the stored bytes with `application/pdf`, inline disposition, and range support. Unknown UUIDs return 404 `document_not_found`. The path is never derived from request input.

## Test Mapping

See `quickstart.md` for executable validation flows. Automated coverage maps to spec criteria as follows:

| Spec coverage | Automated verification |
|---|---|
| US1; FR-001, FR-002, FR-013; SC-001 | Browser project create/list/select/switch journey; active-project document list and role refresh, empty states and isolation are covered in the US2 browser journey. |
| US2; FR-003–006, FR-010–012, FR-015, FR-017; SC-002–004, SC-007–008 | SC-002 requires at least 99 successful imports from 100 supported valid readable PDFs within limits. Separately test failure-message quality, invalid inputs, limits, collision-safe equality, concurrent identical imports, and final-association retention. |
| US3; FR-007–009; SC-004, SC-006 | Role isolation is automated. Delivery tests verify original bytes, metadata, ranges, and missing/unreadable/wrong-length failures. Native viewer page navigation is evaluated as browser behavior in supported Firefox and Chromium on Linux; do not attribute browser UI behavior to the API. |
| FR-011; SC-005 | Restart host over the same temporary SQLite and file roots and verify projects, documents, associations, and active-project selection persist across repeated restart cycles. |
| Constitution I/II/IV; FR-014 | Tests assert original bytes are served unchanged, storage path cannot be supplied by client, and no external service is invoked; document local storage path in user-facing settings/help. |

## Risks and Trade-offs

- **Filesystem and SQLite are not one transaction.** Flush and atomically rename the staged file into place before inserting its row, so a crash normally leaves only a staging file or an unreferenced original. Without a directory sync (deliberately not added), a power loss can rarely undo the rename and leave a row pointing to a missing original, which is handled as 503 `document_unavailable` (FR-017). Startup cleanup deletes staging contents and quarantines unreferenced originals; it never deletes referenced data. Keep originals read-only and never overwrite them. Tests arrange these post-crash states directly.
- **A PDF signature is not proof of a valid or safe PDF.** Parse with PdfPig and reject encrypted input. Parsing is still attack surface; keep the library patched and enforce configurable 100,000,000-byte/2,000-page defaults. F001 does not require malware scanning or content sanitization.
- **Hash-only deduplication is not the product identity rule.** SHA-256 narrows candidates, while byte comparison guarantees exact identity even in a theoretical collision.
- **Native PDF viewers vary by browser.** LexScholar controls endpoint availability, delivery of the stored bytes, headers, availability checks, and range semantics, but not viewer UI, rendering, navigation controls, or page labels. Official browser support is Firefox and Chromium on Linux.
- **Loopback hosting is a security boundary, not authentication.** Default to loopback binding, same-origin requests, CSRF/origin protection for mutations, opaque IDs, and no CORS wildcard. Do not accept arbitrary Host or forwarded headers when running locally.
- **SQLite migration capabilities are narrower than server databases.** Keep schema simple and test migrations against the real SQLite provider. No distributed locking or multi-user writes are needed for this feature.
- **Single-instance lock failure or stale lock files.** Use an OS-held exclusive lock (released on process exit), not lock-file presence alone. Test second-process rejection and multi-tab operation.
- **Linux local storage location.** Resolve a per-user application data directory, allow an explicit operator override, and show the effective location. Backup and whole-library export UX are not specified by F001.

## Unresolved Technical Questions for Review

1. **Browser behavior boundary**: Firefox and Chromium on Linux are official targets. Native viewer controls/navigation remain browser behavior; LexScholar guarantees stored-original availability checks and delivery, not identical viewer UI.
2. **Constitution Principle II obligation**: Full-library export/deletion is deferred to F002 ([Codecks $12y](https://thyago-cunha-dev.codecks.io/card/12y)) under v2.0.0 incremental-delivery governance and remains outstanding; F001 does not claim full compliance.

No implementation tasks or application code are generated by this plan.

## Complexity Tracking

The process lock, serialized import commit, and startup cleanup address consistency and preservation needs; no service/deployment tier is added. An import journal and rehashing on every open were removed as over-specified for a single-process local app (see [research](research.md#serialized-import-commit)). The outstanding Principle II obligation is recorded above, not treated as an exception or as satisfied.
