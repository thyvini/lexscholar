# T005 — Portable stored summaries

## Verification

- RED: `LOG=/tmp/opencode/t005-red-corrected.log scripts/check.sh T005` against the T004 builder: 63 tests, 2 failed, 0 skipped. Both new summary-export assertions expected a complete ZIP but received `ExportFailed`. The initial test fixture had an invalid database timestamp encoding; it was corrected to the repository's seven-fraction UTC encoding and RED was repeated against the unchanged T004 implementation.
- GREEN / final pre-PR check: `LOG=/tmp/opencode/t005-final.log scripts/check.sh T004 T005`: warning-as-error solution build passed; 114 tests, 0 failed, 0 skipped. This includes the three new T005 tests and existing task-tag collisions in other features, not the full suite.
- Existing-behavior sensitivity: temporarily replaced `SummaryRepository.deserialize`'s unknown-version exception with an empty valid summary. `LOG=/tmp/opencode/t005-version-sensitivity.log scripts/check.sh T005` produced 63 tests, 1 failed, 0 skipped: the new unknown-version export test detected successful export instead of whole-export failure. Mutation restored before final GREEN.
- CI full suite: pending integrator push/PR; this slice was not pushed and no PR was opened.

## T009 release integration

Merged `origin/release/0.5.0` after T009 landed, preserving both guide entries, T004's historical-behavior clarification, both task checkmarks/evidence links and test registrations. `LOG=/tmp/opencode/t005-t009-integration.log scripts/check.sh T004 T005 T009`: warning-as-error build passed; 194 tests, 0 failed, 0 skipped. Full suite remains CI-only.

## Coverage and boundaries

Linux x64, pinned .NET 10 SDK, isolated disposable SQLite workspaces and synthetic original bytes only. The archive checks all summary JSON values, both points and unchanged one-based citations, distinct per-project text paths and contents, source PDF paths, UTF-8 names/text, notice, generation time/service/model, covered/unreadable pages and omitted count. Existing T004 checks retain null summaries and complete document/project/association metadata; its internal-only summary-export expectation now accepts ZIPs.

A real `SummaryJobs` replacement is held with task-completion handshakes while the archive is built: the old durable version is exported, the replacement remains running and uncancelled, and stored content is unchanged. No production generation semantics or repository decoding changed. Unknown stored content versions fail the entire build.

Public export remains fail-closed until T008. File lifetime, partial I/O failure and cleanup-race coverage remain T006/T007; this task does not claim those guarantees or enable deletion. Shared integration files touched: infrastructure test `.fsproj`, `docs/agents/codebase.md`, and only T005's checkbox/evidence link in `tasks.md`.
