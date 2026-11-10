# Data Model: Research Workspace and PDF Library

**Feature**: F001  
**Codecks card**: [$12s](https://thyago-cunha-dev.codecks.io/card/12s)

## Identifier convention

All entity IDs are UUIDs from creation through API and persistence. F# domain/entity types use UUID; SQLite stores canonical UUID text (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`); JSON and route parameters use the same lowercase/hyphenated UUID string. No integer surrogate IDs are exposed or used for these entities. Composite association keys consist of UUIDs.

## Entities

### Workspace

One local researcher's workspace. Creation/deletion UX is outside F001.

| Field | Type | Rules |
|---|---|---|
| Id | UUID primary key | Stable workspace identity. |
| ActiveProjectId | UUID, nullable FK to ResearchProject | Persists last active project. Null if there are no projects; FK must reference a project in this workspace. Restore on startup. |
| CreatedUtc | UTC timestamp | Required. |

### ResearchProject

An independent research context.

| Field | Type | Rules |
|---|---|---|
| Id | UUID primary key | Generated when project is created. |
| WorkspaceId | UUID FK | Required. |
| Name | text | Required; display value trimmed at both ends. |
| NormalizedName | text | Required; trim and invariant case normalization, unique within workspace. |
| CreatedUtc | UTC timestamp | Required. |

**Relationships**: One workspace has many projects. A project has many ProjectDocument associations.

**Validation**: Reject names empty after trimming. Reject duplicate `NormalizedName` with a clear error and preserve existing rows. Project rename/deletion is not in F001.

### LibraryDocument

One shared library record for a byte-exact PDF. It can exist without project associations.

| Field | Type | Rules |
|---|---|---|
| Id | UUID primary key | Opaque ID exposed by the API; does not encode storage location. |
| Sha256 | 32-byte blob or lowercase hex text | Indexed candidate key; not unique because equality still requires byte comparison. |
| StorageKey | generated opaque key | Unique, server generated, resolved only under configured private storage root. |
| OriginalFileName | text | Sanitized basename for display only; never a storage path. UI must encode it. |
| ByteLength | integer | Positive; checked against the configured limit at import time (default 100,000,000 bytes). |
| PageCount | integer | Positive; checked against the configured limit at import time (default 2,000). |
| ImportedUtc | UTC timestamp | Required. |

**Relationships**: Zero or more ProjectDocument associations. Removing the final association preserves this row and its file.

**Limit changes**: Limits apply to import requests, including re-imports. Lowering them does not invalidate, remove, or restrict reading previously accepted documents.

**Identity**: Complete bytes determine equality. SHA-256 narrows candidates, then stored bytes are compared exactly. Different bytes are different documents even if hash candidate or filename matches. Imports commit one at a time within the single process, so every concurrent valid import of identical bytes succeeds and returns the same record without retries; the original bytes and existing associations are preserved.

**Storage invariants**: Originals are written once by promotion, never overwritten, and stored read-only, outside the static web root; content is resolved only through UUID-to-record lookup. On each open, the original must exist, be readable, and have exactly ByteLength bytes; otherwise HTTP 503 `document_unavailable` is returned and this row and its associations are not modified. Content is not rehashed on open, so same-length changes made outside LexScholar are not detected; Sha256 is kept for duplicate detection (see [research](research.md#document-availability-on-open)).

### ProjectDocument

Project-specific association and classification of a shared document.

| Field | Type | Rules |
|---|---|---|
| ProjectId | UUID FK | Required. |
| DocumentId | UUID FK | Required. |
| Role | constrained text enum | Exactly `FocusedSource` or `ReferenceSource`. |
| AddedUtc | UTC timestamp | Required. |

**Key/relationships**: Composite primary key `(ProjectId, DocumentId)`. Role is only on this association. Removing it does not delete the document.

### Import staging and startup cleanup

Not an entity: no import state is stored in SQLite. Each import streams to a request-owned staging file with a generated name in a private staging directory. After validation, imports commit one at a time inside the single process: identical bytes reuse the existing LibraryDocument and delete the staged file; new bytes are flushed to disk, atomically renamed without overwriting to their generated StorageKey on the same filesystem, and only then inserted as a LibraryDocument row. A crash normally leaves only a staging file or a stored original without a row. Flushing protects the staged bytes, but without a directory sync (deliberately not added) the rename itself may not survive a power loss; in that rare case a row can point to a missing original, which is handled as HTTP 503 `document_unavailable` (FR-017) with no other data lost. At startup, under the exclusive process lock and before serving, staging contents are deleted and stored originals that no row references are moved to `quarantine/` and logged as a warning at startup, never deleted. There is no report UI or endpoint.

## Constraints and indexes

- Unique index on `(WorkspaceId, NormalizedName)`.
- Index on `LibraryDocument.Sha256`; unique index on StorageKey.
- Composite primary key `(ProjectId, DocumentId)` and foreign-key indexes.
- Workspace active-project FK references ResearchProject; clear setting only when there are no projects (project deletion is out of scope).
- Enable SQLite foreign keys. Store UUIDs as canonical text and UTC timestamps consistently.
- Enforce configurable defaults of 100,000,000 bytes and 2,000 pages. Malformed, zero, or negative values fail startup.
- No LibraryDocument deletion endpoint or entity lifecycle is introduced by F001.

## State and behavior mapping

| Behavior | Model rule |
|---|---|
| Select/restore project | Update Workspace.ActiveProjectId; read it on startup; fallback to no active selection if null. |
| Project name normalization | Trim display name; normalize for unique comparison; preserve existing projects on conflict. |
| Import | Validate signature, readability, encryption, byte limit, and page limit before creating visible LibraryDocument or association state. |
| Deduplicate | Inside the serialized import commit, match SHA-256 candidates, then compare every byte; reuse UUID record only for exact match. |
| Role change | Update only the selected ProjectDocument.Role. |
| Association removal | Delete only one ProjectDocument row; retain LibraryDocument and original file. |
| Unknown document UUID | HTTP 404 `document_not_found`. |
| Known record, missing/unreadable/wrong-length file | HTTP 503 `document_unavailable`; preserve metadata and associations; recovery UI is deferred in F001. |

## Spec traceability and pending spec clarifications

| Spec requirement | Data model coverage |
|---|---|
| FR-001, FR-002, FR-016 | UUID projects, persisted active selection, normalized-name uniqueness. |
| FR-004–006 | Shared LibraryDocument and many-to-many association; hash plus exact byte identity. |
| FR-007, FR-009 | Role exists only on ProjectDocument. |
| FR-010 | Association deletion leaves document and bytes intact. |
| FR-011 | Projects/documents/associations and active selection persist. |
| FR-012, FR-017 | Read-only original never overwritten; failed imports leave no visible state; unavailable file preserves record/associations. |
| FR-015 | Encrypted file rejected before visible data changes. |
| FR-003, SC-008 | Configurable byte/page limits and non-mutating rejection behavior. |
