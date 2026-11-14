---
name: pr-review
description: Run the GPT review of a LexScholar pull request as the reviewer bot (the `gpt-review` check plus a PR comment) and handle what it finds. Use for /pr-review <pr>, or when a PR needs its gpt-review check.
argument-hint: <pr-number>
---

Review pull request `$ARGUMENTS` with GPT, as the merge gate in AGENTS.md requires. If no PR number was given, ask for it.

1. Make sure the PR's head is final for now (pushed, CI started). The review is tied to that head commit; any later push needs a new review.
2. Run `scripts/gpt-review.sh $ARGUMENTS --post`. It takes a few minutes. It prints the verdict and findings, sets the `gpt-review` check on the head commit as the reviewer app, and posts the same text as a PR comment signed `[gpt-review · <model>]`. If it fails because Codex or the app's config is missing, report the error line and stop; don't post a review yourself.
3. Handle the findings, following docs/agents/review-guidelines.md:
   - **P0/P1**: fix it in the PR, push, and run the review again. If you're sure a finding doesn't apply, answer it in a PR comment (prefixed with your role, e.g. `[developer · T010]`) with the evidence, and tell whoever you report to: the check stays failed, so merging then needs the owner's decision.
   - **P2**: give each a verdict (must fix, accepted risk, overkill) and fix only the must-fix ones.
4. Report in one line: `gpt-review: <success|failure> on <sha7> — P0/P1 <fixed|answered|none>, P2 <n>`.
