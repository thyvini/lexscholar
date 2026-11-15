# T001 evidence: shared pieces

Risky slice (storage, startup): the migration runs at every start.

- **RED** (`84b4761`): `scripts/check.sh T001` — 7 of 33 failed: the schema tests on "T001 requires the DocumentSummaries table" and the contract tests on `MethodNotAllowed` instead of `501`.
- **GREEN**: `scripts/check.sh T001 T010 T015 T022 T025 T043` — 100 tests, 0 failed, 0 skipped (T001 plus the existing storage and startup tests that list migrations).
- **Existing tests changed**: `WorkspaceDbTests.fs`, `WorkspaceStartupTests.fs` and `DocumentSchemaTests.fs` list the migrations and table count; the new id was appended and the table count went 4 to 5. No other change.
- **Sensitivity check** (uncommitted): `ON DELETE CASCADE` changed to `RESTRICT` in the migration; `scripts/check.sh T001` fails `the foreign key to the association cascades on delete`. Reverted.
- **Environment**: Linux, .NET 10 SDK from `global.json`, SQLite through EF Core with foreign keys enabled (`WorkspaceDb.connectionString`).
- **Limitations**: routes answer 501 and `SummaryJobs` members are stubs; the summary repository is empty until T006. Full suite: CI on the PR.
