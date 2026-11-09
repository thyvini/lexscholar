# Research: Research Workspace and PDF Library

**Feature**: F001 — Research Workspace and PDF Library  
**Codecks card**: [$12s](https://thyago-cunha-dev.codecks.io/card/12s)  
**Research date**: 2026-11-04

**Architecture decision adopted**: 2026-11-08 — [functional-first F# architecture](proposals/functional-first-architecture.md).

**Scope simplifications adopted**: 2026-11-09 — [serialized import commit](#serialized-import-commit), [import staging and startup cleanup](#import-staging-and-startup-cleanup), [document availability on open](#document-availability-on-open), and [validator and test placement](#validator-and-test-placement).

## Decisions

### Functional-first F# architecture

- **Decision**: Adopt functional-first, pragmatic F# within the existing five application projects. Prefer immutable records, discriminated unions, modules, pure domain functions, and explicit dependencies. Keep domain decisions independent of framework and infrastructure concerns, and compose application workflows around clear effect boundaries. See the [implementation guidance in the plan](plan.md#functional-first-f-design).
- **Capability boundaries**: Storage and document-content "interfaces" mean narrow contracts, not a requirement for CLR interfaces. Use ordinary functions, records of functions, interfaces, or classes according to the problem. Functions and small function records suit explicit F# workflow dependencies; interfaces can group related or generic operations, and classes can encapsulate state and resource lifetimes. Do not introduce a service class/interface pair or an all-purpose dependency record by default.
- **Pragmatism and interoperability**: ASP.NET Core, EF Core, streams, and resource management naturally use object-oriented APIs. Use them directly where beneficial, encapsulate mutation, and keep ownership and disposal clear. Computation expressions can simplify effectful composition. Functional purity is a design aid, not a dogmatic constraint; framework DI is acceptable when helpful, but application dependencies should remain explicit rather than obtained through service locators.
- **Rationale**: Immutable models and pure rules make domain decisions understandable and independently testable. Explicit dependencies and composed workflows expose where effects occur and allow focused testing. Choosing the simplest idiomatic representation avoids unnecessary abstractions, generic frameworks, additional layers, and excessive indirection.
- **Alternatives and trade-offs**: A uniform service-class/interface/container model can obscure simple workflows and introduce indirection without a requirement. Conversely, requiring every capability to be a function record or insisting on purity everywhere can complicate .NET interoperability and resource ownership. Select the representation that best fits the operation; do not wrap stable pure functions or framework APIs without a concrete benefit.
- **Scope and provenance**: Approved by the project owner on 2026-11-08 and incorporated from the [separate proposal](proposals/functional-first-architecture.md), with an explicit clarification that functions, interfaces, and classes are all valid tools. This changes implementation guidance only, not the Constitution, product requirements, data model, HTTP contract, project count, or task completion statuses.

#### Architectural references

These sources inform the decision; the summaries below are not quotations. The choice of architecture is LexScholar's, not a claim that the authors or Microsoft mandate one architecture.

- Scott Wlaschin, [*Domain Modeling Made Functional: Tackle Software Complexity with Domain-Driven Design and F#*](https://pragprog.com/titles/swdddf/domain-modeling-made-functional/) (Pragmatic Bookshelf, 2018). Reference for domain modeling with types, workflows composed from functions, explicit dependencies, and persistence at the edges. LexScholar applies these ideas without adopting every pattern or adding bounded contexts or architectural layers beyond its approved scope.
- Kit Eason, [*Stylish F#: Crafting Elegant Functional Code for .NET and .NET Core*](https://www.apress.com/9781484239995) (Apress, 2018; [publisher's companion code](https://github.com/Apress/stylish-f)). Reference for idiomatic, readable F# and thoughtful type and function design; used as design guidance, not as a prescription for one application architecture.
- Microsoft, [F# style guide](https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/) and [F# component design guidelines](https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/component-design-guidelines). Guidance on composability, interoperability, encapsulated mutation, and choosing modules, functions, interfaces, and classes appropriately. In particular, the component guidance includes interfaces for grouping related operations; it should not be presented as endorsing function records universally.
- Scott Wlaschin, F# for Fun and Profit: [Six approaches to dependency injection](https://fsharpforfunandprofit.com/posts/dependencies/) and [Dependency injection using parameters](https://fsharpforfunandprofit.com/posts/dependencies-2/) (2020). Discussion of separating decisions from I/O and passing dependencies explicitly, including both interfaces and records of functions, with trade-offs rather than one compulsory technique.

### Runtime, UI, hosting, and support

- **Decision**: .NET 10 LTS / ASP.NET Core 10 with Bolero 0.25.x WebAssembly and Elmish, served by one loopback ASP.NET Core process on Linux. F# 10 is the primary language. Official initial support is Linux with Firefox and Chromium. Multiple tabs use the same process and same-origin endpoints.
- **Rationale**: .NET 10 is active LTS through November 2028. Bolero 0.25 adds .NET 10 support and recommends it. This meets the integrated local deployment without cloud services.
- **Alternatives considered**: .NET 8 reaches end of support on November 10, 2026; .NET 11 is still RC on the research date. A separate frontend/API deployment adds complexity without a feature need. Server-side Bolero interactivity adds a persistent circuit without a benefit for these views.
- **Sources**: [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core); [Bolero 0.25 release notes](https://fsbolero.io/blog/20260814-bolero-0-25-released); [Bolero getting started](https://fsbolero.io/).

### Metadata and identifiers

- **Decision**: EF Core 10 with Microsoft's SQLite provider and SQLite 3.46.1 or newer. All Workspace, Project, and Document IDs are UUIDs represented as canonical lowercase/hyphenated text in F# and SQLite and as UUID strings in HTTP. Persist the last active Project UUID on Workspace and restore it on startup.
- **Rationale**: A UUID end-to-end avoids integer/database versus UUID/API mismatch, permits IDs before persistence, and avoids a separate public-ID mapping. The single-user workspace has no need for compact integer keys.
- **Alternatives considered**: Integer keys are smaller but would expose a different identity model or require another mapping layer. Handwritten SQL is viable but adds mapping/migration code.
- **SQLite trade-offs**: Use simple relational mappings, UTC timestamps, and tested migrations; SQLite has limitations for schemas, sequences, some types, and migration operations.
- **Sources**: [EF Core SQLite provider](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/); [SQLite provider limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations); [EF Core provider compatibility](https://learn.microsoft.com/en-us/ef/core/providers/).

### PDF validation and limits

- **Decision**: Treat filename and MIME type as hints. Require `%PDF-` signature and parse using stable PdfPig 0.1.16; reject encrypted/password-protected files. Enforce configurable defaults of 100,000,000 bytes (100 MB) and 2,000 pages. Count bytes while streaming and obtain page count during parsing. Do not publish a document until it passes both limits and validation. Malformed, zero, or negative configured limits fail startup. Exceeding either configured limit produces a clear non-mutating rejection.
- **Rationale**: Signature alone does not establish readability. PdfPig reads document structure and supports encrypted-document detection. Limits bound disk and parsing resources at import time; changing limits never invalidates or prevents reading a previously accepted document. Each new import request, including re-imports, uses current limits.
- **Alternatives considered**: MIME-only validation is unreliable; relying on the browser detects errors too late and varies by browser. No additional rendering library is needed.
- **Sources**: [PdfPig repository](https://github.com/UglyToad/PdfPig); [PdfPig parsing options](https://github.com/UglyToad/PdfPig/wiki/PdfDocument); [PdfPig package versions](https://www.nuget.org/packages/PdfPig); [ASP.NET Core upload security and size limits](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads).

### Exact identity and local files

- **Decision**: Stream uploads into request-owned private staging while computing SHA-256. Use the digest only to find duplicate candidates, then compare bytes exactly. Use generated opaque storage keys outside the web root; never use client filenames or paths for storage. Concurrency and crash handling are covered by the [serialized import commit](#serialized-import-commit) and [import staging and startup cleanup](#import-staging-and-startup-cleanup) decisions.
- **Rationale**: Byte comparison preserves exact identity even in a digest collision; generated names and a private root prevent path traversal and direct static exposure.
- **Alternatives considered**: Hash-only identity is not exact-byte semantics. Filename identity contradicts the spec. PDF bytes in SQLite contradict the confirmed filesystem decision.
- **Sources**: [ASP.NET Core file upload security guidance](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads); [ASP.NET Core upload tutorial](https://learn.microsoft.com/en-us/aspnet/core/tutorials/razor-pages/uploading-files).

### Serialized import commit

- **Decision**: Stream and validate each upload independently, then commit imports one at a time through a single in-process critical section (for example, a `SemaphoreSlim(1)`). Inside it, find SHA-256 candidates, compare bytes exactly, and either reuse the existing document or promote the staged file and insert its row. Streaming and PdfPig validation stay outside the section, so one slow upload does not block others. No contention retries are used. Keep the byte comparison and test it directly on distinct inputs instead of forcing a hash collision through the pipeline.
- **Rationale**: The OS-held data-directory lock already guarantees a single process, so an in-process section makes concurrent identical imports correct by construction: the first commit inserts the document, and later ones find it and reuse it. Tests reduce to starting several identical imports together and checking one document, one stored original, and empty staging.
- **Alternatives and trade-offs**: Rejected: uncoordinated concurrent commits with unique-violation handling and safe internal contention retries, verified by barrier-synchronized races, repeated races, and a forced hash-candidate collision through a test-only hash seam. That design costs retry logic, a hash seam used only by tests, and slow, potentially flaky race tests, for a single-user, single-process application. Serialization limits import commits to one at a time, which is acceptable for one researcher.
- **Spec coverage**: FR-005 is still met: all concurrent valid imports of identical bytes succeed and return the same shared document, with original bytes and associations preserved; different bytes are never treated as identical.
- **Scope and provenance**: Approved by the project owner on 2026-11-09 as part of the US2 scope review. Changes plan, research, data-model, tasks (T021, T029), quickstart and HTTP-contract wording only; spec.md and the Constitution are unchanged.

### Import staging and startup cleanup

- **Decision**: No SQLite import journal. Stream each upload to a request-owned staging file with a generated name in the private data directory. After validation, inside the serialized commit, flush the staged file to disk, atomically rename it without overwriting to its generated storage key on the same filesystem, then insert the document row. Store originals read-only and never overwrite them. A rejected or failed import deletes its own staging file. At startup, under the exclusive data-directory lock and before serving, delete staging contents and move stored originals that no document row references to `quarantine/`, reporting them; never delete referenced data.
- **Rationale**: Renaming before inserting means a crash can leave only a staging file or an unreferenced original, never a row without its file, so neither state is visible to the researcher. Flushing before the rename prevents a committed row from pointing at bytes that a power loss discarded. Startup cleanup handles both crash leftovers without recording import state, and quarantining instead of deleting keeps the "never delete what cannot be explained" safeguard at the cost of one rename. Tests arrange post-crash states directly, without fault-injection hooks.
- **Alternatives and trade-offs**: Rejected: a `PendingImport` SQLite journal with `Uploading`, `Validated` and `Promoted` states, written before streaming and updated at each step, with startup recovery that re-verifies hash, length and PDF readability to finish or discard interrupted imports, verified by fault injection at the journal, staging, promotion and commit boundaries. It adds a table, a recovery state machine, and test-only failure hooks. Its recovery could also finish an import whose request had already failed, so a document would appear that the researcher saw fail. Without the journal, an interrupted import is simply discarded and the researcher re-imports.
- **Spec coverage**: FR-012 is still met: originals are never overwritten or altered, and failed imports produce a clear failure message without changing library data. SC-007 is still met: each tested import failure leaves the original PDF and unrelated library data unchanged.
- **Scope and provenance**: Approved by the project owner on 2026-11-09. Removes PendingImport from the data model and changes plan, research, data-model, tasks (T022, T025, T026, T028), quickstart and HTTP-contract wording; spec.md and the Constitution are unchanged. The `PendingImport` type and journal-based staging signatures in `src/LexScholar.Application/Contracts.fs` and `Ports.fs` are removed or revised with the import implementation.

### Process exclusivity and tabs

- **Decision**: Acquire an OS-held exclusive lock for the data directory before migrations or request handling. A second process using the same directory fails with a clear startup error. Multiple browser tabs use the already-running process.
- **Rationale**: SQLite and the document store are for one local researcher. Cross-process writes would break the serialized import commit and startup cleanup, and complicate active selection, without a feature need.
- **Alternative considered**: SQLite locking alone does not coordinate filesystem operations or application-level import recovery.

### Document availability on open

- **Decision**: Do not rehash stored originals when they are opened. Originals are written once, never overwritten, and stored read-only. On each content request, open the original read-only and serve it only if it exists, is readable, and its length equals the recorded byte length; otherwise return HTTP 503 `document_unavailable`, preserving metadata and associations. Keep the stored SHA-256 for duplicate detection and possible future library verification or export features.
- **Rationale**: The spec requires a clear error for unavailable or unreadable PDFs and preservation of records (FR-017); it does not require detecting changed bytes. Read-only, never-overwritten originals provide the protection against unintended alteration expected by Constitution Principle II. Browser PDF viewers fetch large documents through many range requests, and on Linux .NET file locks are advisory, so unchanged bytes cannot be proven between requests: rehashing would mean hashing up to 100 MB per range request.
- **Alternatives and trade-offs**: Rejected: verifying SHA-256 on every new open, binding verification and delivery to the same handle, preventing changes during delivery, and reusing verification for range requests only with proof of unchanged bytes, tested with same-size replacement with restored modification time and replacement during verification. Also rejected: hashing only on full, non-range requests. Trade-off: same-length changes made outside LexScholar are not detected.
- **Spec coverage**: FR-017 is still met: missing, inaccessible, unreadable, or wrong-length originals produce a clear error, and the record and associations are never deleted or changed. SC-007 is still met for opening failures: each tested failure returns an understandable error and leaves data unchanged.
- **Scope and provenance**: Approved by the project owner on 2026-11-09. Changes plan, research, data-model, tasks (T034, T035, T037), quickstart and HTTP-contract wording; spec.md and the Constitution are unchanged. The verification wording in the `OpenVerifiedContent` and `VerifiedContent` comments in `Ports.fs` and `Contracts.fs` is revised with T037.

### Validator and test placement

- **Decision**: The PdfPig-based validator reads a caller-owned seekable stream and does not depend on the document store, so it can be implemented and tested before staging exists. Its tests live in `tests/LexScholar.Infrastructure.Tests/PdfValidationTests.fs`, next to the infrastructure they exercise, and use the real fixtures. Non-mutating rejection and re-imports under current limits are tested where library data can change: at the import level (T021).
- **Rationale**: The validator is infrastructure (PdfPig), and Application.Tests references only the Application project. The validator only reads its input, so a non-mutation check at that level would be trivially true.
- **Alternatives and trade-offs**: Rejected: tests in `tests/LexScholar.Application.Tests/`, which would need a new reference from Application.Tests to Infrastructure; making the validator depend on the document store (T026), which would delay validation work without a need.
- **Scope and provenance**: Approved by the project owner on 2026-11-09. Changes tasks T020, T021 and T027 only.

### Document delivery and native viewer boundary

- **Decision**: Serve stored originals by UUID after the [availability check](#document-availability-on-open), with content type, length, inline disposition, and byte-range support. An unknown UUID returns 404 `document_not_found`; a missing, unreadable, or wrong-length original returns 503 `document_unavailable`.
- **LexScholar guarantees**: UUID lookup, read-only originals that are never overwritten, availability checks, content type/length, inline delivery, byte ranges, and consistent 404/503 errors.
- **Browser behavior**: Rendering, native viewer controls, page labels, zoom, and page navigation are owned by Firefox/Chromium. They are officially supported on Linux; their UI is not guaranteed to be identical. Test API delivery separately from native viewer usability.
- **Sources**: [ASP.NET Core minimal API range responses](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0); [ASP.NET Core file range processing](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.fileresult.enablerangeprocessing?view=aspnetcore-10.0).

### Automated testing

- **Decision**: xUnit.net v3, real temporary SQLite databases, temporary filesystem roots, in-memory ASP.NET Core integration tests, and browser end-to-end tests on Linux Firefox and Chromium. Cover UUID round trips, exact-byte deduplication, import limit boundaries, invalid configuration startup, startup cleanup after interrupted imports, process lock/multi-tab behavior, active selection restore, role isolation, unavailable originals, HTTP errors, and range delivery.
- **Browser test boundary**: Automate UI journeys where possible; native viewer internals may limit automation, so record manual reader usability evidence when needed.
- **Browser automation (T019)**: Microsoft.Playwright for .NET drives Linux Chromium and Firefox from the xUnit suites. One API covers both engines; auto-waiting assertions handle WebAssembly boot without sleeps or polling; there are no per-browser driver binaries to keep in version lockstep. Journeys run against the in-process `TestHarness` serving the client, with a caller-owned temporary workspace so a host can be restarted over the same data directory.
  - **Caveat**: Playwright drives its own pinned browser builds (Chrome for Testing / Chrome Headless Shell, and a patched Firefox), not the distribution's `chromium`/`firefox` packages. On distributions Playwright does not officially support (e.g. Arch/CachyOS) it installs Ubuntu fallback builds. Automated evidence records the Playwright build versions; stock-browser behavior is covered by manual evaluation (T046).
  - **Install**: once per user, run `scripts/install-browsers.sh`. It builds the browser test project and calls `Microsoft.Playwright.Program.Main install chromium firefox` through `dotnet fsi`, so Playwright selects its bundled Node driver for the current OS and architecture. This needs no PowerShell (`playwright.ps1`) and no platform-specific path. Browsers are cached under `~/.cache/ms-playwright`, outside the repository. Browser tests run in the default suite and fail (not skip) when browsers are missing; exclude them with `--filter-not-trait 'Category=Browser'`.
  - **Alternatives considered**: Selenium WebDriver with geckodriver/chromedriver drives stock distribution browsers but requires matching driver versions and hand-written waits. bUnit renders components without a real browser and cannot verify browser journeys.
  - **Sources**: [Playwright for .NET](https://playwright.dev/dotnet/docs/intro); [Playwright browsers](https://playwright.dev/dotnet/docs/browsers).
- **Sources**: [xUnit v3 getting started](https://xunit.net/docs/getting-started/v3/getting-started); [ASP.NET Core integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests).

## Security and privacy

- Bind to loopback, use same-origin endpoints, reject foreign origins on mutations/antiforgery failures, and do not enable wildcard CORS.
- Store the database, staging files, and originals in a private per-user Linux data directory outside web roots. Show the effective storage location and never disclose absolute paths in API errors.
- Keep storage keys server-generated and display names encoded. No external services or cloud storage are required. No malware scanning, OCR, or content sanitization is added by F001.
- Constitution v2.0.0 permits tracked incremental delivery. Full-library export/deletion is deferred to F002 ([Codecks $12y](https://thyago-cunha-dev.codecks.io/card/12y)); user-facing documentation must disclose the missing controls and F001 design must not unnecessarily block them. Principle II remains outstanding and F001 does not claim full compliance.

## Remaining technical notes

- Configured size/page limits must be positive; malformed, zero, or negative values fail startup. Defaults are 100,000,000 bytes and 2,000 pages.
- Opening a document checks existence, readability, and length; it does not rehash or reparse. Same-length changes made outside LexScholar are not detected.
- Full-library export/deletion and recovery UI for unavailable PDFs are outside F001. Documents with no project associations remain in the library.

## Version notes

As researched 2026-11-04: .NET 10 LTS, Bolero 0.25.x, EF Core 10, SQLite 3.46.1+, PdfPig 0.1.16 stable. Microsoft.Playwright 1.64.1 was pinned for T019 on 2026-11-09. Use latest compatible stable patches at implementation time and pin them. No implementation tasks or code are part of this research artifact.
