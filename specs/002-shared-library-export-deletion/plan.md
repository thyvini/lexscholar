# Implementation Plan: Shared Library Export and Deletion

**Branch**: `docs/f002-plan` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md) (approved through #138)

**Input**: F002, [Codecks $12y](https://thyago-cunha-dev.codecks.io/card/12y). Planning only; no tasks or application implementation in this change.

## Summary

Produce a local ZIP containing original PDFs, a UTF-8 JSON manifest and human-readable summaries with source/page references. Build it completely in private temporary storage before returning a download. A single SQLite read transaction supplies the logical state; an export/delete/import coordination gate keeps the selected original files available while the archive is built. Generation and project edits may continue: the export is the database state seen by its read transaction, not a mixture of separate queries. Missing originals or failed construction produce an error, never a successful download.

Deletion uses a confirmation preview and revision token, durable per-document operation records, transactional metadata removal and idempotent managed-original deletion. The summary registry fences preparation and jobs before an association is removed. Startup recovers deletion records before F001 orphan cleanup can quarantine pending deletions. Each selection has durable per-document outcomes for recovery and partial results. See [research](research.md), [data model](data-model.md), [contracts](contracts/) and [quickstart](quickstart.md).

## Technical Context

**Language/Version**: Functional-first F# / .NET 10, SDK pinned in `global.json`.

**Dependencies**: Existing ASP.NET Core, Bolero/Elmish, EF Core SQLite, xUnit v3 and Playwright; .NET `System.IO.Compression` and `System.Text.Json`; no new package.

**Storage**: existing SQLite and private original store; one narrowly scoped deletion operation/item journal migration; private request-owned archive staging. No arbitrary client paths.

**Testing**: pure state and application tests, real SQLite/filesystem tests, in-process HTTP contracts, restart/process tests and Firefox/Chromium journeys. Deterministic synchronization and fault injection, not sleeps. CI runs the complete suite.

**Platform/constraints**: one Linux host/data-directory lock, multiple local tabs; loopback, Host/Origin and antiforgery unchanged; existing Portuguese UI. PDF sizes are configurable, so stream archive content rather than buffer the library. Construction requires free local space for a ZIP; no fixed timing promise.

**Scope**: whole-library export and confirmed deletion of selected documents, including stored per-project summaries; no import of archives, secure erase, external-copy removal or new service. F001 association removal retains its existing meaning.

## Constitution Check

Pre- and post-design assessment: **pass**, subject to owner approval and validation; no constitutional amendment or exception. Principle II is not claimed fulfilled until implemented and verified.

| Principle | Design and evidence |
|---|---|
| I — evidence | Export retains each stored point, source identity and page numbers, omitted count and coverage; generated status is explicit. Verify against fixture PDFs; no claim that citations prove truth. |
| II — privacy/control | Local-only archive and deletion; private staging, existing host security, no secrets in export or errors. Preview names impact and external-copy limits. Durable recovery covers incomplete removals. |
| III — oversight | Readable summaries say generated/unverified and identify PDFs/pages for human checking. |
| IV — testability | Contract, concurrency, crash-boundary and both-browser validation described in quickstart; use fixture PDFs only. |
| V — simplicity | One ZIP, one snapshot, existing registry and one purpose-specific journal; no restore, queue framework or general operation abstraction. The owner accepted the size exception below. |
| VI — decisions | [research.md](research.md) records choices and alternatives; approved product behavior stays in spec. |

## Design

### Export

1. Acquire shared archive/file lifetime coordination against destructive deletion and import commit; establish a SQLite read transaction and read all export metadata in that transaction. Database writes, including summary completion, can continue subject to SQLite's normal transaction behavior. Export acquires no summary-registry lock while holding the SQLite read transaction. No export-induced cancellation or mutation of summary jobs.
2. Build a private ZIP by streaming every selected-state original; verify availability and recorded length using existing availability rules. Missing, unreadable, wrong-length or read failure aborts construction and identifies the document where possible. Filename paths in archive are generated IDs, never user filenames. Render readable material from the same captured state. Close and validate the complete archive before HTTP response headers or download success; discard partial temp output on failure/cancellation and at startup.
3. Release coordination and snapshot when the archive is complete; stream the completed file with a download response and dispose/delete the private temp file on completion or disconnect. A failed transfer is not described as successful by the client. A complete file may consume temporary space roughly equal to the library; report disk-full, no partial export presented as complete. Storage cleanup recognizes this separate temp area, not an in-progress export.

The coordination protects application-managed import promotion/commit and deletion across tabs. Existing originals are immutable in app use. Out-of-band filesystem mutation and sudden power loss remain risks; see research. SQLite read snapshot must be established by a read before any individual query and use one connection/transaction for all export data, not existing per-call repository contexts.

### Deletion and summary jobs

- Preview reads selected documents, project names/roles and stored-summary presence in one consistent read, and returns a digest of the complete visible impact, including actual summary version/content identity. Confirm submits the same IDs and digest. The server recomputes under mutation coordination; if different, make no deletion and return refreshed impact for another confirmation. A client-provided digest is a comparison token, not authority to choose paths or bypass validation.
- Commit a durable selection and every item before any destructive step. Coordinate all association/role/summary writes that affect the preview, including summary completion, so no such write can slip between validation and registration of deletion intents. Once registered, affected documents reject new associations and summary admissions, including ordinary and regeneration preparations already awaiting extraction/credentials; cancel existing admissions/jobs under the `SummaryJobs` registry lock. Unrelated pairs remain available.
- Lock order is **workspace mutation coordination → SummaryJobs lock → SQLite transaction**, consistently on all affected paths; never await or do PDF/archive I/O while holding the registry lock. Summary completion and both admission paths must enter the coordination boundary in that order. Import deduplication/promotion and deletion share the file-lifetime gate: no import can reuse a document that is pending or being deleted. Design the async gate and synchronous registry calls to obey this order; no code may take the gates in reverse. Test against deadlock using handshakes.
- For each item, under the same boundary, recheck its intent, invalidate preparations/jobs for every associated pair, then in one SQLite transaction remove associations (cascading stored summaries) and document row while retaining the journal item and its opaque storage key. No new work can register for removed pairs. After commit, delete the managed original idempotently; a genuinely missing original is already absent, but access/error ambiguity is failure, not success. Mark item `deleted` only after metadata and managed original are absent; otherwise `incomplete` with a sanitized error and retained retry information. Unselected data is untouched. Cancellation of a request does not abandon an admitted deletion; response loss is reconciled by operation ID.
- On startup after migrations and data-directory lock, recover journal items **before** `StorageCleanup.run`. If metadata deletion did not commit, continue from the recorded intent; if committed, retry original removal. Successful recovery records deleted; failure remains visible/retryable. F001 cleanup must exclude pending deletion keys even on recovery failure, never quarantine them. An uncertain SQLite commit is resolved by re-reading durable state; never infer outcome from an exception. A full disk or read-only store can prevent progress: report not completed, retain journal, and allow retry after restart. Existing quarantine and external copies are not claimed erased.

### Client and contracts

Add narrowly scoped application workflows/capabilities, infrastructure adapters and routes; client state guards selection, preview, confirm, response-loss reconciliation and refresh after changes. Portuguese accessible controls use `Texts.fs`; cancellation never sends confirmation. `contracts/http-api.md` defines routes, errors and restart outcome lookup; `contracts/export-format.md` specifies the ZIP. Existing antiforgery on every POST and local host/origin checks remain in force. No sensitive text in logs or errors.

## Project Structure

```text
specs/002-shared-library-export-deletion/{plan,research,data-model,quickstart}.md
specs/002-shared-library-export-deletion/contracts/{http-api,export-format}.md
src/LexScholar.Application/  # narrow export/deletion workflows, SummaryJobs coordination
src/LexScholar.Infrastructure/ # SQLite snapshot, journal migration/repository, ZIP and recovery
src/LexScholar.Server/       # endpoint composition, startup order, shared coordination
src/LexScholar.Client/       # library state, Portuguese UI, error mapping
src/LexScholar.Domain/       # pure impact/outcome types only as needed
tests/LexScholar.{Application,Infrastructure,Server,Browser}.Tests/ # corresponding verification
docs/agents/codebase.md       # update for new pattern/modules during implementation
```

## Test Mapping

| Coverage | Verification |
|---|---|
| FR-001–005, SC-001–002 | Real SQLite archive round-trip inspection with shared/unassociated docs, all metadata and summary citation pages; deterministic concurrent writes vs snapshot, missing/read failures, disk full and disconnect. Inspect ZIP outside app. |
| FR-006–010, SC-003–004 | Preview/cancel, stale impact, shared documents, mixed outcomes; controlled interruption at every durable boundary and restart; storage failure; summary first-generation and replacement admissions/completions racing deletion; reimport races; unrelated data unchanged. |
| FR-011–014, SC-005 | Portuguese browser journeys in Chromium/Firefox; CSRF/Origin/Host tests, no external calls or credential leakage, export/delete independence from project removal. |

## Size and owner decision

The owner explicitly chose **one complete F002 feature and release**, including both export and deletion, and accepted exceeding the normal 10–15-task budget where justified (estimated 15–18). The earlier delivery split proposal is withdrawn. Deliver export to users before enabling destructive deletion, but develop export and test-only deletion internals concurrently after the T002/T003 risk gates where ownership permits. The owner requested maximum safe parallelism: T001 first; parallel T002/T003; then two worktree lanes (export T004–T008 and deletion T009–T016), with explicit cross-lane prerequisites at T013–T015 and one integrator for common files. T017 activates public deletion only after both lanes are verified; T018 is final acceptance. Do not represent F002 or Principle II as complete until both capabilities are delivered and verified. Stop for owner review of the generated tasks before implementation.

## Proposed accepted risks (owner review required)

- Private archive creation temporarily needs free space and can be slow; no large in-memory ZIP. A transport interruption can leave a partial browser download, which must not be presented by LexScholar as completed.
- Cross-store deletion and admission coordination are high-risk: run deterministic race/crash tests before adding UI polish; stop and re-plan if the lock order cannot be implemented without blocking/deadlock.
- A local power failure or external program modifying managed files lies outside application transactional control; avoid promising physical secure erasure or deletion of other copies. Durability details and user-visible recovery are in [research](research.md).
- Startup recovery cannot guarantee progress while filesystem or SQLite is inaccessible: retain journal and show incomplete status, never false success. Existing F001 import quarantine semantics remain intact for unrelated files.
- A request already sent to a summary provider cannot be recalled; cancellation stops later sends but does not erase data received by the provider (F004 accepted risk 10).
