# Fix: the reader closes when its document is removed

Fixes the known issue in [verification.md](../verification.md#known-issues-after-the-milestone), raised by Greptile on [PR #31](https://github.com/thyvini/lexscholar/pull/31): removing the open document left the reader open if the project reload failed, and a content check still in flight could reopen it.

## Change

- `Library.update`: `Removed (project, document, Ok ())` for the active project drops that association at once, then reloads the project as before. `Removed` now carries the document ID.
- `Reader.followLibrary`: the library-to-reader glue, moved verbatim from `App.Ui.updateLibrary` so it can be tested without HTTP. The reader closes and ignores pending checks once its document leaves the list, as before; the list now changes on the removal itself.
- Visible side effect: after a failed reload, the project's list no longer shows the removed document, next to the reload error.

## Tests

- `ReaderStateTests` (T039): "removing the open document closes the reader even if the project reload fails"; "a content check that returns after the removal does not reopen the document".
- `LibraryStateTests` (T032): "a successful removal drops the association at once, even if the reload fails".

RED was taken after the no-behavior changes (`Removed` carrying the document ID, glue extracted), so the tests compiled and failed on assertions.

## Verification

Base `release/0.1.1` at `13be20f`, .NET SDK 10.0.114, Linux.

```sh
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj -c Release --no-build --filter-not-trait 'Category=Browser' --filter-trait 'Task=T039' --filter-trait 'Task=T032'
```

- Build: 0 warnings, 0 errors (RED and GREEN).
- RED: 41 total, 3 failed, 38 passed, 0 skipped. Each failed on its first new assertion: `reader.Open` was `Some paper.pdf` (two reader tests), and the association list was `[paper; notes]` instead of `[notes]`.
- GREEN: 41 total, 41 passed, 0 skipped.
- Whole Browser.Tests project, journeys in Chromium and Firefox included: 74 total, 74 passed, 0 skipped.
- Full suite: CI on the PR.
