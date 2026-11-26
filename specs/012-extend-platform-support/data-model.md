# Workspace data — no migration

Both targets use the same existing SQLite workspace, project/document associations, summaries, private credentials and managed originals. Keep the data directory separate from the publish tree. A library document's generated storage key, not its display filename, identifies its managed entry. An export is a separate copy outside the library; deleting the document must not change it. No new table, schema version, platform field, credential copy or cross-computer migration.

The lock is OS ownership of an open file, not the presence of `.lexscholar.lock`. Lack of ownership prevents startup before migrations/recovery. Normal disposal or owner termination permits reacquisition without deleting the file. Confirmed deletion retains its existing journal: intent → metadata removed → original removed and directory synced → durable completion. Errors retain an unfinished, retryable item; an already completed retry cannot unlink again. See the [codebase guide](../../docs/agents/codebase.md) for F002 terminal states and recovery.

Candidate identity, tested OS/build/architecture, browser versions, fixture digest, step outcomes, limitations and the owner's attestation belong in verification records, **not** the researcher's database.
