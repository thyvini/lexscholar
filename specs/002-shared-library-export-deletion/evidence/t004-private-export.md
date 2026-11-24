# T004 — Private ZIP export foundation

- Base: `origin/release/0.5.0`, isolated `feat/f002-export-preflight` worktree.
- Environment: Linux x64, repository-pinned .NET 10 SDK; isolated `TemporaryWorkspace` and fixture PDF bytes only.
- RED: `scripts/check.sh T004` — build passed; two new contract assertions failed (`409`/`200` expected, test-only placeholder returned `501`). Log: `/tmp/lexscholar-check.GZNYes`.
- GREEN: `scripts/check.sh T004` — warning-as-error build passed; 51 task-matching tests passed, zero failures/skips. Repeated after adding README, cleanup, UTC and summary fail-closed assertions: 51 passed, zero failures/skips.
- CI full-suite run: pending the PR; not run locally.

## Verified

The explicitly test-mapped POST returns a ZIP with original shared/unassociated PDFs exactly once, matching fixture bytes. Manifest root and document/project/association field sets are exact v1; stored metadata, UTC dates, roles and associated project selection match the database. Unreferenced projects are excluded. Traversal-like and absolute original filenames remain metadata, never ZIP paths; PDF paths use canonical UUIDs. README states generated/unverified status, one-based page positions and no in-app restoration. Successful transfer leaves no staged ZIP and does not remove library records. Empty internal export returns `409 empty_library`.

Ordinary host composition does not map the endpoint (`404`), so no public success is enabled. No configuration flag or production activation was added. The existing `TestHarness.configure` seam maps `LibraryControlEndpoints.mapForTests`.

## Explicit incremental limits

T005 owns structured/readable summaries: this foundation rejects any snapshot containing stored summaries rather than silently omitting them. Its temporary fail-closed assertion must be replaced with full summary assertions in T005. T006 owns failure attribution, bounded-memory/cancellation/transfer/restart fault verification and lifecycle hardening; T007 owns export race verification. This evidence does not claim those safeguards or full F002 acceptance. No deletion files, startup behavior, credentials or external service workflows were changed.
