# T020, T024, T025, T027 document types, schema and PDF validation

## Scope

- **T024** `src/LexScholar.Domain/Documents.fs`: `DocumentRole` (moved from `Application/Contracts.fs`), `LibraryDocument`, `ProjectDocument` and `LibraryDocument.create`. The constructor requires a 64-character lowercase hex SHA-256, a non-blank storage key, and positive byte length and page count. It takes no import limits, so documents above today's limits stay valid. Role validity is enforced by the union type. Tests: `tests/LexScholar.Domain.Tests/DocumentTests.fs`, one test per rule (15 cases).
- **T025** `WorkspaceDb.fs`, `Migrations/Documents.fs` (`20261111000000_Documents`, handwritten SQL) and the model snapshot:
  - `LibraryDocuments`: UUID text key, unique `StorageKey`, non-unique `Sha256` index, `CHECK (ByteLength > 0)` and `CHECK (PageCount > 0)`.
  - `ProjectDocuments`: primary key `(ProjectId, DocumentId)`, `CHECK (Role IN ('FocusedSource', 'ReferenceSource'))`, restricting foreign keys to `ResearchProjects` and `LibraryDocuments`, and a `DocumentId` index.
  - Tests: `tests/LexScholar.Infrastructure.Tests/DocumentSchemaTests.fs` (10 tests): fresh migration with the snapshot matching the model, upgrade from `Projects` keeping projects, idempotent initialization, EF/SQLite round trip (including a byte length above Int32), each constraint, the delete restrictions, association removal keeping the document, and rollback to `Projects`.
- **T027** `src/LexScholar.Infrastructure/PdfValidator.fs`. `ValidatePdf` now returns `Result<ValidatedPdf, PdfRejection>`, where `PdfRejection = NotPdf | Corrupt | Encrypted | TooLarge of maxBytes | TooManyPages of maxPages`. The validator:
  1. requires a seekable stream and reads it from the start;
  2. counts and hashes (SHA-256) at most `MaxBytes + 1` bytes, giving `TooLarge` past the limit;
  3. checks the `%PDF-` signature (`NotPdf`, including empty input);
  4. opens the stream with PdfPig (`PdfDocumentEncryptedException` gives `Encrypted`, any other parse failure gives `Corrupt`);
  5. applies the page limit (`TooManyPages`).

  It never disposes or keeps the caller's stream. Mapping to contract error codes is left to T031.
- **T020** `tests/LexScholar.Infrastructure.Tests/PdfValidationTests.fs` (15 tests) against the real validator and `tests/Fixtures/` (now copied to the test output):
  - fixtures: valid, distinct, not-a-PDF, empty, corrupt, encrypted;
  - limits: byte limit exact (753) and one over (752), page limit exact (2) and one over (1);
  - exactly `MaxBytes + 1` bytes read for an over-limit input;
  - reads from the start whatever the input position;
  - input left open after acceptance and rejection;
  - non-seekable input refused;
  - cancellation.

  Non-mutating rejection is tested at the import level (T021), per research.md, "Validator and test placement".

Related changes:
- The Application DTO `ProjectDocument` was renamed `ProjectDocumentView`, so two records with that name are never in scope together. It still serializes as the `ProjectDocument` schema in `contracts/http-api.yaml`, which is unchanged.
- The T011 placeholder list no longer includes the validator.
- The SQLite helpers shared by the schema tests moved to `SchemaTestSupport.fs`.
- The T010/T014/T015 tests that list the applied migrations or count history rows now include `Documents`.

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, Microsoft.EntityFrameworkCore.Sqlite 10.0.14, PdfPig 0.1.16.

PdfPig probe on the fixtures, run before implementation, with both `UseLenientParsing = true` and `false`:
- `valid.pdf` and `distinct.pdf` open with 2 pages each;
- `corrupt.pdf` throws `PdfDocumentFormatException` ("Could not find any xref tables or streams");
- `encrypted.pdf` throws `PdfDocumentEncryptedException`;
- `not-a-pdf.txt` throws `PdfDocumentFormatException` (no version header).

The default parsing options are therefore used.

## RED

Commit `64bf54b`: tests against stubs with no behavior.
- `LibraryDocument.create` always returned `Ok`.
- The validator returned `Ok { Sha256 = ""; ByteLength = 0L; PageCount = 0 }` without reading its input.
- The document row types existed without EF mapping or a migration.

```sh
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

217 total, 37 failed, 180 succeeded, 0 skipped. Every failure was a new test:

| Task | Failed | Passed | How they failed |
|---|---|---|---|
| T024 | 13 | 2 | Each `Error InvalidSha256` / `InvalidStorageKey` / `NonPositiveByteLength` / `NonPositivePageCount` case got `Ok`. |
| T020 | 14 | 1 | Value mismatches such as `Expected: Error Corrupt` / `Actual: Ok { Sha256 = "" … }`. The non-seekable and cancellation tests failed with "No exception was thrown". |
| T025 | 10 | 0 | Each failed on "T025 requires the LibraryDocuments table in the migrated SQLite schema." |

Three tests passed trivially against the stubs: T024's "valid fields are created unchanged" and "documents above the default import limits remain valid", and T020's "input left open".

An earlier RED attempt mapped the new entities in EF without a migration. `Migrate()` then threw `PendingModelChangesWarning` for every database and host test (112 failures), so the mapping was moved to GREEN.

## GREEN

Commit `05d6e46`. The task filters passed: T024 15/15, T020 15/15, T025 10/10, and T015 8/8 after the migration-list update.

Sensitivity checks (temporary, not committed; files restored afterwards):
- Disposing the input inside the validator failed "validation leaves the caller-owned input open" (`valid.pdf: input was disposed.`). This shows the test can fail even though it passed against the RED stub.
- Removing the role CHECK constraint from the snapshot alone made every T025 test fail: `Migrate()` refuses the pending model change. This shows the snapshot and model are compared.

## Final verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

Restore and build succeeded with zero warnings. The full suite passed 217/217 (177 previously + 40 new: 15 T024, 15 T020, 10 T025), zero skipped, browser journeys included. No temporary test directories remained after the run. Nine empty directories left in `/tmp/lexscholar-project-tests` by the discarded RED attempt were removed.

## Limitations

- The validator accepts any PDF that PdfPig opens. It does not render pages or scan content (plan.md, "A PDF signature is not proof of a valid or safe PDF").
- SHA-256 format is enforced by the domain constructor, not by a SQL CHECK; T025 does not ask for one.
- The domain constructor does not validate `OriginalFileName`; sanitizing it belongs to import (T029).
