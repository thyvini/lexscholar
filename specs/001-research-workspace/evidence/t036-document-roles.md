# T036 project-scoped document roles

Part of the `feat/f001-reader-roles` branch (T034, T036, T038), which becomes one PR once T038 is GREEN. HTTP wiring of role changes (PATCH) is T038. The T034 HTTP tests stay RED until then.

## Scope

- **New `src/LexScholar.Application/DocumentRoles.fs`**, compiled after `Library.fs`:
  - `[<RequireQualifiedAccess>] RoleChangeError = AssociationNotFound`. One case covers both an unknown project and a document that is not associated with the project; T034 expects 404 `not_found` for both.
  - `UpdateRole`, the one storage capability the workflow takes.
  - `changeRole update projectId documentId role : Result<ProjectDocumentView, RoleChangeError>`.

  Invalid role text is not handled here. `DocumentRole` is a closed union, so parsing it is part of T038's HTTP handling.
- **`src/LexScholar.Application/Library.fs`**: `associationView` is now public (it was `private`), so the role workflow reuses the association-to-view mapping. Nothing else in T030 changed, and `Library.Store` is unchanged.
- **`src/LexScholar.Infrastructure/LibraryRepository.fs`**, `changeRole dataDirectory : DocumentRoles.UpdateRole`:
  - finds the `ProjectDocumentRow` by its composite key `(ProjectId, DocumentId)` (`WorkspaceDb.fs` `HasKey("ProjectId", "DocumentId")`);
  - sets `Role` and saves;
  - returns the association with its document, or `AssociationNotFound` if there is no row.

  Only that row can change. `AddedUtc`, other projects' rows and the document row are untouched.
- Not wired into `LibraryOperations.ChangeRole` or a route; that is T038.

The lead approved the shared-file changes (Application `.fsproj` line, `Library.fs` visibility, tests in `LibraryTests.fs`) on 2026-11-11. `DocumentRoles.fs` was kept as `tasks.md` names it.

### Tests

`tests/LexScholar.Infrastructure.Tests/LibraryTests.fs`, beside T030 and reusing its helpers (2 tests, Trait `Task=T036`; real SQLite and filesystem; documents imported through `ImportDocument`):

| Test | Checks |
|---|---|
| changing a role in one project leaves the other project's role unchanged | One document is associated with two projects, both `FocusedSource`. Changing project 0 to `ReferenceSource` returns `Ok` with the view (same project, document view and original `AddedUtc`, new role). `listForProject` shows the new role for project 0 and the unchanged association for project 1. Document rows are unchanged. |
| changing a role for an unknown project or an unassociated document is rejected without changes | Both cases return `Error AssociationNotFound`. Association and document rows are unchanged. |

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114. Branch based on `4e131a9`. Tests ran as uid 1000.

## RED

Commit `59637bb`. The repository `changeRole` was a stub that returned `Error AssociationNotFound` for every call.

```sh
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --project tests/LexScholar.Infrastructure.Tests/LexScholar.Infrastructure.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T036'
```

2 total, 1 failed, 1 succeeded.

- The role-change test failed on `assertResult`: `Expected: Ok { ProjectId = …; Role = ReferenceSource; … }`, `Actual: Error AssociationNotFound`.
- The rejection test passed at RED, as expected: the stub's only answer is the error that test expects, and the stub changes nothing. It does fail if the implementation ignores the project scope or creates rows. GREEN is what shows it is not trivially true, because there the same repository does find and change existing associations.

## GREEN

Commit `1de6b7e`. T036 filter: 2/2 passed.

Sensitivity check (temporary, not committed; the file was restored and rebuilt afterwards): changing the update to set the role on every association of the document, ignoring the project, made "changing a role in one project leaves the other project's role unchanged" fail. The rejection test still passed.

## Verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build --filter-not-trait 'Task=T034'
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T034'
```

- Restore and build: 0 warnings, 0 errors.
- Full suite excluding T034, including browser journeys: 279/279 passed, 0 skipped.
- T034 alone: 13/13 still fail on 501 from the route stubs, as expected until T038.

## Limitations

- Concurrent role changes on the same association are last-write-wins, which is acceptable for a single researcher. A role change racing with removal of the same association either reports `AssociationNotFound` or fails on save with EF Core's `DbUpdateConcurrencyException` (the update affects no row). Either way, no other project's data changes.
