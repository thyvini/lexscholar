# PR #35 release-review fixes (F001)

## Findings and scope

Original review of `release/0.1.0` at `1bc8cde88a2655de8549fe077ce6f9c99232a58b`:

1. **P1, Spec Kit scripts cannot run**: [outside-diff finding](https://github.com/thyvini/lexscholar/pull/35#issuecomment-6787581969). Restore execute bits on six directly invoked scripts, without changing their contents.
2. **P2, Bad uploads report server errors**: [inline finding](https://github.com/thyvini/lexscholar/pull/35#discussion_r4534226566). Map malformed multipart section headers to the existing actionable 400 response, without translating genuine storage failures into client errors.
3. **P2, Removal interrupts role changes**: [inline finding](https://github.com/thyvini/lexscholar/pull/35#discussion_r4534226571). Map an association deleted during its role update to the existing `AssociationNotFound` / HTTP 404 result, preserving unrelated data.

Consulted `CLAUDE.md`, `.claude/agents/developer.md`, constitution v2.0.0, F001 `spec.md`, `plan.md`, `tasks.md`, `data-model.md`, `research.md`, `contracts/http-api.yaml`, and existing T030, T034, T036, T038, import/startup and original-preservation evidence. This follows FR-009/010/012/013 and SC-004/007, without new requirements or schema. Historical T036 already disclosed the role-deletion race; this change removes that exceptional 500 outcome, not its project-isolation guarantee.

## Isolation, baseline and commit provenance

Both developers verified clean isolated worktrees at the baseline before editing:

- dev-a: `.claude/worktrees/review-f001-dev-a`, `fix/f001-release-review-uploads` (multipart tests/endpoint and script modes).
- dev-b: `.claude/worktrees/review-f001-dev-b`, `fix/f001-release-review-roles` (repository and infrastructure/reader HTTP tests).
- Independent baseline tester: `.claude/worktrees/review-f001-verifier`, detached at baseline; no source changes. Restore/build passed; full solution **386/386 passed, 0 skipped**, including browser tests. Raw evidence: `/tmp/opencode/f001-release-review-verifier/baseline-*.log` and `baseline-notes.md`.

The lead cherry-picked into `fix/f001-release-review`. Reachable commits and original developer executions are distinguished below; the original executions are not represented as runs on their later cherry-picked SHAs:

| Change | Reachable consolidated commit | Original developer commit tested |
|---|---|---|
| Upload RED, tests only | `6012b794eed5b0b7aad3e435b4d3c385d7a264e0` | `8f4f7215ad6b8bf8cda6324fa2569513154ccb82` |
| Upload GREEN, endpoint only | `4138a23afc6bf25e925fbf5bd76b0e9369e523f9` | `b1a7955ee16a4c3797d50175eb6faf30bf00a658` |
| Six mode-only changes | `b45a16d3bf8a3030b8c717987641ff4a96c65896` | `54ad0e216cb78e9437bee6bb8e17a2cda54b82f9` |
| Role RED, tests only | `a40442862a378d5eab9fb6c5f44d6e417fb4ded5` | `831bd880fc435abebc13fbbafa09134977d053d8` |
| Role GREEN, repository only | `1917cf62a560981141c1bb8c0aac484ab20f6867` | `d0a70cb0dcb6415c5ab02b16fea0be2db54f32db` |

For combined verification and this evidence only, dev-b switched its **same assigned worktree** to new branch `fix/f001-release-review-evidence` from consolidated code head `1917cf62a560981141c1bb8c0aac484ab20f6867`. No integration-worktree edits or merges were performed. `git ls-remote origin refs/heads/release/0.1.0 refs/heads/main` confirmed remote release remained `1bc8cde88a2655de8549fe077ce6f9c99232a58b`; main was `996ab054b8e1301b166f2cae4955ed19aa20a3b3`. Log: `combined-remote.log` under the dev-b log directory below.

## Finding 1: executable modes

| `.specify/scripts/bash/` file | Baseline | `origin/main` | Consolidated fix | Baseline direct exit | Fixed direct exit |
|---|---|---|---|---|---|
| `check-prerequisites.sh` | 100644 | 100755 | 100755 | 126 | 0 (`--help`) |
| `common.sh` | 100644 | 100755 | 100755 | 126 | 0 (no arguments) |
| `create-new-feature.sh` | 100644 | 100755 | 100755 | 126 | 0 (`--help`) |
| `resolve-template.sh` | 100644 | 100755 | 100755 | 126 | 0 (`--help`) |
| `setup-plan.sh` | 100644 | 100755 | 100755 | 126 | 0 (`--help`) |
| `setup-tasks.sh` | 100644 | 100755 | 100755 | 126 | 0 (`--help`) |

dev-a recorded actual permission-denied failures for all six, then successful direct execution after restoring modes (`modes-before.log`, `modes-after.log`). `git show --numstat` for the mode commit reports 0/0 lines for each file; script contents remain the release versions, not replacements from main. dev-b rechecked all fixed direct invocations on the consolidated code, plus the finding's actual template-resolution example, which returned JSON and exit 0.

Reproduce from the appropriate isolated checkout's root (these are direct calls, not `bash script` workarounds):

```sh
git ls-tree 1bc8cde88a2655de8549fe077ce6f9c99232a58b .specify/scripts/bash/
git ls-tree origin/main .specify/scripts/bash/
git ls-tree HEAD .specify/scripts/bash/
git show --format= --summary b45a16d3bf8a3030b8c717987641ff4a96c65896
git show --format= --numstat b45a16d3bf8a3030b8c717987641ff4a96c65896
.specify/scripts/bash/check-prerequisites.sh --help
.specify/scripts/bash/common.sh
.specify/scripts/bash/create-new-feature.sh --help
.specify/scripts/bash/resolve-template.sh --help
.specify/scripts/bash/setup-plan.sh --help
.specify/scripts/bash/setup-tasks.sh --help
.specify/scripts/bash/resolve-template.sh spec-template --json
```

Combined output: `combined-modes.log`. These are mode/execution smoke checks and template resolution, not exhaustive functional tests of all Spec Kit workflows. No script-test or CI infrastructure was added.

## Finding 2: malformed multipart input versus genuine failures

Original dev-a RED/GREEN command:

```sh
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --filter-trait 'Task=T023'
```

- **RED:** exit 2; **18 total, 3 failed, 15 passed, 0 skipped**. Three new status assertions expected 400 but got 500 `internal_error`: a header without a colon, 17 headers (limit 16), and a 16,385-character header value (length limit 16,384). Framework logs identify `InvalidDataException` with invalid-header/count-limit/length-limit messages respectively. Tests reached the real endpoint with valid request security; no compile/startup failure is counted as RED.
- **GREEN:** exit 0; **18/18 passed, 0 skipped**. Each malformed case now requires 400 `not_a_pdf`, `application/problem+json`, the existing multipart/file explanation, no private header/filename or workspace-root disclosure, unchanged seeded document/association and original names/bytes/mode, and empty staging.
- A genuine filesystem-failure control passed in both RED and GREEN: after startup, replace the empty staging directory with a file, submit valid multipart PDF, require safe 500 `internal_error`, preserve original bytes/mode and row, and restore the test layout in `finally`.

The fix catches **only `System.IO.InvalidDataException` around `ReadNextSectionAsync`** and returns the existing no-file/400 path. Multipart-reader construction, file-stream reads, import validation/storage/database work, cancellation and other I/O errors are not covered by that catch. In particular this is a narrow first-section-header fix, not general normalization of malformed content encountered later during file streaming. No broad import exception translation or production test hooks were added.

Original logs/notes: `/tmp/opencode/f001-release-review-dev-a/{red.log,green-focused.log,restore.log,build.log,nonbrowser.log,notes.md}`. dev-a restore/build passed with zero warnings/errors; its non-browser solution run passed **366/366**, 0 skipped. That run intentionally excluded browser tests; the combined run below does not.

## Finding 3: deletion during role update

`changeRole` loads the composite-key association with `Find`, changes `Role`, and saves. `removeAssociation` independently uses composite-key `ExecuteDelete`; that deletion does not update an already tracked entity in another context. The fresh role-update context modifies only this association, with no concurrency token. Catching only `DbUpdateConcurrencyException` directly around its `SaveChanges` maps the zero-row save to existing `RoleChangeError.AssociationNotFound`; the existing reader endpoint maps this to 404 `not_found`. Document lookup and unrelated database failures remain outside this catch. No schema/ports/endpoint changes, locking or retry infrastructure were necessary.

The regression uses a **test-local SQLite BEFORE UPDATE OF Role trigger**, scoped to both IDs. It deletes the selected association and executes `RAISE(IGNORE)`, deterministically ordering the real repository read before a zero-row UPDATE. Empirically, the pinned EF/SQLite save throws `DbUpdateConcurrencyException` (expected 1 row, affected 0), **and the trigger deletion persists**. Fresh-connection assertions verify actual deletion and preserved collateral state **before** the desired result/status assertion. A rollback therefore cannot make a surviving association masquerade as this regression.

Tests invoke actual `DocumentRoles.changeRole` over actual `LibraryRepository.changeRole`, and actual authenticated HTTP PATCH through `TestHarness`, with Origin and antiforgery protections intact. The repository test captures exceptions as outcome values so RED is an explicit failing outcome assertion, not an uncaught exception. A companion trigger uses `RAISE(ABORT, 'role update denied')`: its `DbUpdateException` / `SqliteException` code 19 must still escape and produce safe HTTP 500 `internal_error`, with the fixed generic title and no root, keys, exception names or trigger message disclosed.

Original dev-b commands:

```sh
dotnet test --project tests/LexScholar.Infrastructure.Tests/LexScholar.Infrastructure.Tests.fsproj -c Release --no-build --filter-trait 'Task=T036'
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj -c Release --no-build --filter-trait 'Task=T034'
```

| Run | Infrastructure T036 | Reader HTTP T034 |
|---|---|---|
| RED | 4 total: 3 passed, 1 failed, 0 skipped; expected `AssociationNotFound`, captured `DbUpdateConcurrencyException` | 15 total: 14 passed, 1 failed, 0 skipped; expected NotFound, actual InternalServerError |
| GREEN | 4/4 passed, 0 skipped | 15/15 passed, 0 skipped |

Preservation checks compare every column of document, project and workspace rows and every remaining association, including `AddedUtc`; both originals retain exact bytes and mode 0400. Sentinels include another project sharing the document, another document in the selected project, and separate unrelated HTTP-arranged projects/document. Existing role-isolation, missing-association and association-removal coverage remains in the suite.

Original logs: `/tmp/opencode/f001-release-review-dev-b/{trigger-probe-red.log,infrastructure-red.log,server-red.log,infrastructure-green.log,server-green.log,notes.md}`. Original dev-b restore/build passed with zero warnings/errors, its non-browser suite passed **366/366**, and its full solution passed **390/390**, 0 skipped. These counts are for that lane, not the consolidated code.

**Limits:** the trigger faithfully reproduces the missing-target save condition, not two simultaneously scheduled HTTP requests or concurrent execution of `ExecuteDelete` itself. No sleeps, probabilistic timing or permanent test-only production hooks are used. Same-role requests may generate no UPDATE; existing semantics remain unchanged. Successful concurrent role changes remain last-write-wins. Stronger serializability is neither introduced nor claimed.

## Final combined verification

Executed from dev-b's assigned worktree, clean at consolidated **code head `1917cf62a560981141c1bb8c0aac484ab20f6867`**, before adding these evidence-only changes:

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln -c Release --no-build
git diff --check
```

All four exited **0**. Restore/build: **0 warnings, 0 errors**. Full solution: **394 passed, 0 failed, 0 skipped**, 43.530 seconds; all five test projects, including Chromium/Firefox browser journeys, passed. No filters or skips were used. This is the 386-test baseline plus four upload cases and four role cases.

Raw combined logs: `/tmp/opencode/f001-release-review-dev-b/combined-{restore,build,test,diff-check,environment,remote,modes,review-comments,reviews,issue-comments}.log`. Detailed lane and baseline notes were cross-read rather than inferring counts. These machine-local raw logs are not committed; this document records their reproducible commands and observed outcomes.

Environment: 2026-11-11, CachyOS Linux `7.3.3-1-cachyos` x86_64, uid/gid 1000; .NET SDK `10.0.114`, MSBuild `18.0.36`, ASP.NET/Core runtime `10.0.14`; EF Core SQLite `10.0.14`, Playwright `1.64.1`. The independent baseline tester ran `scripts/install-browsers.sh` for both engines: Chromium revision 1248 (Chrome for Testing `156.0.8078.4`) and Firefox revision 1555 (`157.0`); CachyOS uses Playwright's Ubuntu 24.04 fallback builds. Tests use isolated temporary SQLite/filesystem workspaces, not the researcher's data directory.

This is developer combined verification, **not an assertion of independent final-head approval**. Exact-final-head independent test results, reviewer verdicts and Greptile review belong in the follow-up fix PR's SHA-keyed comments; they are separate from the baseline and developer executions documented here. No unobserved independent result is claimed.

The out-of-solution SC-002 corpus project was **not rerun**; historical corpus evidence remains separate. Native viewer internals and unassisted researcher usability are not established by these automated tests. **T046 remains unchecked/deferred to beta**, SC-001/006 participant evidence remains outstanding, full-library export/deletion remains F002, and unavailable-original recovery UI remains deferred. No tasks, specification, constitution or acceptance criteria were changed. No PR, push or merge was performed by either developer.
