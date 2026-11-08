# Quickstart Validation: Research Workspace and PDF Library

**Feature**: F001  
**Codecks card**: [$12s](https://thyago-cunha-dev.codecks.io/card/12s)

This is a validation guide for implementation, not a task list. See [data-model.md](data-model.md) for persistence/recovery rules and [contracts/http-api.yaml](contracts/http-api.yaml) for HTTP behavior.

## Prerequisites

- Linux desktop and .NET 10 SDK pinned by the implementation.
- The ASP.NET Core runtime and targeting pack matching the SDK (included with the official .NET SDK distribution). Install the `wasm-tools` workload for optimized WebAssembly publishing.
- Officially supported browsers: Firefox and Chromium on Linux.
- No cloud credentials or external services.
- Test PDFs: readable fixtures at size/page boundaries, encrypted PDF, malformed `.pdf`, non-PDF, exact duplicate under a different filename, and different-byte PDF.

## Automated checks

After implementation, from repository root:

```sh
dotnet restore LexScholar.sln
dotnet test --solution LexScholar.sln --configuration Release
```

Expected: unit, SQLite/filesystem, HTTP, process-lock, and Firefox/Chromium browser journey suites pass. Persistence tests use isolated temporary roots. Browser-native viewer internals may limit automation; record manual reader usability evidence where needed.

## Run locally

```sh
dotnet run --project src/LexScholar.Server
```

Expected: one local loopback process starts and reports its URL and effective private data directory. A second process pointed at that same data directory fails clearly. Multiple tabs connect to the existing process. Database, staging, and PDF files are not served as static content.

### Local host and future client requests (T009)

- The listener is IPv4 loopback only, on port 5000 by default. Set `LexScholar__Port` (or `LexScholar:Port` in configuration) to an integer from 0 to 65535; port 0 selects an ephemeral port, reported in the host log. Generic URL and Kestrel endpoint settings do not change the bind address or port. Use `http://127.0.0.1:<port>` or `http://localhost:<port>` consistently: they are distinct browser origins.
- Before a mutation, the client fetches `GET /api/antiforgery` from the same origin. Read `requestToken` from the JSON response, retain the HttpOnly SameSite=Strict cookie, and send both the cookie and the `X-CSRF-TOKEN` header on POST, PUT, PATCH, DELETE, and other unsafe methods. Browser `fetch` uses same-origin credentials by default. Do not put the token in the URL. The cookie name is scoped to the configured data directory so independent instances do not overwrite each other's cookies across ports. Tokens are process-local; fetch a new one after a restart. Native document GET requests, including ranges, do not need a token.
- Invalid Host, foreign Origin, or missing/invalid antiforgery tokens return a safe `403` problem response with a security-specific `code`; unexpected errors return a generic `500` problem. These host-level responses are separate from the existing documented domain `400` problem codes. API route implementations remain deferred.
- Static hosting uses the build/publish manifest, not the development fallback that serves newly added files. Rebuild to include new client assets, and keep the configured data directory outside public asset trees. See [T009 evidence](evidence/t009-host-security.md) for checks and limitations.

## Acceptance validation scenarios

1. **Projects and active selection (US1, FR-001/002/011/013/016, SC-001/005)**: create two projects, switch between them, verify associations remain separate, and try a case/outer-whitespace duplicate. Restart the host and verify the last active project is restored.
2. **Shared import and exact-byte reuse (US2, FR-003–006, SC-003)**: import a valid PDF; repeat with identical bytes and a different filename; verify the same UUID/document record. Import a different byte sequence with the same name and verify a distinct record. Start simultaneous valid identical imports behind a synchronization barrier: every request must succeed and return the same document, leaving one record, original bytes and associations intact, with safe staging/journal cleanup. Safe internal retries are allowed.
3. **Configured PDF limits (FR-003, SC-008)**: verify defaults of 100,000,000 bytes and 2,000 pages. Exercise just-below, at, and over each boundary; over-limit cases must provide a clear error and leave existing data and files unchanged. Import a multi-page PDF, lower both positive limits below its size and page count, restart, and verify the existing PDF opens unchanged while new over-limit imports, including re-imports, are rejected. Verify malformed, zero, or negative limit configuration prevents startup.
4. **Project-specific roles and associations (US2/US3, FR-002/007–010, SC-004)**: associate one UUID document with two projects under different roles; switch projects and verify the active project's document list refreshes with its own roles and no associations from the other project. Check the empty state for a project with no documents. Change/remove in one project and verify the other is unaffected. Remove the final association and verify the document remains in the shared library.
5. **Original bytes and endpoint delivery**: compare served bytes byte-for-byte to imported input. Verify `application/pdf`, inline disposition, content length, `nosniff`, valid `206` range responses, and `416` unsatisfiable ranges. API checks do not claim to control browser rendering or navigation UI.
6. **Stored integrity and unavailable errors (FR-012/017, SC-007)**: alter or remove a stored file in a test data root. On next open, verify the original SHA-256 check detects changed bytes and content endpoint returns `503 document_unavailable`; verify missing/inaccessible files return the same error code. In all cases preserve document metadata and every association. The user-facing UI provides an error; no repair UI is present.
7. **Native reader behavior (US3, FR-008, SC-006)**: in Firefox and Chromium on Linux, open an associated PDF and navigate to a specified page; confirm page controls remain within the available page range. Record browser/version separately from API delivery evidence.
8. **Import success and errors (FR-003/012/015, SC-002/007)**: import a documented corpus of 100 supported, valid, readable PDFs within configured limits; at least 99 must import successfully. Separately test non-PDF, corrupt, encrypted, oversized, and over-page-limit inputs for understandable errors, no new record/association, and unchanged existing bytes.
9. **Unknown ID and path safety**: unknown valid UUID returns `404 document_not_found`; malformed/traversal-like ID is rejected; no absolute filesystem path appears in responses.
10. **Persistence and import recovery (FR-011, SC-005)**: repeat 100 stop/start checks over stable temporary data roots, verifying projects, shared documents, roles, associations, and active project. Fault-inject at each journal/stage/move/SQLite commit boundary; recovery preserves committed data and discards only artifacts owned by the interrupted import journal.
11. **Single instance and multiple tabs**: start a second process using the same data directory and expect a clear refusal; use multiple tabs on the original process and verify data updates and persisted active selection are shared.

## Evidence to retain

Record command/results, Linux distribution, browser and version, fixture hashes, configured limits, restart-cycle count, and fault-injection outcomes in implementation review. Do not check in real research PDFs or disclose their content in logs.

Constitution v2.0.0 permits incremental delivery with disclosure: full-library export/deletion remains unavailable in F001 and is tracked in F002 ([Codecks $12y](https://thyago-cunha-dev.codecks.io/card/12y)). Full Principle II compliance is not claimed.
