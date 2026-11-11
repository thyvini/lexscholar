# T023, T031 library and association HTTP endpoints

## Scope

- **T031** adds `src/LexScholar.Server/LibraryEndpoints.fs` (new). It implements five routes from `contracts/http-api.yaml`:
  - `GET /api/documents`
  - `POST /api/documents`
  - `GET /api/projects/{projectId}/documents`
  - `POST /api/projects/{projectId}/documents`
  - `DELETE /api/projects/{projectId}/documents/{documentId}`

  Route ids use the `{x:guid}` constraint, as `ProjectEndpoints` does. `PATCH` (role) and `GET …/content` stay as 501 stubs for T038.
- **One import per host.** `LibraryEndpoints.map` calls `ImportDocument.create` once. `map` runs once per host build in `Program.createHost`, and every `POST /api/documents` request shares the returned function, so concurrent identical uploads share one commit section. The dependencies are the real `PdfValidator`, `DocumentStore` and `LibraryRepository`, with the configured `ImportLimits`.
- **Upload handling.**
  - `MultipartReader` (`Microsoft.AspNetCore.WebUtilities`, part of the shared framework, so there is no new dependency and no research.md change) reads only the first section.
  - If that section is the `file` form field, its body streams straight into the import. Otherwise the request gets 400 `not_a_pdf`, and the section body and any later sections are never read.
  - `ReadFormAsync` and `IFormFile` are not used, because they buffer uploads to the framework's temp directory, outside the private data directory.
  - The preamble and section headers stay within `MultipartReader`'s default limits.
  - Antiforgery already has `SuppressReadingTokenFromFormBody`, so it never reads the body.
- **Request size.**
  - Kestrel's default `MaxRequestBodySize` is 30,000,000 bytes, which is below the default 100,000,000-byte import limit. The handler sets it to unlimited for that request only, and a code comment explains why. Every other route keeps Kestrel's default.
  - What bounds the upload instead: only the first section is read, and the import stops staging after `MaxBytes + 1` bytes. An over-limit upload is answered with 413 without reading the rest of the body.
- **Response shapes.**
  - `Document` records serialize directly, with camelCase names and `contentUrl` from `Library.documentView`. That function was private in `Library.fs` and is now public, so the import response uses the same mapping as the listing.
  - `ProjectDocument` is an anonymous object, with `role` given as the enum name.
  - Problem responses carry `code` and an actionable title, never a path.
- **Status mapping.**

  | Route | Outcome | Response |
  |---|---|---|
  | `GET /api/documents` | success | 200 |
  | `POST /api/documents` | new document | 201, `Location` = `contentUrl` |
  | | identical bytes reused | 200 |
  | | `TooLarge` | 413 `upload_too_large` |
  | | `NotPdf` / `Corrupt` / `Encrypted` / `TooManyPages` | 422 `not_a_pdf` / `corrupt_pdf` / `encrypted_pdf` / `page_limit_exceeded` |
  | `GET /api/projects/{id}/documents` | success | 200 |
  | | `ProjectNotFound` | 404 `not_found` |
  | `POST /api/projects/{id}/documents` | success | 201, `Location` `/api/projects/{p}/documents/{d}` |
  | | `ProjectNotFound` | 404 `not_found` |
  | | `DocumentNotFound` | 404 `document_not_found` |
  | | `DuplicateAssociation` | 409 `duplicate_association` |
  | `DELETE /api/projects/{id}/documents/{documentId}` | success | 204 |
  | | `AssociationNotFound` | 404 `not_found` |

- **Contract readings** (lead decision, 2026-11-10; no contract change). Each uses a status the contract already lists for the route, with an existing code:
  - A: `POST /api/documents` with no multipart body, or a first part that is not `file`, gives 400 `not_a_pdf`.
  - B: an association `POST` with malformed JSON or a missing or invalid role gives 400 `invalid_role`. Role is checked first, following the Projects precedent of malformed body → `invalid_name`.
  - C: an association `POST` with a missing or non-UUID `documentId` gives 404 `document_not_found`.
- **Reviewer follow-up from PR #21.** `Library.listForProject` returns `AssociationError`, but listing can only produce `ProjectNotFound`. The endpoint maps that case to 404 `not_found`. Any other case raises `invalidOp`, which reaches the host's generic 500 handler instead of being treated as a reachable response.
- **Shared files** (approved by the lead):
  - `Endpoints.fs`: `map` takes the `WorkspaceConfiguration`, calls `LibraryEndpoints.map`, and drops the five stubs;
  - `Program.fs`: passes `configuration` to `Endpoints.map`;
  - `LexScholar.Server.fsproj` compiles `LibraryEndpoints.fs` before `Endpoints.fs`;
  - `LexScholar.Server.Tests.fsproj` adds `LibraryContractTests.fs` and copies `tests/Fixtures/*`.

  Two existing tests asserted 501 from the replaced stubs, and only their expectations changed (commit `fb3cbce`):
  - `HarnessSmoke.fs`, "all contract routes are reachable with expected behavior";
  - `HostSecurityTests.fs`, "same-origin token and cookie permit each contract mutation".

  Their purpose is unchanged: each route is reachable, and a mutation that carries a valid token gets past CSRF.

### Tests

The 14 cases are in `tests/LexScholar.Server.Tests/LibraryContractTests.fs`, under Trait `Task=T023`. They run on the in-process `TestHarness` with real fixtures; limits are set through harness settings and projects are seeded in the database. Every mutation sends the Origin header, the antiforgery cookie and `X-CSRF-TOKEN`. Every problem response is checked for `application/problem+json`, its `code`, and the absence of the data-directory path. The small request helpers are copied from `ProjectContractTests.fs` rather than shared, so that file is untouched.

| Test | Covers |
|---|---|
| POST imports a new PDF, reuses identical bytes and keeps different bytes distinct | 201 + Document shape + `Location`; 200 with the same id and first filename under another name; 201 with a new id for different bytes; two originals |
| GET lists every library document, including unassociated ones | listing order and shape |
| POST rejects invalid or over-limit uploads safely and changes nothing (5 cases) | not-a-pdf 422, corrupt 422, encrypted 422, `MaxBytes=752` 413, `MaxPages=1` 422. A document imported under default limits on the same workspace stays the only one, originals are unchanged, and staging is empty. The 413 case runs through the same handler with the request limit lifted. |
| POST without a file part is rejected as not a PDF without importing | JSON body, and a multipart body whose first part is `note` followed by a valid `file`, both give 400 `not_a_pdf` with nothing imported (reading A) |
| POST accepts bodies above the server's default request size up to the configured limit | a 30,000,001-byte body generated in memory, with `MaxBytes=40,000,000`, gives 422 `not_a_pdf` rather than a Kestrel rejection |
| simultaneous uploads of the same PDF all succeed with the same document | 8 uploads with `Task.WhenAll`, alternating two filenames: all 200/201, one id, exactly one 201, one row, one original, staging empty |
| POST associates one document with two projects under their own roles | 201 + ProjectDocument shape + `Location`; each project's list shows its own role |
| POST association failures return contract problems and change nothing | invalid role, missing role, malformed JSON and empty body give 400 `invalid_role` (B); non-UUID, missing and unknown `documentId` give 404 `document_not_found` (C); a duplicate gives 409; an unknown project gives 404 `not_found`. The existing association is unchanged. |
| DELETE removes only that association and keeps the shared document | 204; the other project is unaffected; a repeat gives 404 `not_found`; removing the final association keeps the document and its original |
| GET project documents returns not_found for an unknown project and an empty list for an empty one | 404 `not_found`; `[]` |

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114. Tests ran as uid 1000, with temporary directories under `/tmp` (tmpfs). The base was `release/0.1.0` at `4e131a9`, which already includes #20 and #21.

## RED

Commit `874b8c2`: the tests and the project-file entries only, against the existing 501 stubs.

```sh
dotnet build LexScholar.sln -c Release -warnaserror
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj -c Release --no-build --filter-trait 'Task=T023'
```

14 total, 14 failed, 0 succeeded. Every request carried a valid antiforgery token, and none got 403.
- 12 tests failed on a status assertion: `Expected: Created` (or `BadRequest`, `OK`), `Actual: NotImplemented`.
- The concurrency test failed with "Expected 201 or 200, got 501."
- The 30,000,001-byte test failed with `HttpRequestException: Error while copying content to a stream.` The stub answered 501 without reading the body, and Kestrel closed the connection while the client was still sending. No status reaches the client in that case, so this test can only fail as a transport error, but the failure is the server not accepting the body.

## GREEN

Commit `0129b40`. T023 passed 14/14, and 14/14 again on 3 further runs. Build: 0 warnings.

Sensitivity checks (temporary edits, not committed; the file was restored and the solution rebuilt each time):
- **Import created per request** (`ImportDocument.create` called inside the handler): the concurrency test failed in 5 of 5 runs, and the other 13 passed.
- **Request size not lifted**: the 30,000,001-byte test failed with `HttpRequestException: Error while copying content to a stream`, and the other 13 passed.

## Final verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

Restore and build succeeded with 0 warnings and 0 errors. The full suite passed 291/291 (277 at `4e131a9` plus the 14 T023 cases), 0 skipped, browser tests included. No temporary test directories remained under `/tmp/lexscholar-tests` or `/tmp/lexscholar-schema-tests`.

## Limitations

- **Concurrency timing.** Eight simultaneous requests make overlap likely but not guaranteed. The per-request sensitivity check failed in 5 of 5 runs.
- **Over-limit body.** The 413 path does not read the rest of the body. The test uploads are small (753 bytes), so the clients always finish sending. Not reading a large over-limit body is a property of the code path (the import stops at `MaxBytes + 1` and the handler returns), and no test measures it.
- **Malformed route ids.** A non-UUID path segment doesn't match the `{x:guid}` routes, so it gets the framework's plain 404, not a problem body. This is the same as the existing project routes.
- **Not yet in the client.** These endpoints are not used by the client until T032.
