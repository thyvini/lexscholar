# F002 Data Model

## Existing data (unchanged meaning)

- `LibraryDocument`: UUID, original filename, SHA-256, storage key, byte length, page count, import time. Original bytes are managed separately; storage key never exposed as an export path.
- `ResearchProject`: UUID, name, creation time. `ProjectDocument`: project/document UUID pair, role and added time. Association FK to document is restrictive, summary FK to association cascades.
- `DocumentSummary`: one per association: points with 1-based page positions, pages covered/unreadable, omitted count, generated time, service host, model. Failed/running jobs are transient, not exported. During replacement the last stored summary is exported until successful replacement commits.

## Export view

One read transaction yields documents, project details only for their associations, associations and stored summaries. A document with zero associations remains in the view. UUIDs identify each entity within the archive; PDF entry path is generated from document UUID; original name is display metadata. [Export schema](contracts/export-format.md). All referenced documents and associations exist in the same view. Page references remain original 1-based positions; generated disclosure is retained.

## Deletion preview (transient)

Selected unique document UUIDs, names, each affected project's identity/role and stored-summary presence, plus a server-computed revision digest covering the full selected impact including summary version/content identity. No filesystem path or summary text returned in preview. Confirm must match current selection and revision; a mismatch yields fresh preview before any intent is recorded.

## Deletion operation and items (durable SQLite migration)

- `DeletionOperation`: operation UUID supplied/generated before execution, created UTC; item set immutable after admission. Persisted to permit lookup after request loss/restart.
- `DeletionItem`: operation UUID, document UUID, display filename, opaque validated storage key; status `pending`, `metadata_removed`, `deleted`, or `incomplete` with safe reason and phase. Record the step needed for recovery; never retain document text, summary content or arbitrary path. Item identity is `(operationId, documentId)`; prevent concurrent pending deletion of one document. Completed rows are retained until acknowledged; incomplete rows cannot be cleared while recovery is needed. Outcomes project `deleted` only when metadata and file absence have been confirmed; every other state is `not_completed` with explanation.
- Lifecycle: preview (no write) → confirmed/intent committed for all selected items → for each item fence jobs, transactionally remove associations and document and mark metadata removed → unlink and sync managed original → deleted. Failure → incomplete with preserved phase → retry/restart recovery. Transaction/commit uncertainty is resolved by reading document row and journal in a fresh transaction. Missing original counts as absent only when absence is established, not on permissions/I/O errors.
- Operation completion means all items deleted; mixed items remain explicitly partial. Repeated operation UUID with the same selection returns existing outcome, never repeats a different selection; a different selection using the same UUID is rejected.

## Invariants

1. No item is reported deleted while its live library row, its project associations, its stored summaries or its managed original remains.
2. No job or preparation for a removed association can later save a summary or initiate a new send.
3. A pending item is protected from ordinary association/import/summary mutations until completion or a safely resolved failure, and protected from generic orphan quarantine.
4. Other documents, their associations and summaries are not changed. No export includes live credentials or unrelated settings.
