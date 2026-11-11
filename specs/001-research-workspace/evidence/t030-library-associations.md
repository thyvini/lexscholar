# T030 library listing and project associations

## Scope

- **`src/LexScholar.Application/Library.fs`** follows the `Projects` precedent: a `Store` record of plain storage functions, defined in the module, with no changes to `Ports.fs` or `Contracts.fs`.
  - Error unions: `AssociationError = ProjectNotFound | DocumentNotFound | DuplicateAssociation` and `RemovalError = AssociationNotFound`. Both are `[<RequireQualifiedAccess>]`, so they can't clash with `ContentFailure.DocumentNotFound` from PR #19.
  - Workflows:
    - `list` returns every library document;
    - `listForProject` returns a project's associations;
    - `associate` sets `AddedUtc` to the current UTC time;
    - `remove` deletes one association.
  - Results use the existing contract types, `Document` and `ProjectDocumentView`. `contentUrl` builds `/api/documents/{uuid}/content` from the document UUID only.
  - `Library.unimplemented` and `LibraryOperations` are unchanged; `PlaceholderSmoke` and `PortsTests` still use them.
  - Changing a role is T036. The HTTP endpoints are T031, preceded by T023's contract tests (`feat/f001-library-endpoints`).
- **`src/LexScholar.Infrastructure/LibraryRepository.fs`**:
  - `list`: all documents, ordered by `ImportedUtc` then `Id`. The spec sets no order; this one is only deterministic. Sorting happens in memory, because `DateTimeOffset` is stored as text through a converter.
  - `listForProject`: returns `ProjectNotFound` for an unknown project. Otherwise it returns the project's associations with their documents, ordered by `AddedUtc` then document id.
  - `associate`: checks that the project exists, then the document. It inserts the row, with the role stored as the `FocusedSource`/`ReferenceSource` text the CHECK constraint allows. A primary-key violation (`SQLITE_CONSTRAINT_PRIMARYKEY`, extended code 1555) becomes `DuplicateAssociation`, so a concurrent duplicate is mapped the same way and the existing role is never replaced. This is the same pattern `ProjectRepository.create` uses for the unique-name conflict.
  - `removeAssociation`: `ExecuteDelete` on exactly one `(ProjectId, DocumentId)` row. It never touches `LibraryDocuments` or files. When no row was deleted it returns `AssociationNotFound`.
  - Associations are scoped by project existence only. There is one workspace per database (`ProjectRepository.resolveWorkspace` rejects more).

### Tests

The 8 tests are in `tests/LexScholar.Infrastructure.Tests/LibraryTests.fs`, under Trait `Task=T030`. They use real SQLite and the real filesystem in a temporary directory. Documents come from the real import workflow, and projects are seeded with SQL.

| Test | Covers |
|---|---|
| the library lists every document, including unassociated ones | listing order and every `Document` field, including `ContentUrl` |
| one document associated with two projects keeps each project's own role | US2 scenario 3: each project's list shows its own role and the same `Document`; one row |
| a duplicate association is rejected and keeps the existing role | `DuplicateAssociation`; the original role and `AddedUtc` are unchanged |
| associating an unknown project or document is rejected without changes | `ProjectNotFound`, `DocumentNotFound`; no association rows |
| removing one association keeps the document, its original and other projects' associations | FR-010, SC-004; the stored original's bytes and mode 0400 |
| removing a document's final association keeps its row and stored original | the test moved from T021: the row, the single original, the library listing, and association with another project afterwards |
| removing a missing association is rejected and changes nothing | `AssociationNotFound` for an unknown project-document pair and an unknown document |
| listing an unknown project is rejected and an empty project lists nothing | `ProjectNotFound`; `Ok []` |

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, Microsoft.EntityFrameworkCore.Sqlite 10.0.14. Tests ran as uid 1000, with temporary directories under `/tmp` (tmpfs). The base was `release/0.1.0` at `fa32db6`.

## RED

Commit `ad9e729`. In this commit the `Library.fs` workflows run over no-op repository stubs: `list` returns `[]`, `listForProject` returns `Ok []`, `associate` returns `Error DocumentNotFound` without writing, and `removeAssociation` returns `Error AssociationNotFound`.

```sh
dotnet build LexScholar.sln -c Release -warnaserror
dotnet test --project tests/LexScholar.Infrastructure.Tests/LexScholar.Infrastructure.Tests.fsproj -c Release --no-build --filter-trait 'Task=T030'
```

8 total, 8 failed, 0 succeeded.
- 6 tests failed when the test helper reported the stub's result for an association that should have succeeded: "Expected the association to succeed, got DocumentNotFound."
- "associating an unknown project or document…" failed with `Expected: Error ProjectNotFound`, `Actual: Error DocumentNotFound`.
- "listing an unknown project…" failed with `Expected: Error ProjectNotFound`, `Actual: Ok []`.

Some assertions would pass against the stubs, because the stubs return the expected error: the unknown-document case and both missing-removal cases. In each of those tests an earlier assertion fails first. The sensitivity check below shows that the removal assertions can fail.

## GREEN

Commit `03aff33`. T030 passed 8/8, and the build had 0 warnings.

Sensitivity check, a temporary edit that was not committed (the file was restored and the solution rebuilt): I dropped the `ProjectId` condition from `removeAssociation`, so it deleted the document's associations in every project. Two tests then failed: "removing one association keeps … other projects' associations" and "removing a missing association is rejected and changes nothing". The other 6 passed.

## Final verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

Restore and build succeeded with 0 warnings and 0 errors. The full suite passed 276/276 (268 at `fa32db6` plus the 8 new T030 tests), 0 skipped, with the browser tests included. No temporary test directories remained under `/tmp/lexscholar-tests` or `/tmp/lexscholar-schema-tests`.

## Limitations

- **Concurrent duplicate associations.** These are mapped from the primary-key violation, but no test forces two inserts to race. The duplicate test covers the same code path sequentially, because the existence checks don't detect a duplicate pair, so the insert reaches the constraint.
- **Not reachable over HTTP yet.** These workflows have no endpoints until T031. The HTTP status codes and problem codes (`not_found`, `document_not_found`, `duplicate_association`) belong to T023/T031.
