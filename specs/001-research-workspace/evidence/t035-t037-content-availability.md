# T035, T037 content availability on open

## Scope

- **Ports and types** (`src/LexScholar.Application/Contracts.fs`, `Ports.fs`). Added `ContentFailure = DocumentNotFound | DocumentUnavailable`. `OpenVerifiedContent` now returns `Task<Result<VerifiedContent, ContentFailure>>`, so T038 can map an unknown UUID to 404 `document_not_found` and an unavailable original to 503 `document_unavailable`. The T045 signature returned `Task<VerifiedContent>` and could not tell the two apart. The `VerifiedContent` and `OpenVerifiedContent` comments no longer promise "same protected handle, bytes protected from change"; they state the availability check and that content is not rehashed, per research.md ("Document availability on open"), which assigns this rewording to T037. Approved by the lead on 2026-11-10.
- **T037** `src/LexScholar.Infrastructure/VerifiedDocumentContent.fs`:
  - `openRead (dataDirectory) : OpenVerifiedContent` replaces the parameterless stub.
  - It looks up the `LibraryDocumentRow` by Id with one `AsNoTracking` query. No row: `Error DocumentNotFound`. `LibraryRepository.fs` is unchanged.
  - It opens `originals/<StorageKey>` with `FileMode.Open, FileAccess.Read, FileShare.Read`. The storage key comes only from the record.
  - If opening throws `IOException` (missing file or directory, other I/O failure) or `UnauthorizedAccessException` (unreadable), it returns `Error DocumentUnavailable`.
  - If the opened handle's `Length` differs from `ByteLength`, it disposes the stream and returns `Error DocumentUnavailable`. The length is read from the same handle that is served, not from a separate `stat`.
  - Otherwise it returns `Ok { Stream; ByteLength; OriginalFileName }`, and the caller owns the stream.
  - No hashing, no PdfPig, and no writes to SQLite or the filesystem.

### Tests

**T035** `tests/LexScholar.Infrastructure.Tests/ContentAvailabilityTests.fs` (11 cases; real SQLite and filesystem in a temporary directory). Each test imports `valid.pdf` through the real `ImportDocument` workflow. It then inserts a workspace, a project and a `FocusedSource` association with raw SQL, because no association repository exists yet. "Unchanged" means every column of every `LibraryDocuments` and `ProjectDocuments` row is equal before and after.

| Test | Cases |
|---|---|
| an available original opens read-only with its exact bytes and metadata | 1: `Ok`, `CanWrite = false`, `ByteLength` and `OriginalFileName` from the record, bytes equal to the fixture |
| stored originals are read-only | 1: mode `UserRead` (0400), `File.OpenWrite` throws `UnauthorizedAccessException`, bytes unchanged |
| an unavailable original is reported with its record and associations unchanged | 4: missing (deleted), unreadable (mode 000), shorter (truncated to 700 of 753 bytes), longer (one byte appended) |
| an unavailable original is still reported after restart with its record and associations unchanged | 4, same conditions: checked before and after a restart (`WorkspaceDb.initialize` and `StorageCleanup.run` on the same directory, as the host does before serving), then a fresh `openRead` |
| an unknown UUID is not found | 1: `Error DocumentNotFound`, rows unchanged |

Same-length changes are not tested. They are not detected by design (see Limitations), and the lead decided not to pin that limitation as required behavior.

Existing tests changed:
- `tests/LexScholar.Application.Tests/PortsTests.fs`: "caller streams verified content and owns the returned handle" (T011) wraps its content in `Ok` and unwraps the result. Its assertions are unchanged.
- `tests/LexScholar.Server.Tests/PlaceholderSmoke.fs`: removed the `VerifiedDocumentContent.openRead` entry, since it is no longer a throwing stub.

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, Microsoft.EntityFrameworkCore.Sqlite 10.0.14, xunit.v3 4.0.2. Tests ran as uid 1000 (`thyvini`, not root). Temporary test directories are under `/tmp`.

## RED

Commit `5bc8e34`. It contains the new signature and a stub that returns `Error DocumentNotFound` for every UUID, so the tests start and fail on assertions, not exceptions.

```sh
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --project tests/LexScholar.Infrastructure.Tests/LexScholar.Infrastructure.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T035'
```

11 total, 9 failed, 2 succeeded, 0 skipped.

- 8 unavailable cases (4 conditions, before and after restart): `Expected DocumentUnavailable, got DocumentNotFound.`
- Available original: `Expected the content to open, got DocumentNotFound.`
- Two tests passed at RED, as expected:
  - "stored originals are read-only" checks behavior that `DocumentStore.promote` (T026) already provides. It does not call `openRead`.
  - "an unknown UUID is not found" passes because `DocumentNotFound` is the stub's only answer.

  The available-original test fails without a real lookup, and the unavailable tests fail if every UUID is treated as unknown.

## GREEN

Commit `08061d0`. T035 filter: 11/11 passed, 0 skipped. Build: 0 warnings, 0 errors.

Sensitivity checks (temporary edits to `VerifiedDocumentContent.fs`, not committed; the file was restored and the solution rebuilt afterwards):
- **Length check removed** (`when false`): 4 failed (shorter and longer, before and after restart); 7 passed.
- **Open-failure handling narrowed** to `DirectoryNotFoundException` only: 4 failed (missing and unreadable, before and after restart; the exceptions escaped); 7 passed.

## Final verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

Restore and build succeeded with 0 warnings and 0 errors. The full suite passed 265/265 (254 previously + 11 new T035 cases), 0 skipped, browser journeys included. The T011 ports tests still pass. No temporary test directories remained under `/tmp/lexscholar-schema-tests`.

## Limitations

- **Same-length changes are not detected.** This is by design (research.md, "Document availability on open"). A replacement of the original with the same number of bytes, made outside LexScholar, is served as is. The stored SHA-256 is used only for duplicate detection.
- **Between open and delivery.** The length is checked once when the handle is opened. A change made while the stream is read, or between range requests, is not detected (on Linux, .NET file locks are advisory).
- **Unreadable case needs a non-root user.** Root can read a mode-000 file. The unreadable case asserts the user is not root, with a clear message, instead of skipping.
- **Other I/O failures** that throw while opening are also reported as `DocumentUnavailable`. Failures while later reading the returned stream belong to the caller (T038).
- HTTP mapping (404/503, problem bodies, ranges) is not covered here; it is T034/T038.
