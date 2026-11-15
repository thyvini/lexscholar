# Data model: F004 Background Summaries

## SummaryKey

`{ Project: Guid; Document: Guid }` — a summary belongs to one document **in one project** (FR-011). A document shared by two projects has two independent keys.

## Summary job (memory only)

| Field | Notes |
|---|---|
| Id | Fresh per job; only the current id for a key may change state (research §3). |
| Key | `SummaryKey` |
| State | `Running (progress option)` → `Finished` or `Failed`; or removed by Stop / dismiss / removal. |
| Cancellation | Own `CancellationTokenSource` linked to `ApplicationStopping`; for plan jobs also registered in `Vault` via `BeginRun`. |

Transitions:

```text
(none) --POST, checks pass--> Running --done--> Finished (saved to DocumentSummaries, job dropped)
                                     --error/sign-out--> Failed (memory)
Running --Stop/removal--> (none), nothing saved, nothing more sent
Finished/Failed --dismiss--> (none) (stored row deleted)
Failed --POST--> Running (new job)
restart: Running and Failed are gone; Finished rows remain
```

Progress: `{ PagesProcessed; TotalPages }` or `Combining`, as F003's events.

## DocumentSummaries (SQLite)

| Column | Type | Notes |
|---|---|---|
| ProjectId | TEXT NOT NULL | PK part; FK (ProjectId, DocumentId) → `ProjectDocuments` ON DELETE CASCADE |
| DocumentId | TEXT NOT NULL | PK part |
| Content | TEXT NOT NULL | JSON, `version: 1`, F003 summary fields: points (text, pages), pagesCovered, unreadablePages, omittedPoints, serviceHost, model |
| GeneratedAt | TEXT NOT NULL | ISO 8601 UTC |

Validation: written only from a validated `DocumentSummary` (F003 citation rules already applied). Deleted by dismiss and by cascade on removal of the association. Export / full deletion: F002 (`$12y`).

## Status (for markers)

`Running | Finished | Failed` per key, from the registry plus stored rows. Project roll-up: any running → running; else any failed → failed; else any finished → finished.
