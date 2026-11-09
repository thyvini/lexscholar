# T016 and T017 GREEN evidence

The prior [T015 and T013 RED evidence](t015-schema-t013-red.md) is retained. T013 persistence tests were run first and passed 7/7; the HTTP contract tests then passed 9/9. Existing host/security tests that asserted project routes were stubs were updated to expect their implemented responses; unrelated route and security checks remain.

Workspace resolution is deliberately lazy: host startup migrates but does not insert a workspace. The first project operation adopts the sole existing workspace or creates one if none exists. An immediate SQLite transaction serializes concurrent initial resolution. More than one workspace is treated as an error instead of arbitrarily selecting one, consistent with the single-researcher product; seeded workspaces remain usable after host startup. Invalid names are validated before workspace resolution. The unique index is the authority for duplicate creation; a matching normalized-name conflict is translated into the typed duplicate error. Two additional real-SQLite tests exercise concurrent initialization and duplicate creation.

Final commands:

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release
```

Restore and build succeeded with zero warnings. The full suite passed **163/163**, zero failures and zero skips (145 previously GREEN, 16 T013 and two new concurrency tests). No UI or document behavior was implemented.
