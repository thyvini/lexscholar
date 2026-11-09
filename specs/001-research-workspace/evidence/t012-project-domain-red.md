# T012 project-domain RED evidence

Environment: Linux CachyOS x86_64 and .NET SDK `10.0.114`. Commands ran from the repository root on 2026-11-08. This is a deliberately incomplete test-first increment, not implemented project management.

## Baseline before scaffolding and tests

```sh
dotnet test --solution LexScholar.sln --configuration Release
```

Result: **119 passed, 0 failed, 0 skipped** on the untouched `release/0.1.0` baseline.

## Compile-ready scaffold and RED

```sh
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --project tests/LexScholar.Domain.Tests/LexScholar.Domain.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T012' --output Detailed
```

The Release build succeeded with **0 warnings and 0 errors**. The focused test command exited with code 2: **18 total, 13 failed, 5 passed, 0 skipped**. Every failure was a domain behavior assertion, not a setup, compilation, or unimplemented-method exception:

| Expected RED cases | Count | Actual scaffold behavior |
|---|---:|---|
| Empty, spaces, tabs/newlines, and null names are rejected | 4 | `Ok` with the unchanged input. |
| Project creation rejects a blank name | 1 | `Ok` project with a blank name. |
| Display names are trimmed, including on project creation | 2 | Surrounding whitespace remains. |
| Case, outer whitespace, and invariant-culture variants normalize identically | 3 | Unchanged input is returned as the normalized name. |
| Exact and case/whitespace duplicate names are rejected in the same workspace, without changing existing data | 3 | The duplicate attempt returns `Ok` instead of `Error DuplicateProjectName`; the snapshot-preservation assertion itself passes. |

Passing controls: a valid name is accepted; different names coexist in one workspace; the same name can be used in independent workspaces; UUID identities and the supplied UTC timestamp are retained; and an active project can be absent. The tests compare normalized results, not a chosen upper- or lowercase representation.

After adding the scaffold, the original suite was checked separately:

```sh
dotnet test --solution LexScholar.sln --configuration Release --no-build --filter-not-trait 'Task=T012'
```

Result: **119 passed, 0 failed, 0 skipped**. Running the full suite without the filter is expected to fail until the T014 behavior is implemented.

## Domain API decisions and remaining work

- `Workspace` and `ResearchProject` are immutable F# records with `Guid` identities; an absent active project is `None`. `CreatedUtc` uses `DateTimeOffset`, matching existing application contracts. The scaffold accepts the supplied timestamp and does not yet validate its UTC offset.
- `ProjectName.create` exposes a pure validation/normalization result with `InvalidProjectName`; `ResearchProject.create` accepts explicit IDs, time, name, and a snapshot of existing projects, returning `InvalidName` or `DuplicateProjectName` as typed errors. IDs and time are passed in rather than generated or read from ambient state.
- The scaffold deliberately returns names unchanged and ignores the existing-project snapshot. It is not a usable validation or duplicate-prevention implementation. Public records do not yet enforce invariants. T014 remains unchecked until the behavior is implemented and verified GREEN.
- The domain duplicate decision is a pure check against supplied data, not an atomic persistence guarantee. T015/T016 must separately enforce the workspace-scoped uniqueness constraint in storage. No schema, repositories, HTTP endpoints, or UI work is included.
