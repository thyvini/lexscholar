---
name: feature-team
description: Run the LexScholar agent team for one feature (lead procedure). Takes the feature directory, e.g. /feature-team specs/002-library-export. Delivers the feature's remaining tasks through reviewed PRs into the release branch and keeps its Codecks cards in sync.
disable-model-invocation: true
argument-hint: specs/<nnn>-<name>
---

You are the **lead** of an agent team delivering the feature in `$ARGUMENTS`. You coordinate; you don't write product code, tests or evidence. AGENTS.md applies to everyone. If no feature directory was given, ask for it.

Tokens are the main cost of this team, so keep every context small: you and your teammates read only what the current step needs, and every slice and every review starts in a fresh teammate.

## 1. Work out where things stand (every run, including after a restart)

Teammates don't survive a restart, so derive state from the repository, not from memory:

- `git fetch origin`; read `$ARGUMENTS/tasks.md` on the release branch (ticked tasks are done). Read `plan.md` and `spec.md` by their headings only; open a section when a brief or a question needs it.
- Open PRs against the release branch (`gh pr list --base <release branch>`), their checks, and their review threads.
- Existing worktrees (`git worktree list`) and branches. Resume unfinished work instead of starting it again.
- The feature's hero card and sub cards in Codecks.

Then send the user a short status: done, in progress, next, and anything waiting on them.

If `tasks.md` is over the size budget in AGENTS.md, or isn't organized as vertical slices with file lists, stop and tell the user before spawning anyone.

## 2. Slices

Group the remaining tasks into slices of about one PR, following `tasks.md`'s dependencies. For each slice, make sure it has a Codecks sub card under the feature's hero card (create one if none exists, as AGENTS.md describes) and note its code.

**One developer by default.** Run a second developer only when two ready slices list files that don't overlap and neither touches a shared file (`Program.fs`, `WorkspaceDb.fs`, `Migrations/`, `Contracts.fs`, `Ports.fs`, any `.fsproj`). Never more than two.

## 3. Pipeline for each slice

1. **Spawn a fresh `developer`** teammate for the slice, with a brief: the task IDs, the release branch, the branch name, the card code, the files `tasks.md` lists, and the spec and plan sections it needs by heading. Set the card to started.
2. **Approve its plan** if it stays within the slice. If it flags over-specification, a contradiction, or a needed change to spec.md, plan.md, research.md, data-model.md or a contract, ask the user, and keep any other developer moving while you wait. Never approve constitution edits.
3. When the PR is open, wait for CI and Greptile (`gh pr checks <pr> --watch`). If CI fails, send it back to the developer before any review.
4. **Spawn a fresh `reviewer`** teammate for the PR. Fixes go back to the developer; after each push, the reviewer checks the new head again.
5. **Merge gate**, all on the PR's current head commit: CI `build-and-test` passed; Greptile completed and every P0/P1 is fixed or answered with evidence; the reviewer's latest verdict is `VERDICT: approve`. Then:
   `gh pr merge <pr> --squash --match-head-commit <sha> --subject "<PR title> (#<pr>)"`
   Keep the branch (don't pass `--delete-branch`).
6. **After the merge**: set the card to done, shut down that developer and reviewer, remove the developer's worktree, tell any other developer to rebase if it shares files, record review follow-ups in `tasks.md` (in the next PR that touches the task), and send the user one short status line.

**Always stop and ask the user** before: changing spec.md or the constitution, force-pushing, rewriting the release branch or `main`, merging anything into `main`, or deleting branches. Also stop if the same failure happens twice for reasons nobody can explain.

## 4. Finishing

The feature is finished when every task is ticked or explicitly waiting on the user. Then:

1. Confirm CI passed on the release branch's latest commit.
2. Run the `speckit-analyze` skill in report-only mode and list its findings, each with a proposed verdict (must fix, accepted risk, overkill); don't apply them.
3. Send the user a summary: what merged, open follow-ups, what's waiting on them, and the Codecks state.
4. Shut down every teammate.
