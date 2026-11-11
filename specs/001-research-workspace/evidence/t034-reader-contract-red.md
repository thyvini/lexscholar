# T034 reader contract RED (and T037 review follow-ups)

T034 is test-first for T036 (association role updates) and T038 (role PATCH and content endpoint). Following the lead's decision (2026-11-10), it is not merged on its own. T034, T036 and T038 share the branch `feat/f001-reader-roles`, which becomes one PR once GREEN, as T013 did with T016/T017 in #10. GREEN is recorded with T038.

## Scope

`tests/LexScholar.Server.Tests/ReaderContractTests.fs` (13 cases, Trait `Task=T034`), registered in `LexScholar.Server.Tests.fsproj`. No changes to `TestHarness.fs`, src or fixtures.

- **Setup:** each test starts `TestHarness` and arranges data directly in its data directory through `WorkspaceDb` rows:
  - one workspace;
  - projects A and B;
  - one `LibraryDocumentRow` (`source.pdf`, 4,096 bytes, real SHA-256 of the content);
  - associations A and B, both `FocusedSource`.

  The original goes to `originals/<key>` with mode 0400. Its bytes are deterministic and start with `%PDF-1.7`; the content endpoint does not parse, so no fixture is needed. HTTP import is T031 and is not used.
- **Requests:** PATCH obtains an antiforgery token and sends `Origin` and `X-CSRF-TOKEN`, as in `ProjectContractTests`. The helpers are copied locally because they are private there.
- **Unchanged** means every column of every `LibraryDocuments` and `ProjectDocuments` row is equal before and after the request.

| Area | Test | Cases |
|---|---|---|
| Role isolation (FR-009, SC-004) | PATCH changes the role in one project only. Checks: 200 with a `ProjectDocument` body (`projectId`, `document.id`, `role`, unchanged `addedUtc`); A's row becomes `ReferenceSource`; B stays `FocusedSource`; the document row is unchanged. | 1 |
| | PATCH for an association that does not exist is 404 `not_found` and changes nothing. Cases: document not associated with the project; unknown project. | 2 |
| | PATCH with an invalid (`"Primary"`) or missing role is 400 `invalid_role` and changes nothing. | 2 |
| Content | A full GET returns 200: `application/pdf`, `Content-Length` 4096, `Accept-Ranges: bytes`, `Content-Disposition: inline`, `X-Content-Type-Options: nosniff`, exact bytes. | 1 |
| | `Range: bytes=100-199` returns 206 with `Content-Range: bytes 100-199/4096` and exactly that slice. | 1 |
| | `Range: bytes=5000-` returns 416. | 1 |
| | An unknown UUID returns 404 `document_not_found` (problem+json), rows unchanged. | 1 |
| | A missing, unreadable (mode 000), shorter (4,000 bytes) or longer (4,097 bytes) original returns 503 `document_unavailable`. Checks: rows unchanged; the problem body contains neither the data directory nor the storage key. | 4 |

The approved plan counted 14 cases. The list above is the same set of tests, which comes to 13; the plan's total was a miscount. CSRF/Origin rejection is left to `HostSecurityTests`, and path traversal to T042.

The unreadable case checks its own precondition. After `chmod 000` it asserts that `File.OpenRead` throws. That fails with a clear message under root or `CAP_DAC_OVERRIDE`, instead of skipping.

### T037 review follow-ups (PR #19, recorded in tasks.md)

- **(b)** `ContentAvailabilityTests.fs`: the `Environment.UserName <> "root"` precondition was replaced with the same `File.OpenRead`-throws check after `chmod 000`. Commit `e1f0c08`.
- **(a)** `VerifiedDocumentContent.fs`: `tryOpen` now takes the recorded length. It reads `stream.Length` inside the same `try`, and disposes the handle whenever it is not returned, including when reading the length throws. An fstat failure now returns `DocumentUnavailable` without leaking the handle. Commit `9c63148`. There is no test first: a failing `fstat` on an open handle can't be set up without a test-only seam, and adding one isn't justified (Principle V). The existing T035 cases cover both the length mismatch and the open failure.

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114. Written on `fa32db6` (release/0.1.0 after #18 and #19) and rebased onto `4e131a9` (after #20 and #21) and then `8210007` (after #22); the commit hashes below are the final rebased ones. Tests ran as uid 1000 (not root).

## RED

T034 commit `a7b7e13`.

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build --filter-not-trait 'Task=T034'
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T034'
```

- Restore and build succeeded with 0 warnings and 0 errors.
- The suite without T034, run at `9c63148` (including both T037 follow-ups and browser journeys), passed 268/268 with 0 skipped.
- The T034 run intentionally exits 2: 13 total, 13 failed, 0 succeeded, 0 skipped.
  - Every failure is the status assertion: `Assert.Equal() Failure: Values differ`, with `Expected: OK / PartialContent / RequestedRangeNotSatisfiable / NotFound / BadRequest / ServiceUnavailable` and, in all 13 cases, `Actual: NotImplemented`. That is the T045 route stub's 501.
  - `expectStatus` first asserts the status is not 403, and every PATCH passed that check: the requests carried valid antiforgery tokens and reached the route stub.
- No test passes at RED, and none can turn GREEN before T038, because every reader route is still a stub. T036 alone changes no HTTP result.

T037 follow-ups: T035 filter 11/11 passed after both changes.

Sensitivity check for (b), temporary and not committed: leaving the original at mode 0400 instead of 000 made both unreadable T035 cases fail with the precondition message. The file was restored and rebuilt afterwards.

## Limitations

- **Expected failures:** T036 and T038 are unimplemented, so the full suite on this branch has 13 expected T034 failures until T038. The branch is not opened as a PR before then.
- **Headers:** the header assertions (`nosniff`, `inline`) follow the contract. No current middleware adds `X-Content-Type-Options`, so T038 must.
- **Role serialization:** the role JSON uses the contract's string enum (`"ReferenceSource"`); T038 decides how it is serialized.
