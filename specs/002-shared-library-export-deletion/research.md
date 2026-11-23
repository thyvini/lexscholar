# F002 Research and Decisions

## R1 — Portable export

**Decision:** ZIP with one original PDF per document, a versioned UTF-8 JSON manifest and human-readable UTF-8 summary text. Generated UUID-based entry names avoid duplicate filenames and traversal; original filenames are metadata only. ZIP, JSON and text are readable by standard local tools without LexScholar. The manifest preserves project IDs/names, association roles, document metadata, per-project summaries, citations, generation context and explicit generated/unverified labeling. [Format contract](contracts/export-format.md).

**Rationale:** No package, no proprietary database, one browser download, and both machine-readable relationships and readable summaries. References use document ID and one-based PDF page positions, not promises about native viewer link navigation. Existing stored summary versions are decoded with existing validation; unknown versions fail export, not silently omit data.

**Alternatives:** Raw SQLite plus originals couples export to internals and leaks unrelated data; PDFs alone omit required context; per-project folders duplicate shared originals; HTML-only loses structured metadata. Archive restoration is explicitly deferred, not implied by a version field.

## R2 — A consistent and complete export

**Decision:** A single SQLite read transaction/connection reads all metadata at one consistent point, while file-lifetime coordination prevents deletion or promotion/import commit until the private ZIP is complete. Stream originals to a private request-owned staging ZIP; finalize it before sending response headers. Compare bytes read to recorded length and use existing verified-original availability semantics. Errors abort; cleanup removes the staged file; startup cleans abandoned exports only after exclusive process lock. No job is cancelled by export. All writes may proceed except file-changing import/deletion, which wait; database changes cannot change the snapshot.

**Rationale:** SQLite metadata snapshot plus immutable app-managed originals gives one coherent state without a global lock over long compression or changing job behavior. Snapshot only protects SQLite: the file-lifetime gate is essential. Build-before-response permits meaningful missing-file/disk-full failure before a success status.

**Alternatives:** Independent repository queries can mix states; streaming direct to browser can fail after 200; holding a global mutation lock for the entire ZIP delays summary completion; copying raw database exposes unrelated secrets/settings. The shared gate must coordinate all import and delete paths, not just the new route.

**Risks:** SQLite read transactions can delay writers/checkpoint depending on journal mode; measure with real tests. ZIP construction requires local free space; response transfer can fail after server construction, and an incomplete browser file is outside app cleanup. External changes to same-length files are not detected by F001 availability rules; do not claim cryptographic attestation of PDF contents. App-managed originals are immutable. Power-loss and storage hardware failure cannot be fully excluded.

## R3 — Deletion across SQLite and files

**Decision:** A SQLite deletion operation and per-document items durably record selection, identity, display name and opaque storage key before destruction. Per item, remove associations and document in one transaction while retaining the item; delete the original; then mark item completed. On restart re-read state and retry idempotently before existing startup cleanup, excluding journaled keys from orphan quarantine while incomplete. Outcome remains queryable and incomplete when deletion cannot be proven. Journal records retain no summary text or PDF content. Durable intent means recovery can continue an already confirmed deletion after restart; this is not a second confirmation.

**Rationale:** SQLite cannot commit atomically with unlink. Deleting bytes before recording intent can leave a live row referencing a missing original; deleting row first without durable key causes F001 cleanup to quarantine bytes, contradicting deletion. A per-item journal captures partial success and lost responses.

**Alternatives:** Single database transaction plus filesystem deletion is not atomic; trash/quarantine alone retains deleted research material; rollback of a file unlink cannot restore bytes; whole-batch transaction conflicts with allowed partial outcomes.

**Risks:** Inaccessible filesystem/SQLite prevents progress; startup must not silently declare success, and pending state remains available once DB is accessible. Preserve no-false-success if commit result is uncertain by re-reading. Ensure durability ordering (SQLite commit and directory sync for unlink before marking deleted) and test process termination at each boundary. Power loss/hardware filesystem faults beyond the supported durability guarantees cannot be ruled out; no secure-erase promise.

## R4 — Preview, concurrency and summaries

**Decision:** Preview is a consistent selected-document impact with a digest of all displayed impact and stored-summary identity. Confirmation compares it under a shared mutation boundary and records all intents atomically before releasing that boundary. Any difference returns refreshed preview, no deletion. Pending documents cannot be newly associated or admitted for summarization. Extend existing `SummaryJobs` fencing to ordinary preparation as well as F006 regeneration; cancel pending preparations and registered jobs for all affected pairs, block late admission/completion, and remove associations/summary rows under the existing registry lock. Order locks workspace mutation → registry → SQLite. No async wait or file I/O while holding registry lock.

**Rationale:** A previous preview is not authorization for new affected projects or summaries. F006 covers regeneration preparation but F004's ordinary admission can start late; checking only running jobs is insufficient. Atomic intent registration closes the preview-to-deletion gap across tabs. Source calls already in flight cannot be unsent; cancellation prevents new sends as in F004.

**Alternatives:** UI-only preview races other tabs; a digest without a server-side synchronized comparison races writes; stopping only jobs misses pending admissions; deleting each association independently permits re-association and partial unreported impact. No generic workflow or queue framework is needed.

## R5 — Reporting and privacy

**Decision:** Local unsafe endpoints retain antiforgery/Origin/Host guards. The server creates operation UUID before execution and the client retains it to reconcile lost responses; list recent/incomplete operations after reload. Each selected item has an explicit outcome and safe reason. Completed records are local metadata and may be cleared explicitly after acknowledgement; unresolved items are retained for recovery. Never use filenames as filesystem paths, include credentials/settings in ZIP, log document content or promise removal of downloads/backups/external-service copies.

**Alternatives:** A synchronous response only loses results on disconnect; persist full PDF or summary content in the journal would retain material targeted for deletion and is unnecessary.
