# T015 schema and T013 RED evidence

Baseline on release commit `6b858a8`: `dotnet test --solution LexScholar.sln --configuration Release` passed 137/137. Seven initial real-file T015 tests failed assertions because the T010 database lacked `Workspaces`; after the schema implementation they passed 7/7 and the full phase-A suite passed 144/144. An additional rollback test exposed a SQLite cyclic-foreign-key failure; clearing active references before dropping the tables fixed it. Final T015 result: 8/8.

Final verification:

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --project tests/LexScholar.Infrastructure.Tests/LexScholar.Infrastructure.Tests.fsproj --configuration Release --filter-trait 'Task=T015' --output Normal
dotnet test --solution LexScholar.sln --configuration Release --no-build --filter-not-trait 'Task=T013' --output Normal
dotnet test --project tests/LexScholar.Infrastructure.Tests/LexScholar.Infrastructure.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T013' --output Detailed
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T013' --output Detailed
```

Restore and build succeeded without warnings; the non-T013 suite passed 145/145. The last two commands intentionally exit 2: seven persistence assertions fail against the no-behavior signatures, and nine HTTP assertions receive 501 from the existing route stubs instead of the specified responses. Mutation requests obtained antiforgery tokens and cookies and reached the routes, not a CSRF 403. Detailed logs under `/tmp/opencode/lexscholar-t013-*-red.log` are transient, not repository artifacts. T016/T017 remain unimplemented.

## Migration decisions

The original `20261108000000_Initial` is unchanged. The handwritten SQL migration `20261110000000_Projects` creates both tables with restrictive foreign keys and a unique `(WorkspaceId, Id)` principal key for the composite active-project reference. SQLite permits the forward table reference; tests exercise fresh migration, upgrade, constraints and rollback on real files. Rollback deliberately removes project/workspace data after clearing active references to break the FK cycle, while preserving unrelated T010 data.

Persistence-only mutable rows leave domain records EF-independent. T014 supplies trimmed and invariant-normalized names; the schema requires both strings and enforces uniqueness of the supplied normalized value. The migration target model and snapshot explicitly define string-named entities and properties, rather than discovering current CLR properties, so future model changes remain detectable. Tests verify migration history and `HasPendingModelChanges()`.
