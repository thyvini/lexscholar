# T021, T022, T026, T028, T029 import staging, duplicate reuse and startup cleanup

## Scope

- **Ports and types** (`src/LexScholar.Application/Contracts.fs`, `Ports.fs`). Removed `PendingImport`, `PendingImportStatus` and `ImportJournalOperations`, which belonged to the journal design rejected on 2026-11-09 (research.md, "Import staging and startup cleanup"). Added `StagingKey = StagingKey of Guid`. The workflow generates it, and the store derives the file name from the UUID alone. `OpenStagedWrite`, `OpenStagedRead`, `PromoteStagedContent` and `DiscardStagedContent` now take a `StagingKey`. Promotion returns the storage key it generated, and discard succeeds if the file is already gone. New port types: `OpenStoredOriginal` (read-only, for the byte comparison), `FindDocumentsBySha256` and `InsertDocument`. `evidence/t011-storage-ports.md` still describes the old `PendingImport` signatures; it is left as the historical record.
- **T026** `src/LexScholar.Infrastructure/DocumentStore.fs` (new; no stub existed):
  - layout: `staging/`, `originals/` and `quarantine/` under the data directory, mode 0700;
  - staging write: `staging/<uuid N>`, `CreateNew`, mode 0600;
  - `promote`:
    1. opens the staged file for writing (without truncating) and calls `Flush(true)`;
    2. makes it read-only (0400);
    3. moves it to `originals/<new uuid N>` with `File.Move(…, overwrite: false)`. On Linux, .NET implements this as link plus unlink, so an existing destination is never replaced.
  - No directory sync, as decided in research.md.
  - `openOriginal` accepts only a 32-character lowercase hex key. Client filenames never reach a path.
- **T028** `src/LexScholar.Infrastructure/LibraryRepository.fs`: SHA-256 candidate lookup, row insert, and the set of referenced storage keys. `src/LexScholar.Infrastructure/StorageCleanup.fs` replaces `ImportJournal.fs`:
  - it ensures the directories exist;
  - it deletes everything in `staging/`;
  - it moves every file in `originals/` that no row references to `quarantine/` (no overwrite, never deleted);
  - it returns `{ DeletedStagingFiles; Quarantined }` (storage keys, not paths).

  `Program.createHost` runs it after the lock, migrations and `builder.Build()`, and before security, endpoints and serving. It logs one warning per quarantined key (`Moved unreferenced original {StorageKey} to quarantine.`, with no absolute path). The step sits inside the existing `try`, so a failure disposes the app and releases the lock. There is no report UI or endpoint.
- **T029** `src/LexScholar.Application/ImportDocument.fs`:
  - `create : ImportDependencies -> Import` allocates one `SemaphoreSlim(1, 1)` in its closure. The composition root calls it once per host; endpoint wiring is T030/T031.
  - Each import:
    1. streams to staging, stopping after `MaxBytes + 1` bytes (no hashing);
    2. validates the staged file with the current limits (the validator is the only place SHA-256 and length are computed);
    3. waits for the commit section, honoring cancellation while waiting but not inside;
    4. looks up SHA-256 candidates, skips those with a different `ByteLength`, and compares the rest byte for byte (`contentEquals`);
    5. reuses the match, or promotes the staged file and inserts the row.
  - The staged file is discarded after every outcome: reuse, rejection, exception or cancellation. Exceptions are rethrown with their original stack.
  - `OriginalFileName` is a display-only basename: the last `/` or `\` segment, without control characters, trimmed, or `document.pdf` if nothing is left.
  - PR #15 follow-ups:
    - `LibraryDocument.create` now takes a `LibraryDocument` record, so every field is named at the call site. A separate input record with the same labels would bring back the duplicate-label inference trap.
    - `PdfValidator` re-raises `IOException` and `UnauthorizedAccessException` instead of reporting `Corrupt`. A reflection probe of PdfPig 0.1.16 showed that none of its six exception types derive from `IOException`.

### Tests

- **T021** `tests/LexScholar.Infrastructure.Tests/LibraryTests.fs` (29 cases; real SQLite and filesystem in a temporary directory). Each successful import checks the stored original: mode 0400, a generated 32-hex name, and exact bytes.

  | Flow step | Test |
  |---|---|
  | promote and insert | a new PDF is stored as one read-only original and one document row |
  | reuse | identical bytes reuse the existing document; identical bytes under a different filename reuse it (first filename kept) |
  | distinct | different bytes produce a distinct document even under the same filename |
  | byte comparison (direct) | 7 cases on 200,000-byte inputs: identical, last/first byte differs, either side shorter, both empty, one empty |
  | rejection | 5 cases (non-PDF, corrupt, encrypted, over-bytes, over-pages): rows, originals (bytes and mode), associations and staging unchanged |
  | re-import over current limits | 2 cases (over-bytes, over-pages) on bytes already in the library: no changes |
  | staging cap | an oversized upload is read and staged only up to `MaxBytes + 1` bytes (exactly 1,001 read) |
  | serialized commit | 8 concurrent imports of identical bytes, alternating two filenames, with an existing document and association: all succeed with the same document, exactly one `Reused = false`, one row and one original for those bytes, associations intact, staging empty |
  | failure paths | a validator that throws `IOException` (passed through the dependency record) propagates with staging empty; an import cancelled mid-staging discards its file |
  | filename | 7 sanitizer cases |

- **T022** `tests/LexScholar.Infrastructure.Tests/StorageCleanupTests.fs` (5 tests; post-crash states set up directly):
  - leftover staging files, including a read-only one, are deleted;
  - an unreferenced original is quarantined with identical bytes and reported;
  - referenced originals (bytes and mode) and rows are unchanged;
  - missing directories are created with mode 0700;
  - a second run changes nothing.

  `tests/LexScholar.Server.Tests/WorkspaceStartupTests.fs` adds "startup cleanup finishes before serving and logs each quarantined original as a warning". It uses a capturing `ILoggerProvider` and `Program.create` without starting the host. The task names only `StorageCleanupTests.fs`, but Infrastructure.Tests cannot reference `Program`, so this one test lives in Server.Tests.
- **T029** `PdfValidationTests.fs` (2 tests):
  - an I/O failure while PdfPig parses (a stream that fails every read after the measuring pass) propagates as `IOException`;
  - a PDF truncated mid-document is still `Corrupt` (a guard that the narrower catch lets no parse failure through).

Existing tests changed:
- `PortsTests.fs`: 4 staging tests use `StagingKey`, and "journal promotion and staging cleanup remain separately injectable" became "promotion and staging cleanup remain separately injectable", with promote returning a key. All still T011.
- `PlaceholderSmoke.fs`: dropped the journal stub and its four operations.
- `DocumentTests.fs`: only the `create` helper changed to the named-record call; the 15 cases are unchanged.

Scope decisions (2026-11-10, project owner):
- T021's "final-association retention" moves to T030, which implements association removal. T021 checks that reuse, rejection and concurrent imports leave existing associations intact. tasks.md T021 and T030 are reworded to match.
- If the row insert fails after promotion, the error propagates and the unreferenced original stays in `originals/` until the next startup quarantines it. There is no capability to delete a just-promoted original. This is a small, deliberate deviation from "add no … stored originals" for a rare SQLite failure.

Deviation from the approved plan: the plan placed the fsync at the end of staging. It is in `promote` instead, so rejected uploads are not fsynced, and the flush, read-only and move steps sit together in `DocumentStore.fs` as T026 describes.

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, Microsoft.EntityFrameworkCore.Sqlite 10.0.14, PdfPig 0.1.16. Tests ran as uid 1000 (not root). Temporary test directories are under `/tmp`, which is tmpfs (hard links supported).

## RED

Commit `869aeb5`. The tests ran against stubs that don't throw:
- `ImportDocument.create` returned `Error Corrupt` without touching storage;
- `contentEquals` always returned `false`, and `sanitizeFileName` returned its input;
- the store wrote to memory streams and never promoted;
- the repository returned no rows;
- `StorageCleanup.run` did nothing;
- `Program` did not call cleanup;
- the validator still turned I/O failures into `Corrupt`.

```sh
dotnet build LexScholar.sln -c Release -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

254 total, 30 failed, 224 succeeded, 0 skipped. Every failure was a new test:

| Task | Failed | Passed | How they failed |
|---|---|---|---|
| T021 | 23 | 6 | "Expected the import to succeed, got Corrupt." (12); `Expected: Error (TooLarge 1000L)` / `Error NotPdf` … `Actual: Error Corrupt`; `Assert.Throws() Failure: No exception was thrown` for the `IOException` and cancellation tests; `Assert.Equal() Failure` for the identical and both-empty comparisons and 6 sanitizer cases. |
| T022 | 6 | 0 | `Expected: 2` deleted staging files, `Collections differ` for quarantined keys, missing directories, and the host test finding the staging file still present. |
| T029 | 1 | 1 | `Assert.Throws() Failure`: expected `IOException`, got `Error Corrupt`. |

Seven tests passed trivially against the stubs: the five "bytes differ" comparison cases (the stub always answered `false`), the sanitizer case whose input is already a basename, and the truncated-PDF guard (the old catch-all already returned `Corrupt`). The sensitivity checks below show that the comparison and staging behavior can fail.

## GREEN

Commit `70b2b2c`. Task filters: T021 29/29, T022 6/6 (5 Infrastructure + 1 Server), T029 2/2, and the existing T024 15/15, T020 15/15 and T011 9/9.

Sensitivity checks (temporary, not committed; files restored and the solution rebuilt afterwards):
- **Commit section removed** (no `WaitAsync`/`Release`): the concurrent test failed in 4 of 5 consecutive T021 runs and passed once. It detects a missing section when imports overlap, but overlap is not guaranteed (see Limitations).
- **Discard skipped**: 13 T021 tests failed on non-empty staging.
- **`IOException` re-raise removed**: the T029 I/O test failed; the truncated-PDF guard still passed.
- **Read-only chmod removed from `promote`**: 10 T021 tests failed on the stored original's mode.
- **Cleanup deleting orphans instead of quarantining**: 2 T022 tests failed.

## Final verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

Restore and build succeeded with 0 warnings and 0 errors. The full suite passed 254/254 (217 previously + 37 new: 29 T021, 6 T022, 2 T029), 0 skipped, browser journeys included. `HostSecurityTests` writes `staging/draft.txt` after the host is built, so cleanup cannot interfere, and its static-exposure test still passes. No temporary test directories remained under `/tmp/lexscholar-tests` or `/tmp/lexscholar-schema-tests`.

## Limitations

- **Running as root (analyze finding U1).** Root bypasses permission bits, so the tests check mode bits (`File.GetUnixFileMode = UserRead`, directories 0700) and never attempt a write that should be denied. Fault tests use throwing streams and dependencies, not `chmod`. Nothing depends on the uid, but write denial on read-only originals is not exercised.
- **Durability.** `Flush(true)` is called before the move, but fsync and power-loss behavior can't be observed in tests, and `strace` was not available to confirm the syscall. Per research.md, there is no directory sync, so a power loss can rarely undo a rename. That case is handled as 503 `document_unavailable` (T037).
- **No-overwrite.** `promote` generates the destination key itself, so a collision can't be set up without a test-only seam. The guarantee rests on `File.Move(…, overwrite: false)` and is untested. With one process and the commit section, a collision would need a repeated random UUID.
- **Concurrency timing.** `Task.WhenAll` makes overlap likely but not guaranteed: without the section, the test passed 1 run in 5. Correctness with the section does not depend on timing.
- **Unreadable candidate.** If a same-hash, same-length candidate's original is missing or unreadable, the import fails (staging discarded) rather than treating it as "no match", which could create a duplicate record.
- **Insert failure after promotion** leaves an unreferenced original until the next startup quarantines it (see Scope decisions).
- **Discard failure.** If deleting the staged file itself throws, that exception replaces the import's original error. The staged file is then removed at the next startup.
