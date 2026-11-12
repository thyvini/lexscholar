# Quickstart Validation: Research Workspace and PDF Library

**Feature**: F001  
**Codecks card**: [$12s](https://thyago-cunha-dev.codecks.io/card/12s)

This guide validates what F001 shipped; it is not a task list. See [data-model.md](data-model.md) for persistence/recovery rules, [contracts/http-api.yaml](contracts/http-api.yaml) for HTTP behavior and [docs/local-workspace.md](../../docs/local-workspace.md) for the researcher-facing description of the data directory.

## Prerequisites

- Linux desktop and the .NET SDK pinned in `global.json` (10.0.114, rolling forward to the latest patch). The official SDK distribution includes the matching ASP.NET Core runtime and targeting pack.
- Optional: the `wasm-tools` workload. When it is installed, a Release publish relinks the WebAssembly runtime natively (an optimized publish). It is not needed to build or test: CI does both without it.
- Officially supported browsers: Firefox and Chromium on Linux.
- Browser journey tests (`tests/LexScholar.Browser.Tests`, trait `Category=Browser`) use Playwright's Chromium and Firefox builds. Install them once per machine with `scripts/install-browsers.sh` (cached in `~/.cache/ms-playwright`; CI adds `--with-deps` to install system libraries; see [research.md](research.md#automated-testing)). Without them, browser tests fail rather than skip; exclude them with `--filter-not-trait 'Category=Browser'`.
- No cloud credentials or external services.
- Test PDFs: the synthetic fixtures in `tests/Fixtures/` (see its README for hashes): `valid.pdf` (two pages), `valid-renamed.pdf` (an exact copy under another name), `distinct.pdf` (different bytes), `encrypted.pdf`, `corrupt.pdf` (truncated) and `not-a-pdf.txt`. Size and page boundaries are exercised by configuring the limits around a fixture's byte length and page count, not with separate boundary files.

## Automated checks

From the repository root:

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release
```

Expected: zero build warnings, and the unit, SQLite/filesystem, HTTP, process-lock and Firefox/Chromium browser journey suites pass with zero skips. CI (`.github/workflows/ci.yml`) runs the same commands on every PR and push to `main` and `release/**`. Persistence tests use isolated temporary roots. Browser-native viewer internals limit automation, so reader navigation was verified manually ([T040 evidence](evidence/t040-reader-verification.md)).

The SC-002 import corpus (scenario 8) is outside `LexScholar.sln`, because its PDFs are downloaded rather than committed:

```sh
scripts/fetch-sc002-corpus.sh specs/001-research-workspace/sc002-corpus.csv corpus/sc002
LEXSCHOLAR_SC002_CORPUS=$PWD/corpus/sc002 dotnet test --project tests/LexScholar.Corpus.Tests/LexScholar.Corpus.Tests.fsproj -c Release
```

## Run locally

```sh
dotnet run --project src/LexScholar.Server
```

For a published build, start it from the publish directory so the client assets are found:

```sh
dotnet publish src/LexScholar.Server/LexScholar.Server.fsproj -c Release -o <dir> --no-restore -warnaserror
cd <dir> && dotnet LexScholar.Server.dll        # or pass --contentRoot <dir>
```

Started from any other directory, the API still works but every client asset returns `404`.

Expected: one local loopback process starts, logs `Using data directory <path>.` and then Kestrel's `Now listening on: http://127.0.0.1:<port>`. Open that URL: `/` redirects to `/index.html`, the single-page client (projects, library and reader on one page; there are no client-side routes). A second process pointed at the same data directory exits with code 1 and the message `Data directory '<path>' is already in use by another instance. Connect to that instance or choose a different directory.` Multiple tabs connect to the existing process. Database, staging and PDF files are not served as static content.

At startup, in order: configuration is validated (invalid values stop startup with an error naming the setting), the data directory lock is acquired, `workspace.sqlite` is opened and the EF migrations (`Initial`, `Projects`, `Documents`) are applied, then startup cleanup empties `staging/` and moves unreferenced originals to `quarantine/` (logging a warning for each), and only then does the server listen. See [T010 evidence](evidence/t010-sqlite-foundation.md) for the timestamp/UUID conventions.

### Where data lives

Everything is in one data directory, created with mode `0700` if missing:

- Default: `$XDG_DATA_HOME/lexscholar`, or `~/.local/share/lexscholar` when `XDG_DATA_HOME` is unset or not absolute.
- `workspace.sqlite`: projects, the active project, document records, associations and roles.
- `originals/`: imported PDFs, byte for byte, read-only, under generated names.
- `staging/`: uploads in progress; emptied at startup.
- `quarantine/`: originals left without a document record by an interrupted import; never deleted automatically.
- `.lexscholar.lock`: the single-instance lock file.

Settings, as configuration keys (environment variables use `__`, e.g. `LexScholar__DataDirectory`; command-line options use `--LexScholar:DataDirectory=<path>`):

| Key | Default | Notes |
|---|---|---|
| `LexScholar:DataDirectory` | see above | Absolute path; not the filesystem root or an existing file. |
| `LexScholar:Port` | `5000` | Integer 0–65535; `0` picks an ephemeral port, shown in the log. |
| `LexScholar:ImportLimits:MaxBytes` | `100000000` | Positive whole number. |
| `LexScholar:ImportLimits:MaxPages` | `2000` | Positive whole number. |

### HTTP routes

All routes are under `/api`; [contracts/http-api.yaml](contracts/http-api.yaml) has the request and response schemas and problem codes.

| Method and path | Purpose |
|---|---|
| `GET /api/antiforgery` | Antiforgery request token and cookie for unsafe requests. |
| `GET /api/projects` | Projects and the active project. |
| `POST /api/projects` | Create a project. |
| `PUT /api/projects/{projectId}/active` | Select the active project. |
| `GET /api/documents` | Every document in the shared library. |
| `POST /api/documents` | Import a PDF (multipart field `file`); `201` for a new document, `200` when identical bytes are reused. |
| `GET /api/projects/{projectId}/documents` | A project's documents with their roles. |
| `POST /api/projects/{projectId}/documents` | Associate a library document with a project under a role. |
| `PATCH /api/projects/{projectId}/documents/{documentId}` | Change the document's role in that project. |
| `DELETE /api/projects/{projectId}/documents/{documentId}` | Remove the association; the document stays in the library. |
| `GET /api/documents/{documentId}/content` | The original PDF bytes, with range support. |

IDs are UUIDs; a value that is not a UUID does not match a route and returns `404`.

### Local host security (T009)

- The listener is IPv4 loopback only, on port 5000 by default. Set `LexScholar__Port` (or `LexScholar:Port` in configuration) to an integer from 0 to 65535; port 0 selects an ephemeral port, reported in the host log. Generic URL and Kestrel endpoint settings do not change the bind address or port. Use `http://127.0.0.1:<port>` or `http://localhost:<port>` consistently: they are distinct browser origins.
- Before a mutation, the client fetches `GET /api/antiforgery` from the same origin. Read `requestToken` from the JSON response, retain the HttpOnly SameSite=Strict cookie, and send both the cookie and the `X-CSRF-TOKEN` header on POST, PUT, PATCH, DELETE, and other unsafe methods. Browser `fetch` uses same-origin credentials by default. Do not put the token in the URL. The cookie name is scoped to the configured data directory so independent instances do not overwrite each other's cookies across ports. Tokens are process-local; fetch a new one after a restart. Native document GET requests, including ranges, do not need a token.
- Invalid Host, foreign Origin, or missing/invalid antiforgery tokens return a safe `403` problem response with a security-specific `code`; unexpected errors return a generic `500` problem. These host-level responses are separate from the documented domain problem codes.
- Static hosting uses the build/publish manifest, not the development fallback that serves newly added files. Rebuild to include new client assets, and keep the configured data directory outside public asset trees. See [T009 evidence](evidence/t009-host-security.md) for checks and limitations.

## Acceptance validation scenarios

1. **Projects and active selection (US1, FR-001/002/011/013/016, SC-001/005)**: create two projects, switch between them, verify associations remain separate, and try a case/outer-whitespace duplicate. Restart the host and verify the last active project is restored.
2. **Shared import and exact-byte reuse (US2, FR-003–006, SC-003)**: import a valid PDF; repeat with identical bytes and a different filename; verify the same UUID/document record. Import a different byte sequence with the same name and verify a distinct record. Start several valid identical imports together: every request must succeed and return the same document, leaving one record, one stored original, intact associations, and no staging files.
3. **Configured PDF limits (FR-003, SC-008)**: verify defaults of 100,000,000 bytes and 2,000 pages. Exercise just-below, at, and over each boundary; over-limit cases must provide a clear error and leave existing data and files unchanged. Import a multi-page PDF, lower both positive limits below its size and page count, restart, and verify the existing PDF opens unchanged while new over-limit imports, including re-imports, are rejected. Verify malformed, zero, or negative limit configuration prevents startup.
4. **Project-specific roles and associations (US2/US3, FR-002/007–010, SC-004)**: associate one UUID document with two projects under different roles; switch projects and verify the active project's document list refreshes with its own roles and no associations from the other project. Check the empty state for a project with no documents. Change/remove in one project and verify the other is unaffected. Remove the final association and verify the document remains in the shared library.
5. **Original bytes and endpoint delivery**: compare served bytes byte-for-byte to imported input. Verify `application/pdf`, inline disposition, content length, `nosniff`, valid `206` range responses, and `416` unsatisfiable ranges. API checks do not claim to control browser rendering or navigation UI.
6. **Unavailable originals (FR-012/017, SC-007)**: in a test data root, remove a stored original, make it unreadable, or change its length. On next open, verify the content endpoint returns `503 document_unavailable` in each case, and that stored originals are read-only. Same-length changes are not detected by design (see research.md). In all cases preserve document metadata and every association. The user-facing UI provides an error; no repair UI is present.
7. **Native reader behavior (US3, FR-008, SC-006)**: in Firefox and Chromium on Linux, open an associated PDF from the active project's list; it is shown in the browser's own PDF viewer inside the page (with an **Open in new tab** link). Navigate to a specified page and confirm page controls remain within the available page range. Record browser/version separately from API delivery evidence.
8. **Import success and errors (FR-003/012/015, SC-002/007)**: import a documented corpus of 100 supported, valid, readable PDFs within configured limits; at least 99 must import successfully. Separately test non-PDF, corrupt, encrypted, oversized, and over-page-limit inputs for understandable errors, no new record/association, and unchanged existing bytes.
9. **Unknown ID and path safety**: unknown valid UUID returns `404 document_not_found`; malformed and encoded traversal-like IDs do not match the UUID routes and return `404` without reaching a file; no absolute filesystem path appears in responses.
10. **Persistence and import recovery (FR-011, SC-005)**: repeat 100 stop/start checks over stable temporary data roots, verifying projects, shared documents, roles, associations, and active project. Arrange post-crash states (a leftover staging file; a stored original without a document row) and restart: staging is emptied, the unreferenced original is moved to `quarantine/`, and committed data is unchanged.
11. **Single instance and multiple tabs**: start a second process using the same data directory and expect a clear refusal; use multiple tabs on the original process and verify data updates and persisted active selection are shared.

## Evidence to retain

F001's verification records are in [evidence/](evidence/), and the milestone review, SC-002 corpus results and known issues are in [verification.md](verification.md). When re-validating, record commands and results, Linux distribution, browser and version, fixture hashes, configured limits, restart-cycle count and startup-cleanup outcomes. Do not check in real research PDFs or disclose their content in logs.

Constitution v2.0.0 permits incremental delivery with disclosure: full-library export/deletion remains unavailable in F001 and is tracked in F002 ([Codecks $12y](https://thyago-cunha-dev.codecks.io/card/12y)). Full Principle II compliance is not claimed.
