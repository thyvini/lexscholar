# T016 — New summaries in Brazilian Portuguese ($153)

- Owner-approved correction: all new summaries use Brazilian Portuguese regardless of source language. F006 FR-016/R11 explicitly supersede F003's source/majority-language rule and F006's former F007 deferral.
- RED: `LOG=/tmp/opencode/t016-red.log scripts/check.sh T016` — warning-as-error build passed; 6 tests, 4 failed assertions, 0 skipped. All three single-call source cases lacked the Brazilian-Portuguese instruction; the long-document instruction assertion also failed.
- GREEN: `LOG=/tmp/opencode/t016-green.log scripts/check.sh T016` — warning-as-error build passed; 7 tests, 0 failed, 0 skipped.
- Coverage: English, Portuguese and mixed-language single-call inputs retain their source text; a long English document exercises map, intermediate reduction and final combine instructions. Existing parser/citation rules remain unchanged. The existing server restart regression is additionally tagged T016: stored English `Main idea`, document id, host, model and generation date survive restart; GET and repeated POST make no new service request.
- Scope: three shared prompts only; both destinations consume them. No storage read/write changes, migration, translation of stored summaries, language detection or additional model call.
- Limitation: scripted replies demonstrate outgoing instructions and workflow behavior, not real model compliance. Quickstart step 10 adds real-plan output checks for T013; these were not performed here.
- Full-suite CI: pending the eventual PR; not run locally per AGENTS.md. No PR opened or merge performed for this slice.

## PR #125 review corrections

- P1 (unrelated T015 ancestry): **must fix**. Preserve the old head on `backup/f006-summary-language-pre-review`; cherry-pick only T016 onto `origin/release/0.4.0` at `98fcd0b`. The PR diff now excludes project-title source, styles, tests and T015 evidence.
- P2 (duplicate FR-015): **must fix**. Give the new language requirement FR-016 and update F003/F006 cross-references and task coverage; keep existing FR-015 for both-browser behavior.
- Post-base-change verification: `LOG=/tmp/opencode/t016-review-green.log scripts/check.sh T016` — warning-as-error build passed; 7 tests, 0 failed, 0 skipped. `git diff --check` passed. No behavior change beyond the original T016 correction; full-suite CI and updated GPT review remain the PR gates. No merge performed.

## PR #125 full-suite CI failure investigation

- RED: [CI run 44549720195](https://github.com/thyvini/lexscholar/actions/runs/44549720195), head `1447f9f` — 906 tests, 905 passed, 1 failed, 0 skipped. Retrieved once with `gh run view 44549720195 --repo thyvini/lexscholar --log-failed`; local log `/tmp/opencode/pr125-ci-failure.log`.
- Failure: Firefox `WorkspaceJourneyTests.changing a role moves the document and leaves the other project unchanged` timed out at the library's "Adicionar como fonte de referência" button (line 42). The ARIA snapshot shows Beta active and the main area's document-selection prompt, not the library. No summary-language assertion failed.
- Actionable cause: the journey clicked library navigation immediately after starting asynchronous Beta selection, without waiting for its rendered completion. Selection inserts/removes the sidebar loading status and changes navigation positions. Unlike the existing shared summary setup, this journey lacked the selected-row assertion before the next navigation click. The precise pointer/render interleaving is inferred from the snapshot and code; CI does not contain a browser trace.
- Fix: wait for Beta's existing `selected` class via Playwright's auto-waiting assertion before clicking library navigation. No application behavior, timeouts, retries, sleeps or T018 selection changes. Existing coverage and task tags remain unchanged.
- GREEN: `LOG=/tmp/opencode/pr125-ci-fix-green.log scripts/check.sh T004 T016` — warning-as-error build passed; 56 tests passed, 0 failed, 0 skipped, including the affected journey in both browsers and the T016 tests. No full-suite rerun requested; the next head's CI must validate the complete suite.
