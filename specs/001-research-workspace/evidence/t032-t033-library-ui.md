# T032, T033 library UI and browser journeys

## Scope

- **T032: `src/LexScholar.Client/Library.fs`** (new). This is pure state in the same shape as `Projects.fs`: `Model`, `Message`, `Effect`, and `update : Message -> Model -> Model * Effect option`. It does no HTTP and no rendering. It also has no reader concerns: no content URL, no role editing and no open action. Those belong to T039 (`Reader.fs`).
  - State: the shared library; the active project's associations (document and `LexScholar.Domain.DocumentRole`); separate library and project loading flags; one mutation at a time (`Busy`: upload, associate or remove); the last upload notice (`Imported` or `Reused`); and an error.
  - Isolation:
    - `ActiveProjectChanged` clears the association list straight away and fetches the new project, so one project's documents are never shown under another.
    - `ProjectLoaded`, `Associated` and `Removed` carry their project id. A response for a project that is no longer active changes nothing that is shown.
  - Refresh: a successful upload refetches the library, a successful associate or remove refetches the active project, and a project switch fetches the new project.
  - Two explicit steps: an upload adds the PDF to the shared library only, and associating it with the active project is a separate action that takes a role.
  - `parseFailure status body` maps the contract's problem codes (`not_a_pdf`, `corrupt_pdf`, `encrypted_pdf`, `upload_too_large`, `page_limit_exceeded`, `duplicate_association`, `invalid_role`, `not_found`, `document_not_found`) to failures. A wrong status/code pair, or any other body, maps to a generic failure, so no response detail is shown.
- **`src/LexScholar.Client/App.fs`**:
  - The program state is now `{ Projects; Library }`, with wrapped messages. Whenever the project list has loaded and its active id differs from the library's, `App.update` forwards `ActiveProjectChanged`.
  - Startup fetches the projects and the library together.
  - `Api` gains:
    - the library and project-document GETs;
    - the multipart upload, which streams `IBrowserFile.OpenReadStream(Int64.MaxValue)`. A comment notes that the server enforces the configured limit.
    - association POST and DELETE, each with a fresh antiforgery token, following the existing `mutate` pattern.
  - The view adds a "Shared library" card and a "Project documents" card:
    - The library card has a labelled `InputFile` ("Import a PDF"), import progress, an "Imported <name>." or "Already in the library: <name>. The existing document was reused." notice, a mapped error alert, and "No documents in the library yet." when empty. Each row shows the file name and page count. When a project is active, the row offers "Add as Focused Source" and "Add as Reference Source", or shows "In this project".
    - The project card shows "Select a project to see its documents." with no project active, and "No documents are associated with this project." for an empty project. Each row shows the role and a Remove button.
  - `Projects.fs`, the projects card and its text are unchanged, so the T019 selectors still hold.
- **Other files**:
  - `LexScholar.Client.fsproj` adds `Library.fs` between `Projects.fs` and `App.fs`;
  - `wwwroot/app.css` gets row styles;
  - the Browser.Tests fsproj adds both test files and copies `tests/Fixtures/*`.
  - No server, Application or contract changes. `InputFile` comes from `Microsoft.AspNetCore.Components.Forms`, which is already part of the WebAssembly package.

### Tests

- **T032: `tests/LexScholar.Browser.Tests/LibraryStateTests.fs`** (14 tests, Trait `Task=T032`). These are pure `update` tests, with a fake `IBrowserFile` for uploads.

  | Test | Covers |
  |---|---|
  | loading the library requests it and shows the documents | `FetchLibrary`, listed documents |
  | switching projects clears the previous project's documents and loads the new one | clearing, `FetchProject`, the new project's own role |
  | a late response for a previously active project is ignored | stale-response isolation |
  | an empty project is loaded with no documents and no project shows nothing | the empty-project state; reselecting the same project is a no-op; no active project |
  | an imported upload is reported and refreshes the library | `SendUpload`, the `Imported` notice, `FetchLibrary` |
  | a reused upload is reported as already in the library | the `Reused` notice, `FetchLibrary` |
  | a rejected upload shows its failure and a new action clears it | error, then recovery |
  | associating sends the chosen role for the active project and refreshes it | `SendAssociate (project, document, role)`, `FetchProject` |
  | associating needs an active project | no effect without a project |
  | a duplicate association shows its failure without refreshing | `DuplicateAssociation` |
  | removing an association refreshes the active project | `SendRemove`, `FetchProject` |
  | a mutation result after a project switch does not refresh the new project | isolation for mutation results |
  | one mutation at a time: repeated actions are ignored while one is pending | `Busy` |
  | HTTP problem codes are mapped to understandable failures | 13 status/body cases, including an unexpected body and a code under the wrong status |

- **T033: `tests/LexScholar.Browser.Tests/LibraryJourneyTests.fs`** (3 journeys × Chromium and Firefox = 6 tests, Traits `Task=T033` and `Category=Browser`).
  - Every test uses its own `TestHarness` with `serveClient = true` and its own temporary workspace.
  - Uploads use the real fixtures through the file input.
  - Only Playwright's auto-waiting assertions are used; there are no sleeps.

  | Journey | Steps |
  |---|---|
  | importing adds to the shared library, reuses identical bytes and explains rejections | both empty states; import `valid.pdf` ("Imported valid.pdf.", one row); `valid-renamed.pdf` is reused ("Already in the library: valid.pdf.", still one row); `not-a-pdf.txt` shows the alert "That file is not a PDF. Choose a PDF file."; `distinct.pdf` makes two rows |
  | switching projects shows each project's own documents and roles | create Alpha and Beta; import two PDFs; Alpha: empty state, then `valid.pdf` as Focused Source; Beta: empty state with no rows from Alpha, then `valid.pdf` as Reference Source and `distinct.pdf` as Focused Source; switching back and forth shows each project's own rows and roles |
  | removing associations keeps the shared document and other projects' associations | `valid.pdf` in Alpha (Focused) and Beta (Reference); remove from Beta, which shows its empty state while the library keeps the document; Alpha still shows Focused Source; remove the final association, the library still has the document, and it can be added again |

  Helpers such as `withBrowser`, `openApp`, `createProject` and `select` are copied from `ProjectJourneyTests.fs`, which stays untouched. `ProjectJourneyTests.fs`'s assembly fixture sets the 30-second assertion timeout for the whole test assembly.

## Environment

- CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, uid 1000, with temporary directories under `/tmp` (tmpfs).
- Microsoft.Playwright 1.64.1, with Playwright's own browsers: Chrome Headless Shell (chromium v1248) and Firefox (v1555), the same builds as in the T019 evidence.
- The work was done on `release/0.1.0` at `8210007`. It was rebased onto `b96caa4` before the PR, and onto `f651c5b` with the review fixes.

## RED

**T032: commit `553852c`.** `Library.fs` had the final types, but `update` returned `model, None` and `parseFailure` always returned `RequestFailed`.

```sh
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj -c Release --no-build --filter-trait 'Task=T032'
```

14 total, 11 failed, 3 succeeded. The 11 failures were assertions:
- `Expected: Some FetchLibrary`, `Actual: None`;
- `Expected: Some (FetchProject bbbbbbbb-…)`, `Actual: None`;
- `Expected: (422, NotAPdf)`, `Actual: (422, RequestFailed)`;
- `Assert.True() Failure`, and similar.

Three tests passed against the no-op, because they check that an action is ignored: the stale response, associating with no active project, and repeated actions while busy. The sensitivity checks below show that the isolation tests can fail.

**T033: commit `d3051bc`.** The journeys ran against the UI as it stood at that commit: the T018 projects UI, with no library section. They were committed before the T032 implementation.

```sh
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj -c Release --no-build --filter-trait 'Task=T033'
```

6 total, 6 failed, 0 succeeded. Every failure was a Playwright assertion, raised after its 30-second auto-wait:
- the import journey (both browsers): `Locator expected to be visible - waiting for GetByText("No documents in the library yet.")`;
- the switching and removal journeys (both browsers): project creation through the existing UI succeeded, then `Locator expected to be visible - waiting for GetByLabel("Import a PDF")`.

On a first attempt, the upload helper called `SetInputFilesAsync` directly. Against the missing input, that failed with an action timeout instead of an assertion. Before the commit, the helper was changed to assert that the input is visible before setting files, so RED fails on assertions.

## GREEN

**Commit `7ecb768`.** T032 passed 14/14.

The first browser run of the implementation failed all 6 T033 journeys. Logging the browser console in a temporary, uncommitted edit showed why: Bolero's `cond` was given an `option * bool` tuple. `cond` accepts only F# unions and booleans, so it threw `ArgumentException: … is not an F# union type` on the first render after an upload, and the client stopped. I replaced it with nested `cond`s. After that, the browser test project passed 35/35: 14 T032, 6 T033, and the existing T018 and T019 tests. T033 then passed 6/6 on 2 further runs.

Sensitivity checks (temporary edits, not committed; the file was restored and the solution rebuilt each time):
- **No clearing on project switch**: "switching projects clears the previous project's documents and loads the new one" failed.
- **Stale project responses accepted** (the active-project guard removed from `ProjectLoaded`): "a late response for a previously active project is ignored" failed.

## Final verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

Restore and build succeeded with 0 warnings and 0 errors. The full suite passed 311/311 (291 at `8210007`, plus 14 T032 and 6 T033 tests), 0 skipped, browser tests included.

The branch was then rebased onto `release/0.1.0` at `b96caa4` (PR #24: T034, T036, T038) without conflicts. The same three commands were re-run on the rebased head: 0 warnings, and 326/326 passed (311 plus the 15 tests from #24), 0 skipped, browser tests included. The commit ids above are from the later rebase onto `f651c5b` (see Review fixes).

## Review fixes (PR #26)

Greptile raised two findings, and the lead chose the fixes.

- **P1 "Older responses overwrite newer lists".** After an associate or remove, `Busy` clears before the refresh finishes, so a second change could start a second refresh. If the older response arrived last, it was accepted because its project id still matched.
  - **Fix:** each list has a request counter, `LibraryRequest` and `ProjectRequest`. `FetchLibrary`, `FetchProject` and `FetchAll` carry the new number, and `LibraryLoaded` and `ProjectLoaded` are accepted only with the latest number (and, for projects, the active project id). This also covers switching A→B→A quickly, where the first request for A would otherwise still match.
- **P2 "Failed lists lack a retry".**
  - **Fix:** a new `Refresh` message reloads the library and, if a project is active, its documents with new request numbers (`FetchAll`), and clears the error. The page's existing Refresh button (`Projects.Reload`) now also dispatches it in `App.update`, so "Please try again" can be acted on.
- **Tests** (Trait `Task=T032`, 4 new, 18 in total). The existing tests now take the request numbers from the effects.
  - "an older library response arriving last does not overwrite the newer list";
  - "an older project response arriving last does not overwrite the newer list" (two associations in a row, responses in reverse order);
  - "switching away and back ignores the first request for the same project";
  - "refresh reloads the library and the active project's documents after a failure" (with and without an active project).
- **RED, commit `8bff739`:** the effects carried numbers, but responses were accepted whatever their number, and `Refresh` did nothing. 4 failed and 14 passed. The three ordering tests failed on `Expected`/`Actual` list assertions, with the stale list shown or, for A→B→A, `Actual: []`. The refresh test failed with "Expected both lists to be fetched, got None."
- **GREEN, commit `940c9f0`:** 18/18.
- **Sensitivity check:** both number checks removed again, so any number was accepted. The three ordering tests failed, and the other 15 passed.
- **Not tested:** the Refresh button wiring in `App.update` has no state test of its own (the `Ui` module is private), and no journey triggers a failed load.
- **Final verification after the rebase onto `f651c5b` (#27, T042):** restore and build had 0 warnings, and the full suite passed 350/350, 0 skipped, browser tests included. No temporary test directories remained under `/tmp/lexscholar-tests` or `/tmp/lexscholar-schema-tests`.

## Limitations

- **Momentary stale display.** The journeys can't observe a brief flash of one project's documents under another, because auto-waiting assertions converge on the final state. That property rests on the T032 state tests: clearing on switch and ignoring stale responses, both shown sensitive above.
- **Upload size.** The upload uses `OpenReadStream(Int64.MaxValue)`. Blazor WebAssembly's `HttpClient` may hold the request body in browser memory before sending. Only the small fixtures are uploaded in tests; large uploads near the 100,000,000-byte default are not exercised in the browser.
- **Upload size limit and page limit in the browser.** These rejections are covered at the HTTP level (T023) and through the state mapping (T032), but no journey triggers them. The journeys exercise one rejection, the non-PDF.
- **Usability not evaluated.** This is automated coverage only, on Playwright's browser builds. Usability with distribution browsers is T046.
