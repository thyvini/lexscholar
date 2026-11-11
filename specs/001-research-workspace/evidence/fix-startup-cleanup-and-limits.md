# Fix: interrupted startup cleanup and the largest byte limit (follow-ups to T027, T028)

## Scope

Two Greptile findings on code already on `release/0.1.0`:

- **PR #16, P1, "Interrupted cleanup blocks startup"** (`src/LexScholar.Infrastructure/StorageCleanup.fs`). On Linux, `File.Move(…, overwrite: false)` links the destination and then unlinks the source. A stop between the two steps leaves the same file under both `originals/<key>` and `quarantine/<key>`. The next startup's move then threw `IOException`, and every later startup did the same.
  - If `quarantine/<key>` already exists with the same bytes, cleanup deletes `originals/<key>`, which completes the interrupted move. The key is reported in `Quarantined` and logged with the existing warning.
  - If `quarantine/<key>` holds different bytes, both files are left untouched and the key is reported in a new `CleanupReport.Conflicting` list. `Program.createHost` logs one warning per conflicting key with the same logger and style as the existing warning: `Left unreferenced original {StorageKey} in place: quarantine already holds different content under that key.` Storage keys are fresh UUIDs, so the app itself can't produce this state. The lead approved this "leave both" option on 2026-11-10.
  - The byte comparison reuses the existing `ImportDocument.contentEquals`. No new helper was added.
- **PR #15, P2, "Largest byte limit rejects PDFs"** (`src/LexScholar.Infrastructure/PdfValidator.fs`). `Configuration.load` accepts `MaxBytes = Int64.MaxValue`, and there `maxBytes + 1L - total` wrapped to a negative value. The first read asked for 0 bytes, so every PDF was rejected as `NotPdf`. The read size is now `min (buffer.Length - 1) (maxBytes - total) + 1`. The loop guard `total <= maxBytes` keeps `maxBytes - total` non-negative, so neither step overflows. `ImportDocument.fs` already guards the staging cap for `Int64.MaxValue`, so it is unchanged.

### Tests

- `tests/LexScholar.Infrastructure.Tests/StorageCleanupTests.fs` (Trait `Task=T022`, states set up directly with no fault-injection hooks):
  - "an interrupted quarantine move is completed on the next startup": the same bytes under the orphan key in both `originals/` and `quarantine/`. Cleanup does not throw, `originals/` ends up empty, `quarantine/` holds the bytes, and the report lists the key as quarantined, with no conflicts.
  - "a different file already in quarantine under the same key keeps both files": different bytes in each directory. Cleanup does not throw, both files keep their bytes, the key is not reported as quarantined, and it is reported as conflicting.
- `tests/LexScholar.Infrastructure.Tests/PdfValidationTests.fs` (Trait `Task=T020`): "a valid PDF is accepted at the largest configurable byte limit". `valid.pdf` with `MaxBytes = Int64.MaxValue` gives the same result as with the default limits.

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, PdfPig 0.1.16. Tests ran as uid 1000 (not root), with temporary directories under `/tmp` (tmpfs).

## RED

Commit `881a12f`. That commit adds the three tests and the `Conflicting` field to `CleanupReport`, always empty, so that the tests compile.

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --project tests/LexScholar.Infrastructure.Tests/LexScholar.Infrastructure.Tests.fsproj -c Release --no-build
```

The run had 95 tests: 3 failed, 92 succeeded and 0 were skipped. Each failure was one of the new tests:

| Test | Failure |
|---|---|
| a valid PDF is accepted at the largest configurable byte limit | `Expected: Ok { Sha256 = "0fcd00dc…"; ByteLength = 753L; PageCount = 2 }`, `Actual: Error NotPdf` |
| an interrupted quarantine move is completed on the next startup | `System.IO.IOException : The file '/tmp/lexscholar-schema-tests/<dir>/quarantine/<key>' already exists.` |
| a different file already in quarantine under the same key keeps both files | `System.IO.IOException : The file '/tmp/lexscholar-schema-tests/<dir>/quarantine/<key>' already exists.` |

The two cleanup failures come from the exception that blocked startup in production. They are runtime behavior, not compile errors.

## GREEN

Commit `7d516da`. Build: 0 warnings and 0 errors. Results by task filter: T022 passed 7/7 and T020 passed 16/16.

Sensitivity check (a temporary edit, not committed; the file was restored and the solution rebuilt afterwards): I replaced the content comparison with `true`, which deletes the original whenever the destination exists. "a different file already in quarantine under the same key keeps both files" then failed, and the other 6 T022 tests passed.

## Final verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

Restore and build succeeded with 0 warnings and 0 errors. The full suite passed 257/257 with 0 skipped, browser tests included. That is the 254 tests from before plus the 3 new ones. No temporary test directories were left under `/tmp/lexscholar-tests` or `/tmp/lexscholar-schema-tests`.

## Limitations

- **Copies, not a hard link.** A real interrupted move leaves one inode with two names. The test writes two separate files with identical bytes instead, because .NET has no hard-link API without P/Invoke. Cleanup compares bytes, not inodes, so both states take the same path. Deleting `originals/<key>` removes only that name, and the bytes remain in `quarantine/`.
- **The conflict warning is not covered by a test.** `WorkspaceStartupTests` covers the existing quarantine warning. The new `Conflicting` warning in `Program.createHost` is a two-line loop over the report and has no host-level test. The `Conflicting` report itself is covered.
- **Cost of the comparison.** The comparison reads both files in full, but only when a quarantine name already exists, which happens only after an interrupted cleanup.
