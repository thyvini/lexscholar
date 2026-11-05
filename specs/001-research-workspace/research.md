# Research: Research Workspace and PDF Library

**Feature**: F001 — Research Workspace and PDF Library  
**Codecks card**: [$12s](https://thyago-cunha-dev.codecks.io/card/12s)  
**Research date**: 2026-11-04

## Decisions

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

### Exact identity, local files, and recovery

- **Decision**: Stream uploads into request-owned private staging while computing SHA-256. Use the digest only to find duplicate candidates, then compare bytes exactly. Every valid simultaneous identical import succeeds and returns the same document; safe internal contention retries are allowed. Preserve one record, original bytes and associations, and clean only each operation's owned staging/journal artifacts. Use generated opaque storage keys outside the web root; never use client filenames or paths for storage. A SQLite pending-import journal coordinates staging, atomic same-filesystem move, and metadata commit.
- **Recovery protocol**: Create the pending-import row and owned staging/destination keys before receiving bytes. After validation, record hash/length/page count, atomically promote staged bytes, then commit the UUID document row while clearing the journal in one SQLite transaction. Under the exclusive data-directory lock, startup recovery verifies journaled files and finalizes valid interrupted imports or discards only files owned by that journal. Preserve every referenced file/record and quarantine/report unjournaled artifacts. Ordinary request failure cleans only that request's artifacts.
- **Rationale**: SQLite and filesystem writes cannot share a transaction. The journal makes interrupted operations attributable. Byte comparison preserves exact identity even in a digest collision; generated names and a private root prevent path traversal and direct static exposure.
- **Alternatives considered**: Hash-only identity is not exact-byte semantics. Filename identity contradicts the spec. An unjournaled file move can leave ambiguous crash orphans. PDF bytes in SQLite contradict the confirmed filesystem decision.
- **Sources**: [ASP.NET Core file upload security guidance](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads); [ASP.NET Core upload tutorial](https://learn.microsoft.com/en-us/aspnet/core/tutorials/razor-pages/uploading-files).

### Process exclusivity and tabs

- **Decision**: Acquire an OS-held exclusive lock for the data directory before migrations or request handling. A second process using the same directory fails with a clear startup error. Multiple browser tabs use the already-running process.
- **Rationale**: SQLite and the document store are for one local researcher. Cross-process writes would complicate journal recovery and active selection without a feature need.
- **Alternative considered**: SQLite locking alone does not coordinate filesystem operations or application-level import recovery.

### Document integrity, delivery, and native viewer boundary

- **Decision**: Verify stored bytes against the original SHA-256 on every new document open; do not reparse the PDF. Bind verification and delivery to the same opened file and ensure bytes cannot change throughout verification and delivery; fail safely before delivery if this cannot be established. For range requests reuse verification only when the implementation can prove the bytes cannot have changed; otherwise rehash. File size and timestamps alone are not proof of integrity. A missing/inaccessible file, read failure, or detected hash mismatch returns HTTP 503 `document_unavailable` with a safe error, preserving metadata and associations. An unknown UUID returns 404 `document_not_found`.
- **LexScholar guarantees**: UUID lookup, stored-byte integrity verification, unchanged bytes, content type/length, inline delivery, byte ranges, and consistent 404/503 errors.
- **Browser behavior**: Rendering, native viewer controls, page labels, zoom, and page navigation are owned by Firefox/Chromium. They are officially supported on Linux; their UI is not guaranteed to be identical. Test API delivery separately from native viewer usability.
- **Sources**: [ASP.NET Core minimal API range responses](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0); [ASP.NET Core file range processing](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.fileresult.enablerangeprocessing?view=aspnetcore-10.0).

### Automated testing

- **Decision**: xUnit.net v3, real temporary SQLite databases, temporary filesystem roots, in-memory ASP.NET Core integration tests, and browser end-to-end tests on Linux Firefox and Chromium. Cover UUID round trips, exact-byte deduplication, import limit boundaries, invalid configuration startup, recovery interruption points, process lock/multi-tab behavior, active selection restore, role isolation, integrity mismatch, HTTP errors, and range delivery.
- **Browser test boundary**: Automate UI journeys where possible; native viewer internals may limit automation, so record manual reader usability evidence when needed.
- **Sources**: [xUnit v3 getting started](https://xunit.net/docs/getting-started/v3/getting-started); [ASP.NET Core integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests).

## Security and privacy

- Bind to loopback, use same-origin endpoints, reject foreign origins on mutations/antiforgery failures, and do not enable wildcard CORS.
- Store the database, staging files, and originals in a private per-user Linux data directory outside web roots. Show the effective storage location and never disclose absolute paths in API errors.
- Keep storage keys server-generated and display names encoded. No external services or cloud storage are required. No malware scanning, OCR, or content sanitization is added by F001.
- Constitution v2.0.0 permits tracked incremental delivery. Full-library export/deletion is deferred to F002 ([Codecks $12y](https://thyago-cunha-dev.codecks.io/card/12y)); user-facing documentation must disclose the missing controls and F001 design must not unnecessarily block them. Principle II remains outstanding and F001 does not claim full compliance.

## Remaining technical notes

- Configured size/page limits must be positive; malformed, zero, or negative values fail startup. Defaults are 100,000,000 bytes and 2,000 pages.
- Every new open rehashes without reparsing. Range requests may reuse verification only with proof that the served bytes cannot have changed; otherwise they rehash.
- Full-library export/deletion and recovery UI for unavailable PDFs are outside F001. Documents with no project associations remain in the library.

## Version notes

As researched 2026-11-04: .NET 10 LTS, Bolero 0.25.x, EF Core 10, SQLite 3.46.1+, PdfPig 0.1.16 stable. Use latest compatible stable patches at implementation time and pin them. No implementation tasks or code are part of this research artifact.
