---
name: feature-team
description: Run the LexScholar agent team for one feature (lead procedure). Takes the feature directory, e.g. /feature-team specs/002-library-export. Delivers the feature's remaining tasks through reviewed PRs into the release branch and keeps its Codecks cards in sync.
disable-model-invocation: true
argument-hint: specs/<nnn>-<name>
---

You are the **lead** of an agent team delivering the feature in `$ARGUMENTS`. You coordinate; you don't write product code, tests or evidence, and you don't read code or diffs. AGENTS.md applies to everyone. If no feature directory was given, ask for it.

Tokens are the main cost of this team. Every tool result you read stays in your context for the rest of the session, so:

- ask for the fields you need (`gh pr view <pr> --json headRefOid,statusCheckRollup --jq ...`, `gh pr list --json number,title,headRefName`), never full pages;
- read files by section (`grep -n '^#'`, then the lines you need), never whole;
- let teammates do the waiting and the fixing, and report to you in the fixed formats below.

## 1. Where things stand (every run, including after a restart)

Teammates don't survive a restart, so derive state from the repository:

- `git fetch origin`; in `$ARGUMENTS/tasks.md` on the release branch, find the open tasks (`grep -n '^- \[ \]'`), then read each open task's full entry (its notes, such as a deferral) and the dependencies section. That is what the briefs are built from.
- Open PRs against the release branch, with their check states.
- `git worktree list` and the feature's Codecks sub cards. Resume unfinished work instead of restarting it.

Send the user a status of at most five lines: done, in progress, next, waiting on them. If `tasks.md` is over the size budget in AGENTS.md, or isn't vertical slices with file lists, stop and tell the user before spawning anyone.

## 2. Slices

Group the open tasks into slices of about one PR, following the dependencies in `tasks.md`. Give each slice a Codecks sub card under the feature's hero card (search first; create it only if none exists) and note its code.

**One developer at a time by default.** Run a second only when two ready slices list files that don't overlap and neither touches a shared file (`Program.fs`, `WorkspaceDb.fs`, `Migrations/`, `Contracts.fs`, `Ports.fs`, any `.fsproj`).

## 3. Each slice

1. **Spawn a fresh `developer`** with this brief, filled in and nothing else:

   ```text
   Slice: <task IDs> — <one-line goal in user terms>
   Feature: $ARGUMENTS   Release branch: release/<x.y.z>   Branch: <type>/<feature>-<topic>   Card: $<code>
   Files (from tasks.md): <paths>
   Read: docs/agents/codebase.md sections <names>; spec.md "<heading>"; plan.md "<heading>"; contracts <path#operation> if any
   Notes: <decisions from earlier slices this one depends on, or "none">
   Report formats: plan, ready, blocked (see developer.md)
   ```

   Set the card to started.
2. **Plan**: approve it if it stays within the slice. If it flags over-specification, a contradiction, or a needed change to spec.md, plan.md, research.md, data-model.md or a contract, ask the user and keep any other developer moving. Never approve constitution edits.
3. **Ready**: the developer opens the PR, waits for CI and Greptile itself, fixes CI failures and answers every Greptile P0/P1, then reports `ready`. Check the gate fields yourself (CI conclusion and Greptile on the reported head commit); don't re-read the diff.
4. **Extra review, risky slices only**: when the slice touches storage, migrations, startup, host security or concurrency, or Greptile's confidence is below 4/5, or the developer answered a Greptile P0/P1 instead of fixing it, run `scripts/gpt-review.sh <pr> --post` (GPT through Codex CLI, read-only). Send any P0/P1 it reports to the developer; after fixes, wait for CI and Greptile on the new head and run it again. If Codex isn't available, ask the user instead of skipping the review.
5. **Merge gate** (AGENTS.md), then
   `gh pr merge <pr> --squash --match-head-commit <sha> --subject "<PR title> (#<pr>)"`
   Keep the branch (don't pass `--delete-branch`).
6. **After the merge**: set the card to done, shut down the developer, remove its worktree, tell any other developer to rebase if it shares files, and send the user one status line. Record review follow-ups in `tasks.md` with the next PR that touches the task.

**Always stop and ask the user** before: changing spec.md or the constitution, force-pushing, rewriting the release branch or `main`, merging anything into `main`, or deleting branches. Also stop if the same failure happens twice for reasons nobody can explain.

## 4. Finishing

When every task is ticked or explicitly waiting on the user:

1. Confirm CI passed on the release branch's latest commit.
2. Run `speckit-analyze` in report-only mode and list its findings, each with a proposed verdict (must fix, accepted risk, overkill); don't apply them.
3. Send the user a summary: what merged, open follow-ups, what's waiting on them, the Codecks state.
4. Shut down every teammate.
