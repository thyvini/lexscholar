# Post-promotion insertion failure investigation (T021/T022/T029)

## Scope and decision

Work performed only in `.claude/worktrees/dev-b`, branch `fix/f001-import-cleanup`; verified with `pwd && git branch --show-current && git status --short` before editing (clean).

Consulted `CLAUDE.md`, constitution v2.0.0, F001 spec, plan, tasks, research, HTTP contract and existing import/startup evidence. Constitution II requires preservation; V forbids unnecessary recovery machinery.

**No runtime cleanup change is justified without a policy decision.** The original [T021–T029 evidence](t021-t029-import-staging-and-cleanup.md), “Scope decisions (2026-11-10, project owner)” explicitly approves propagating a post-promotion insert error and retaining the unreferenced original until startup quarantine, as a deliberate deviation from “add no … stored originals.” Its limitations repeat this decision. The current implementation matches that approval. T029's blanket “failed imports … add no rows or stored originals” and plan step 3's “leaves no visible state” do not describe every exceptional outcome. This investigation does not silently reverse approval or rewrite those requirements.

Human decision needed: reconcile the strict task/plan wording with the approved retained-orphan exception, and clarify the failed-request outcome when the row actually committed. If stronger compensation is desired, approve its semantics and architecture first. A `Task<unit>` insertion fault does not establish absence of a committed row. Unconditional deletion (or quarantine) of promoted bytes could break an existing row. A failed reference query is not evidence of absence either. Do not add an original-deletion capability to implement the literal task sentence.

## Reproduction and retained regression

First added a focused T021 regression using real staging, validation, promotion, SQLite and filesystem, replacing only `Insert` with a throwing capability. Inside that capability the test first asserted that promotion had completed, bytes matched `valid.pdf`, and mode was read-only. After the propagated exception it asserted empty staging, no rows, then no originals.

```sh
dotnet test --project tests/LexScholar.Infrastructure.Tests/LexScholar.Infrastructure.Tests.fsproj --configuration Release --filter-trait 'Task=T021'
```

**RED:** exit 2; 30 total, 29 passed, 1 failed, 0 skipped. `an insertion failure after promotion leaves no new stored originals` failed at `Assert.Empty(originals workspace.Path)` (line 297): collection contained one generated storage key. Staging and row assertions passed. Local output: `/tmp/opencode/dev-b-import-red.log`.

After confirming historical owner approval, replaced that literal-policy assertion with a two-case preservation/recovery regression, rather than changing runtime behavior to satisfy an unsafe interpretation:

- Throw before inserting: exception identity preserved; no new row; promoted read-only bytes retained; startup cleanup under the directory lock quarantines exactly that orphan without changing bytes or mode.
- Perform the real repository insert, then throw: the same exception reaches the caller, but a new row exists. Startup leaves its original in place, quarantines nothing, and a retry through the same workflow reuses the row (also checks semaphore release).
- Both cases keep pre-existing rows, bytes, mode and project association unchanged; staging is empty and a second cleanup is idempotent.

The second case is an injected acknowledgement failure, not a claim that a particular SQLite error routinely happens after commit. It tests the ambiguity allowed by the insertion port. Recovery is called directly with no active imports, not through a new process. Existing startup host tests cover ordering and warning logging.

**GREEN (approved recovery policy, not a fix of the literal RED contract):** same command, exit 0; 31/31 passed, 0 skipped. Local output: `/tmp/opencode/dev-b-import-green.log`. Added comments at `InsertDocument` and its call site to make the safety constraint explicit. No production behavior changed. The literal stricter assertion would still fail; that policy conflict remains open.

## Verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release --no-build
git diff --check
```

Restore/build succeeded, 0 warnings/errors. Full solution: **354 passed, 0 failed, 0 skipped**, including Chromium/Firefox browser tests. Diff check passed. Environment: Linux `7.3.3-1-cachyos`, .NET SDK `10.0.114`, uid 1000. All data storage uses isolated temporary workspaces.

## Risks and adjacent finding

- Until restart, genuine pre-commit failures leave one additional unreferenced original, deliberately preserved; repeated failures can consume disk. Startup quarantines rather than deletes it.
- Commit-then-error can report a failed request even though the document is listed. Deleting the original cannot safely repair that uncertainty.
- `Discard` runs after the captured attempt and outside its exception handling. If it throws, it masks a primary failure, or can turn an already committed success into a reported failure. The old evidence already records primary-error masking. This investigation does not broaden scope to change error precedence or retry semantics; track/approve that separately. Staging deletion must never become deletion of an original.
- No new fault-injection hooks, journal, retry loop, reference-query fallback, weakened startup recovery, contract/spec amendments, or original deletion were introduced.
