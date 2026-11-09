# T014 project-domain GREEN evidence

Environment: Linux CachyOS x86_64 and .NET SDK `10.0.114`. The original [T012 RED evidence](t012-project-domain-red.md) is retained unchanged: 18 focused cases, 13 assertion failures and 5 passing controls before the behavior was implemented.

## Verification

The focused T012 suite was run first after implementation:

```sh
dotnet test --project tests/LexScholar.Domain.Tests/LexScholar.Domain.Tests.fsproj --configuration Release --filter-trait 'Task=T012' --output Detailed
```

Result: **18 passed, 0 failed, 0 skipped**. Then the required solution commands ran from the repository root:

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release
```

Restore succeeded; the Release build had **0 warnings and 0 errors**; the complete solution passed **137/137**, with no skips. This includes the original 119 tests and all 18 T012 cases.

## Behavior and scope

- Name preparation rejects null or whitespace-only input, retains a trimmed display name, and uses invariant uppercase for the normalized name. The result does not depend on the current culture.
- Project creation maps invalid-name errors and checks the supplied immutable snapshot for the same workspace UUID and normalized name. It preserves the supplied IDs and timestamp and does not change the snapshot. The snapshot's existing `NormalizedName` values are expected to follow the same convention.
- The check is not an atomic persistence guarantee. T015/T016 must enforce workspace-scoped uniqueness in storage. The domain API continues to preserve the supplied timestamp rather than validating or changing its offset.
- This increment does not implement T013, T015, T016, HTTP endpoints, UI features, or project persistence.
