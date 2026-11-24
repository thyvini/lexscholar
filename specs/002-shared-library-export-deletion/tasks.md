# Tasks: Shared Library Export and Deletion

**Input**: [spec](spec.md), [plan](plan.md), [research](research.md), [data model](data-model.md), [HTTP contract](contracts/http-api.md), [export format](contracts/export-format.md), [quickstart](quickstart.md). **Feature**: F002, hero [$12y](https://thyago-cunha-dev.codecks.io/card/12y). **Delivery**: one complete feature/release, one feature/release with two controlled work lanes; owner accepted exceeding 10–15 tasks. Do not treat export alone as F002 completion or Principle II compliance.

## Execution rules

Each numbered item is one independently verifiable slice and normally one PR. T001 is shared setup; T002 and T003 are independent risk-first proofs. `[P]` means parallel work only after all listed prerequisites merge, with disjoint file ownership in separate worktrees. Use up to two implementation agents (A: export, B: deletion internals) plus a non-writing reviewer/coordinator. Branch from the latest `release/0.5.0`, update on merged dependencies, and designate one integrator for shared `.fsproj` registration, `Endpoints.fs` composition and `docs/agents/codebase.md`. Never edit common files concurrently: queue integration-only changes or serialize commits. Do not introduce packages or generic frameworks.

For each automated slice, write task-tagged xUnit v3 tests (`Trait("Task", "Txxx")`) first; run `scripts/check.sh Txxx` and record a meaningful failing assertion (RED), implement and rerun for GREEN. For already-existing behavior, demonstrate test sensitivity by temporarily breaking it rather than claiming a false RED. Before PR rerun only if changes since GREEN; CI runs the full suite, zero skips. Add `evidence/txxx-<topic>.md` with RED/GREEN commands, one-line results and CI run; include environment, injected crash points and limitations for storage, concurrency, startup, security and data-loss work. Tick the task with its evidence link only when verified. Each slice has a concise Codecks subcard under $12y with its ID and durable link to this file on `release/0.5.0`; never create duplicates. Owner validation uses explicit manual evidence rather than manufactured RED.

**Visibility gate:** Before T008, export construction is internal/test-only; no public endpoint or UI may report a successful whole-library export before T004–T007 pass. Before T017, production confirmation/admission and retry must fail closed without persisting new intents or deleting data, and production startup recovery must not execute deletion; exercise admission, retry and recovery only through isolated test-only seams and temporary workspaces. Do not expose a confirmation action or endpoint that actually removes user data until T009–T016 pass and T017 enables the complete controls. At T017 enable production admission, retry and startup recovery together; after that, previously admitted deletions recover on restart as specified in the plan. Export is independently testable after T008; deletion internals are testable through T016, and the complete user journey after T017. The PR sequence is not a proposal to split F002.

## Phase 1 — Shared setup

- [x] T001 Establish shared F002 composition and schema without enabling destructive operations in `src/LexScholar.Application/LibraryControls.fs`, `src/LexScholar.Infrastructure/Migrations/LibraryDeletions.fs`, `src/LexScholar.Infrastructure/WorkspaceDb.fs`, `src/LexScholar.Infrastructure/Migrations/WorkspaceDbModelSnapshot.fs`, `src/LexScholar.Server/Endpoints.fs`, relevant `.fsproj` files and `docs/agents/codebase.md`; add `tests/LexScholar.Infrastructure.Tests/LibraryDeletionSchemaTests.fs` and registration. Verify real migration/restart, unique operation/document identity, valid pending/metadata_removed/deleted/incomplete states, and that no existing workspace behavior or public destructive action changes. [Evidence](evidence/t001-foundations.md)

## Phase 2 — Risk-first architectural checkpoints (parallel after T001)

- [x] T002 [P] Prove one-connection SQLite export snapshot behavior in `src/LexScholar.Infrastructure/LibraryExportSnapshot.fs`, `src/LexScholar.Infrastructure/SummaryRepository.fs` and `tests/LexScholar.Infrastructure.Tests/LibraryExportSnapshotTests.fs`: establish the snapshot by an initial read; deterministically interleave document, role, association and saved-summary writes on a second connection and prove one pre- or post-change state, including replacement and unassociated documents; measure writer progress/locking using handshakes. If a coherent snapshot with supported SQLite configuration cannot be demonstrated, stop and return to owner before T004. [Evidence](evidence/t002-snapshot.md)
- [x] T003 [P] Prove file-lifetime and mutation/registry lock order in `src/LexScholar.Application/LibraryControls.fs`, `src/LexScholar.Application/ImportDocument.fs`, `src/LexScholar.Application/SummaryJobs.fs`, `src/LexScholar.Server/Endpoints.fs`, `src/LexScholar.Server/Program.fs`, `src/LexScholar.Server/LibraryEndpoints.fs`, `src/LexScholar.Server/DocumentEndpoints.fs`, `tests/LexScholar.Server.Tests/TestHarness.fs`, `tests/LexScholar.Application.Tests/LibraryCoordinationTests.fs` and `tests/LexScholar.Server.Tests/LibraryCoordinationContractTests.fs`: use the actual shared boundary in the host; deterministically interleave import, export-file lease, summary completion/admission and simulated deletion, asserting workspace mutation → registry → SQLite order, no registry lock over await/PDF I/O and no deadlock. No destructive library deletion is exposed. Stop and re-plan with owner if order or progress cannot be demonstrated; document SQLite journal-mode observations and evidence. [Evidence](evidence/t003-coordination.md)

## Phase 3 — US1: Take the shared library elsewhere (P1; lane A)

**Independent test:** Export a fixture library with an unassociated document and one shared across two projects with distinct roles and summaries; inspect the ZIP outside LexScholar, matching original bytes, all associations, context and cited pages to one consistent state. Depends on T001–T003.

- [x] T004 [P] [US1] Deliver a local complete ZIP export in `src/LexScholar.Application/LibraryExport.fs`, `src/LexScholar.Infrastructure/LibraryExport.fs`, `src/LexScholar.Infrastructure/LibraryExportSnapshot.fs`, `src/LexScholar.Server/LibraryControlEndpoints.fs`, `src/LexScholar.Server/Endpoints.fs` and `tests/LexScholar.Server.Tests/LibraryExportContractTests.fs`: internal contract test of POST `/api/library/export` builds a private archive (public success remains disabled until T008) with each original once and exact v1 `manifest.json` document/project/association fields and generated UUID-based PDF paths; verify shared/unassociated PDFs, byte equality, empty-library 409 and path safety (FR-001/002/004, SC-001). [Evidence](evidence/t004-private-export.md)
- [ ] T005 [P] [US1] Preserve readable and structured summaries in `src/LexScholar.Infrastructure/LibraryExport.fs`, `src/LexScholar.Infrastructure/SummaryRepository.fs` and `tests/LexScholar.Infrastructure.Tests/LibraryExportSummaryTests.fs`: verify every manifest/text field in `contracts/export-format.md`, per-project separation, one-based page citations, generation context, unreadable/omitted pages, Portuguese generated/unverified notice, source paths, and stored old version during regeneration; unknown stored version fails rather than omits (FR-002/003/004, SC-001/002).
- [ ] T006 [P] [US1] Protect complete exports and lifecycle in `src/LexScholar.Infrastructure/LibraryExport.fs`, `src/LexScholar.Infrastructure/DocumentStore.fs`, `src/LexScholar.Infrastructure/StorageCleanup.fs`, `src/LexScholar.Server/LibraryControlEndpoints.fs` and `tests/LexScholar.Server.Tests/LibraryExportFailureTests.fs`: missing/unreadable/wrong-length/read error identifies the document, disk-full/construction error never sends successful ZIP, streaming uses bounded memory, private staging is cleaned on error, disconnect and restart, and live jobs/library are unchanged (FR-005, SC-004).
- [ ] T007 [P] [US1] Verify coordinated export races in `src/LexScholar.Application/ImportDocument.fs`, `src/LexScholar.Infrastructure/LibraryExport.fs`, `src/LexScholar.Server/LibraryControlEndpoints.fs` and `tests/LexScholar.Server.Tests/LibraryExportRaceTests.fs`: deterministic barriers show imported/promoted/deleted file lifetime is protected and concurrent role/association/summary changes produce a coherent archive; summary work is not cancelled, and lock acquisition follows T003 without waits under the registry lock (FR-002/005, SC-001).
- [ ] T008 [P] [US1] Enable the complete public export endpoint and provide Portuguese export action and feedback in `src/LexScholar.Server/LibraryControlEndpoints.fs`, `src/LexScholar.Client/Library.fs`, `src/LexScholar.Client/App.fs`, `src/LexScholar.Client/Texts.fs`, `src/LexScholar.Client/FailureTexts.fs`, `tests/LexScholar.Browser.Tests/LibraryExportStateTests.fs` and `tests/LexScholar.Browser.Tests/LibraryExportJourneyTests.fs`; verify Firefox/Chromium download, empty/failure messaging, failed transfer not shown as success, included material and no in-app restore explanation, no external network or credentials/settings in export (FR-001/004/005/012/014, SC-002/004/005).

## Phase 4 — US2: Permanently delete selected library documents (P1; lane B)

**Independent test:** With two projects sharing a document and separate summaries, preview/cancel and verify no changes; confirm the refreshed impact, verify originals, associations and summaries removed and all item outcomes correct after restart. Unassociated documents and unrelated data must also be checked. Start internal/test-only deletion work after T002 and T003 while export is being developed. Keep production destructive processing fail-closed until all T008–T016 prerequisites pass; enable it in T017.

- [ ] T009 [P] [US2] Provide a consistent non-destructive impact preview in `src/LexScholar.Application/LibraryDeletion.fs`, `src/LexScholar.Infrastructure/LibraryDeletionRepository.fs`, `src/LexScholar.Server/LibraryDeletionEndpoints.fs` (test-only mapping until T017) and `tests/LexScholar.Server.Tests/LibraryDeletionPreviewTests.fs`: POST preview validates nonempty unique UUIDs and unknown IDs, returns selected names, project names/roles, per-project stored-summary presence and an opaque revision including summary content/version; no mutation, paths or summary text (FR-006/007/011/014).
- [ ] T010 [P] [US2] Atomically admit confirmed selections in `src/LexScholar.Application/LibraryDeletion.fs`, `src/LexScholar.Infrastructure/LibraryDeletionRepository.fs`, `src/LexScholar.Infrastructure/LibraryRepository.fs`, `src/LexScholar.Server/LibraryDeletionEndpoints.fs` (test-only mapping) and `tests/LexScholar.Server.Tests/LibraryDeletionAdmissionTests.fs`; reuse summary capabilities without concurrently editing T005-owned `SummaryRepository.fs`: compare full revision and selection under T003 boundary, persist operation and all items before destructive work, return fresh preview on changed project/role/summary with no intent; reject overlap, handle same-ID retry/idempotency versus different-selection conflict, unknown/duplicate IDs and concurrent tabs; block pending-item associations and summary mutations (FR-007/010). Exercise admission only through isolated test-only seams; production admission must fail closed without persisting intents until T017.
- [ ] T011 [P] [US2] Fence first-generation and regeneration preparations/jobs in `src/LexScholar.Application/SummaryJobs.fs`, `src/LexScholar.Server/SummaryEndpoints.fs`, `src/LexScholar.Application/LibraryDeletion.fs` and `tests/LexScholar.Server.Tests/LibraryDeletionSummaryRaceTests.fs`: register both preparation types before awaits; deterministic extraction/credential/service handshakes prove pending intent blocks late admission, cancels running jobs and prevents further sends or late save, including replacement, completion winning before intent and unrelated jobs; retain T003 lock order (FR-009).
- [ ] T012 [P] [US2] Implement per-document durable metadata deletion in `src/LexScholar.Application/LibraryDeletion.fs`, `src/LexScholar.Infrastructure/LibraryDeletionRepository.fs`, `src/LexScholar.Infrastructure/LibraryRepository.fs` and `tests/LexScholar.Infrastructure.Tests/LibraryDeletionTransactionTests.fs`: after T010/T011, atomically delete one document's associations and cascading summaries then its row, recording `metadata_removed` and storage key; exercise success, transaction rollback, uncertain commit re-read, unassociated/shared cases, per-item partial failure and unchanged unselected data (FR-008/009/010). Keep production destructive action disabled until T017.
- [ ] T013 [P] [US2] Finish managed-original removal in `src/LexScholar.Infrastructure/DocumentStore.fs`, `src/LexScholar.Infrastructure/LibraryDeletionRepository.fs`, `src/LexScholar.Application/LibraryDeletion.fs` and `tests/LexScholar.Infrastructure.Tests/LibraryDeletionFileTests.fs`: unlink only validated managed keys, sync directory before marking deleted, distinguish proven missing from permission/I/O failure, retry idempotently; assert no deleted outcome while row, association, summary or original remains, and never remove other files (FR-008/010).
- [ ] T014 [P] [US2] Recover interrupted deletions before orphan cleanup in `src/LexScholar.Infrastructure/LibraryDeletionRecovery.fs`, `src/LexScholar.Infrastructure/StorageCleanup.fs`, `src/LexScholar.Server/Program.fs` and `tests/LexScholar.Server.Tests/LibraryDeletionRecoveryTests.fs`: terminate/fault before and after intent, metadata commit, unlink, directory sync and completion marker; restart same isolated workspace, re-read uncertain commits, resume idempotently, retain inaccessible items as incomplete and exclude their keys from quarantine while preserving F001 cleanup for unrelated files (FR-010, SC-004). Run destructive recovery only through a test-only seam until T017; production startup must not execute deletion before then.
- [ ] T015 [P] [US2] Enforce reimport and association safety in `src/LexScholar.Application/ImportDocument.fs`, `src/LexScholar.Infrastructure/LibraryRepository.fs`, `src/LexScholar.Application/LibraryDeletion.fs`, `src/LexScholar.Server/LibraryControlEndpoints.fs` and `tests/LexScholar.Server.Tests/LibraryDeletionImportRaceTests.fs`: deterministic holds across intent, deduplication, promotion and unlink prove no pending item is reused/reassociated or late summary admitted, later reimport produces a valid new record, and unrelated documents work. Verify confirmed per-document processing internally with no partial admission; keep public destructive processing disabled until T017 (FR-008–010/013).
- [ ] T016 [P] [US2] Implement durable result, retry and acknowledgement in `src/LexScholar.Application/LibraryDeletion.fs`, `src/LexScholar.Infrastructure/LibraryDeletionRepository.fs`, `src/LexScholar.Server/LibraryDeletionEndpoints.fs` and `tests/LexScholar.Server.Tests/LibraryDeletionOutcomeTests.fs`: per-item deleted/not_completed, running/partial/complete, list and lookup after restart, retry and completed-only acknowledgement, client-generated operation UUID and identical-request retry after lost response/404; verify 202, partial success, storage failures and no false complete (FR-010, SC-004). Test destructive retry only through isolated test-only seams; production retry fails closed until T017.
- [ ] T017 [US2] Deliver accessible Portuguese selection, confirmation and result journeys in `src/LexScholar.Client/Library.fs`, `src/LexScholar.Client/App.fs`, `src/LexScholar.Client/Texts.fs`, `src/LexScholar.Client/FailureTexts.fs`, `src/LexScholar.Server/LibraryControlEndpoints.fs`, `src/LexScholar.Server/Program.fs`, `tests/LexScholar.Browser.Tests/LibraryDeletionStateTests.fs` and `tests/LexScholar.Browser.Tests/LibraryDeletionJourneyTests.fs`: select one/many including unassociated, preview shared projects/summaries and external-copy caveats, cancel without mutation, reconfirm changed impact, report each outcome/retry across navigation/reload, prevent misleading success on uncertain request; enable production confirmation/admission, retry and startup recovery together only after T009–T016 pass; preserve association-removal behavior in Firefox and Chromium (FR-006–011/013/014, SC-003–005).

## Phase 5 — Cross-cutting owner validation

- [ ] T018 Verify security, privacy and owner acceptance in `tests/LexScholar.Server.Tests/LibraryControlsSecurityTests.fs`, `tests/LexScholar.Browser.Tests/LibraryControlsJourneyTests.fs`, `specs/002-shared-library-export-deletion/quickstart.md` and `specs/002-shared-library-export-deletion/evidence/t018-owner-validation.md`: task-tagged sensitivity checks and regressions for Host/Origin/CSRF on every new POST, no external calls for export/deletion, no sensitive data/paths in errors/logs or secrets/settings in ZIP; owner manually follows quickstart in Firefox and Chromium using only fixture PDFs and isolated temporary workspaces, checks citations, partial failure/recovery and cancellation, records environment/results and CI link. No real research material (FR-011–014, SC-001–005).

## Dependencies, parallel lanes and merge discipline

T001 merges first. T002 and T003 may run concurrently, but both are stop-and-replan gates. When they pass, lane A (T004–T008) and test-only lane B (T009–T016) can proceed in separate worktrees. Explicit cross-lane dependencies protect shared storage modules. T017 enables destructive operations only after both lanes complete. T018 verifies the whole feature.

| Task | Prerequisites (merged) | Lane |
|---|---|---|
| T001 | none | foundation |
| T002 | T001 | foundation |
| T003 | T001 | foundation |
| T004 | T002, T003 | A — export |
| T005 | T004 | A — export |
| T006 | T005 | A — export |
| T007 | T006 | A — export |
| T008 | T007 | A — export |
| T009 | T002, T003 | B — deletion |
| T010 | T009 | B — deletion |
| T011 | T010 | B — deletion |
| T012 | T011 | B — deletion |
| T013 | T012, T006 | B — deletion |
| T014 | T013, T006 | B — deletion |
| T015 | T014, T007 | B — deletion |
| T016 | T015 | B — deletion |
| T017 | T008, T016 | integration |
| T018 | T017 | integration |

**Parallel-file safety:** `[P]` does not authorize concurrent changes to the same file. T009/T010/T016 own `LibraryDeletionEndpoints.fs` (test-only mapping) instead of export's `LibraryControlEndpoints.fs`. Serialize shared `.fsproj`, `Endpoints.fs`, `docs/agents/codebase.md` and any accidental collisions through a designated integrator. T013 waits for T006 (DocumentStore); T014 waits for T006 (StorageCleanup); T015 waits for T007 (ImportDocument). Branch from current release head, incorporate merged dependencies before final PR review, and keep task-specific RED/GREEN, CI and GPT review. Replan rather than weaken T002/T003 gates.

**Visibility:** Public export only at T008. Production deletion admission, retry and startup recovery remain fail-closed/test-only through T016; T017 activates them together after all safeguards and export UI exist. These parallel lanes do not split F002 or its release.

## Requirements and acceptance coverage

| Requirement | Task-tagged tests / owner validation |
|---|---|
| FR-001 | T004, T008, T018 |
| FR-002 | T002, T004, T005, T007, T018 |
| FR-003 | T005, T008, T018 |
| FR-004 | T004, T005, T008, T018 |
| FR-005 | T006, T007, T008, T018 |
| FR-006 | T009, T017, T018 |
| FR-007 | T009, T010, T017, T018 |
| FR-008 | T012, T013, T015, T017, T018 |
| FR-009 | T011, T012, T015, T018 |
| FR-010 | T010, T012–T016, T018 |
| FR-011 | T009, T017, T018 |
| FR-012 | T008, T018 |
| FR-013 | T015, T017, T018 |
| FR-014 | T008, T009, T017, T018 |
| SC-001 | T002, T004, T005, T007, T018 |
| SC-002 | T005, T008, T018 |
| SC-003 | T014, T017, T018 |
| SC-004 | T006, T011, T013, T014, T016, T018 |
| SC-005 | T008, T017, T018 |

## Risk and boundary notes

- SQLite snapshot/writer behavior and workspace→registry→SQLite lock ordering are unproven assumptions until T002/T003 pass. T011/T014/T015 validate real admissions, crashes and reimport beyond the early checkpoint.
- A failed browser transfer can leave an incomplete file outside LexScholar's control; no claim that it is a completed export. External same-length PDF modification is not caught by F001 availability checks; stored import hash is not a fresh integrity assertion.
- Local power/hardware failure, inaccessible storage and already-sent third-party data are bounded by the plan's proposed accepted risks; do not promise physical secure erasure or external-copy removal. The owner has not separately accepted each risk merely by approving the task budget.
- No restore, cloud sync, project deletion, broad operation framework, new package or changed summary-generation semantics. F001 project-association removal remains distinct.

## Codecks subcards

T001 $15c · T002 $15e · T003 $15f · T004 $15g · T005 $15h · T006 $15i · T007 $15j · T008 $15k · T009 $15o · T010 $15q · T011 $15r · T012 $15s · T013 $15u · T014 $15v · T015 $15w · T016 $15x · T017 $15y · T018 $15z
