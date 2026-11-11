# T044 SC-002 import corpus

## Scope

- **Corpus**: 100 openly licensed arXiv PDFs (CC BY 4.0 or CC0 1.0). The user decided on 2026-11-10 that agents download the PDFs and never commit them; the lead approved the plan on 2026-11-11. The full selection, the corpus table and the result are in [verification.md](../verification.md#sc-002-import-corpus).
- **New files**:
  - `specs/001-research-workspace/sc002-corpus.csv`, the manifest. It holds metadata only: arXiv id and version, set, PDF URL, license name and URL, bytes, `pdfinfo` page count and SHA-256.
  - `scripts/fetch-sc002-corpus.sh`, the tool actually used for every download. It reads the manifest, builds file names and URLs from validated identifiers only (`^[0-9]{4}\.[0-9]{4,5}$`, `^v[0-9]+$`), and downloads over HTTPS with a 3-second gap, a generic user agent and `--max-filesize 100000000`. It skips files already present and verifies each SHA-256; rows with an empty hash print the computed one instead. It only downloads and hashes; it never opens or runs a PDF.
  - `tests/LexScholar.Corpus.Tests/` (project plus `CorpusImportTests.fs`, one test, Trait `Task=T044`), deliberately not added to `LexScholar.sln`. It reads the manifest, which is copied to the output directory, and the corpus directory named by `LEXSCHOLAR_SC002_CORPUS`. It:
    - asserts the manifest has 100 entries and that every file has the manifest's size and SHA-256;
    - imports each file through `ImportDocument.create` with the real `PdfValidator`, `DocumentStore` and `LibraryRepository` and the default limits, into one temporary workspace;
    - writes `results.csv` (name, bytes, SHA-256, manifest and imported page counts, outcome; for exceptions, the type name only);
    - asserts at least 99 were imported. Without the variable it fails with instructions; it never skips.
  - `specs/001-research-workspace/verification.md`, with the SC-002 section and two placeholder sections marked "not yet recorded": automated SC-003–005/007–008 results, and the usability evaluations (SC-001/006).
- **`.gitignore`**: `/corpus/`. Every `git status` during the work, including `--untracked-files=all`, showed no PDF.
- **`research.md`**: new decision "SC-002 import corpus" (Constitution VI), covering the source, the independent validity tools and the out-of-solution project, with the rejected alternatives: `Explicit` tests counted as skipped, a heavy `.fsx`, and a `Category` trait that would pass silently.
- **`tasks.md`**: T044 now names the explicit corpus command and notes that the corpus is not committed. The lead approved this change; the spec is unchanged.

No product code changed.

## Selection, as run

1. **Metadata:** five OAI-PMH `ListRecords` requests (`metadataPrefix=arXivRaw`, sets `cs`, `math`, `physics`, `q-bio`, `stat`, datestamp 2024-03-04), 3 seconds apart. They returned 321, 166, 265, 13 and 46 records.
2. **Candidates:** a scratchpad script (not committed; it reads metadata XML only) applied the license rule, latest version, non-zero size and per-set quotas, giving 118 candidates.
3. **Download:** `scripts/fetch-sc002-corpus.sh <candidates> corpus/sc002`. A background start was stopped after 16 files and resumed in the foreground; already-present files were skipped and partial `.part` files removed. All 118 downloads succeeded.
4. **Membership:** `qpdf --check` and `pdfinfo` were run on each file, with paths passed as arguments from outside the PDF directory:
   - 114 passed with qpdf exit 0; 4 had qpdf warnings (exit 3);
   - none was encrypted, none was over the limits, and there were no duplicate hashes.
5. **Selection:**
   - A first ordered pick by set left `stat` out entirely, so I re-ran it round-robin across the sets before LexScholar processed any file.
   - That gave 100 eligible files, plus 14 spares and the 4 exclusions listed in verification.md.
   - The non-selected files were deleted from `corpus/sc002/pdfs/`.
   - Running the fetch script on the final manifest gave 100 `OK` (all hashes verified) and exit 0.

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, PdfPig 0.1.16, Microsoft.EntityFrameworkCore.Sqlite 10.0.14, xunit.v3 4.0.2, qpdf 12.4.2, poppler `pdfinfo` 26.10.0, curl 8.23.0. Written on `4e131a9` and rebased onto `8210007`. Tests ran as uid 1000.

## RED

There is no product code to make GREEN. RED shows that the corpus test fails on its assertions when the corpus is missing or not the documented one.

```sh
dotnet restore tests/LexScholar.Corpus.Tests/LexScholar.Corpus.Tests.fsproj -warnaserror
dotnet build tests/LexScholar.Corpus.Tests/LexScholar.Corpus.Tests.fsproj -c Release --no-restore -warnaserror
env -u LEXSCHOLAR_SC002_CORPUS dotnet test --project tests/LexScholar.Corpus.Tests/LexScholar.Corpus.Tests.fsproj -c Release --no-build
LEXSCHOLAR_SC002_CORPUS=$PWD/corpus/sc002 dotnet test --project tests/LexScholar.Corpus.Tests/LexScholar.Corpus.Tests.fsproj -c Release --no-build
```

- **Variable unset:** exit 2, 1 failed, 0 skipped. The failure is `Assert.False` with "Set LEXSCHOLAR_SC002_CORPUS to the corpus directory downloaded by scripts/fetch-sc002-corpus.sh …".
- **Deliberately wrong manifest hash:** the first entry's SHA-256 (2107.01815v1) was temporarily set to 64 zeros and the project rebuilt. Exit 2, 1 failed: `Assert.Empty() Failure: Collection was not empty`, `["2107.01815v1: SHA-256 differs from the manifest"]`. The manifest was then restored, checked byte-identical with `diff`, and rebuilt. The wrong hash was never committed.

## Run

Same commands, with the correct manifest: exit 0, 1/1 passed, 0 skipped, in 4.5 s. `results.csv` shows **100 Imported**, with no rejections, reuses or failures. For every file, the page count LexScholar recorded equals `pdfinfo`'s count.

## Final verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

After rebasing onto `8210007` (release/0.1.0 after #22):
- Solution restore and build: 0 warnings, 0 errors.
- Full suite: 291/291 passed with 0 skipped, browser journeys included. The suite is unchanged by this task, because the corpus project is outside the solution.
- The corpus project, restored and built on its own with `-warnaserror`, also had 0 warnings. Its corpus run passed again: 1/1, 100 Imported.

## Limitations

- **Source variety:** the corpus is homogeneous (arXiv's TeX toolchain), well below the size and page limits. The four candidates with qpdf warnings were excluded by rule, not tested. See the limitations in verification.md.
- **Reproducibility over time:** re-running the same OAI query later can return different records, because datestamps change when papers are updated. The committed manifest, pinned to arXiv versions and checked by SHA-256, defines the corpus; the query only explains how it was chosen.
- **Not in the default suite:** the corpus run happens only when someone runs it. Nothing in the default suite or CI re-runs it automatically.
