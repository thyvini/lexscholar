# T038 reader endpoints (and T034 GREEN)

The last task on `feat/f001-reader-roles`. That branch is merged as one PR with T034 (contract tests, [RED evidence](t034-reader-contract-red.md)) and T036 ([evidence](t036-document-roles.md)).

## Scope

- **New `src/LexScholar.Server/ReaderEndpoints.fs`**, compiled after `LibraryEndpoints.fs`:
  - `PATCH /api/projects/{projectId:guid}/documents/{documentId:guid}`:
    - The JSON body is parsed first, with the same parser as association creation. A missing or invalid role returns 400 `invalid_role`.
    - Then `DocumentRoles.changeRole (LibraryRepository.changeRole directory)` runs. Success returns 200 with the `ProjectDocument` DTO (role serialized by name); `AssociationNotFound` returns 404 `not_found`.
  - `GET /api/documents/{documentId:guid}/content`:
    - Calls `VerifiedDocumentContent.openRead directory id RequestAborted`.
    - `DocumentNotFound` returns 404 `document_not_found`. `DocumentUnavailable` returns 503 `document_unavailable` with a fixed title that names no path or storage key.
    - On success it sets `Content-Disposition: inline` and `X-Content-Type-Options: nosniff`, then returns `Results.Stream(stream, "application/pdf", enableRangeProcessing = true)`. ASP.NET Core adds `Accept-Ranges`, `Content-Length`, 206 with `Content-Range`, and 416, and disposes the stream after the response.
- **`Endpoints.fs`** maps the reader routes. The last two 501 stubs and the `notImplemented` helper are removed.
- **`LibraryEndpoints.fs`**: `problem`, `associationDto` and `associationRequest` are now public, so both endpoint modules share one problem shape, role DTO and body parser. The lead approved this.
- **Existing tests changed**, each with a "(T038)" comment, because the routes are no longer 501:
  - `HarnessSmoke.fs`: PATCH with an empty body returns 400; content for an unknown id returns 404.
  - `HostSecurityTests.fs`, "same-origin token and cookie permit each contract mutation": the PATCH `test` body returns 400.
  - `HostSecurityTests.fs`, "document GET needs no antiforgery token and accepts ranges": a tokenless ranged GET for an unknown id returns 404, not 403.

  No test now expects 501.
- **Unchanged**:
  - `Program.fs`, the Application layer, `HostSecurity.fs`;
  - `LibraryOperations.ChangeRole`: `Library.unimplemented` is untouched, and nothing in the host uses it.

Decisions approved by the lead on 2026-11-11:
- Plain `inline`, without `filename*`. The contract asks only for inline, and leaving out the filename avoids encoding user-supplied names into a header.
- Non-GUID ids get the routing 404, as in `LibraryEndpoints`; path traversal is T042.

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, Microsoft.Playwright 1.64.1 (its own Chromium and Firefox builds). Branch rebased onto `8210007` (release/0.1.0 after #22). Tests ran as uid 1000.

## RED

T034's tests are T038's RED (commit `53c59e8` after the rebase). I re-ran them on the rebased branch (`6415bfa`) before implementing:

```sh
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T034'
```

Exit 2: 13 total, 13 failed, each with `Actual: NotImplemented` (the 501 stubs).

## GREEN

Commit `75e786f`.

- T034 filter: 13/13 passed.
- Before the expectation updates, the full suite had exactly one failure outside T034: the T009 ranged GET, which still expected 501. It was updated as described above.

Sensitivity checks (temporary edits to `ReaderEndpoints.fs`, not committed; the file was restored and rebuilt afterwards):
- `enableRangeProcessing = false`: 3 T034 tests failed (range, unsatisfiable range, and the full GET's `Accept-Ranges`).
- Removing the `nosniff` header: 1 test failed (the full GET).

## Final verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

- Restore and build: 0 warnings, 0 errors.
- Full suite: 306/306 passed with 0 skipped, browser journeys included. That is 291 on `8210007`, plus 13 T034, plus 2 T036.
- The T035 cases, including both T037 follow-ups, still pass.

## Limitations

- **Browser viewer:** native viewer behavior (page navigation, how often ranges are requested) is browser behavior and is not tested here. The reader UI is T039, and manual usability is T046.
- **Changes during delivery:** availability is checked once, when the content is opened. A change to the stored file while it is streaming, or between range requests, is not detected (research.md, "Document availability on open").
- **`HEAD` requests:** not mapped, since the contract defines only `GET`.
