# Feature Specification: Shared Library Export and Deletion

**Feature Branch**: `docs/f002-shared-library-spec` (specification work)

**Created**: 2026-11-23

**Status**: Draft — decisions and owner approval required before planning

**Input**: F002, [Codecks $12y](https://thyago-cunha-dev.codecks.io/card/12y): export the shared PDF library and its relevant information, and deliberately delete selected documents and their derived data.

## Clarifications

### Session 2026-11-23

- Q: When permanently deleting a library document that belongs to projects, should LexScholar remove it from all those projects after explicit confirmation? → A: Yes. After confirmation, delete the document, all its project associations and their summaries.
- Q: Must F002 also let the researcher restore an exported library into LexScholar? → A: No. Export for use outside LexScholar; defer in-app restoration.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Take the shared library elsewhere (Priority: P1)

The researcher exports the entire shared library, including documents not currently in any project, so she can keep and use her materials outside LexScholar. The export includes the context needed to understand each document, its project associations and any stored summaries.

**Why this priority**: Access to research materials and their context outside the application is an outstanding user-control obligation.

**Independent Test**: Export a library containing an unassociated PDF and a PDF shared by two projects with different roles and separate stored summaries. Inspect the result outside the application and match the documents, roles, summaries and citations to the originals.

**Acceptance Scenarios**:

1. **Given** documents with and without project associations and all originals readable, **When** the researcher exports the library, **Then** every library PDF and its relevant metadata are included.
2. **Given** one document shared across projects with different roles and summaries, **When** she inspects the export, **Then** she can identify the document, each project and role, and each project's own stored summary and its source references.
3. **Given** a stored summary, **When** she reads it in the export, **Then** its generated status, points, citations, coverage, omissions and available generation context are preserved without presenting it as verified scholarship.
4. **Given** a required original PDF is missing or unreadable, **When** she requests an export, **Then** the export fails, she is informed which material is unavailable, no partial result is presented as complete, and her library is unchanged.
5. **Given** a library association, role or stored summary changes while export runs, **When** the export succeeds, **Then** its documents, associations, roles and stored summaries together represent one logically consistent library state, not a contradictory mixture of states.

### User Story 2 - Permanently delete selected library documents (Priority: P1)

The researcher chooses one or more documents for permanent deletion. Before deciding, she sees the impact on projects and summaries and can cancel. This action is distinct from removing a document from just one project.

**Why this priority**: Removal is the other outstanding user-control obligation and can cause irreversible loss.

**Independent Test**: Select a document shared by two projects with separate summaries, inspect the consequences, cancel and verify nothing changed; then confirm and verify the approved deletion semantics, including after restart.

**Acceptance Scenarios**:

1. **Given** a selected document, **When** the researcher asks to delete it, **Then** she sees its identity, affected projects and stored summaries, the consequences and the permanent nature of deletion before confirming.
2. **Given** the confirmation, **When** she cancels, **Then** the documents, associations and summaries remain unchanged.
3. **Given** a document selected and deletion confirmed, **When** deletion completes, **Then** the document, all its project associations and their stored summaries are deleted, and the researcher receives an accurate result.
4. **Given** a running summary or replacement for a selected document, **When** deletion completes, **Then** work for deleted associations has stopped and cannot recreate a summary; unrelated documents and projects remain unchanged.
5. **Given** deletion of several selected documents partially succeeds or is interrupted, **When** the researcher checks the result or returns after restart, **Then** the app accurately identifies each document as deleted or not completed, does not report the whole selection as deleted, and explains remaining work or failure without silently losing unrelated materials.

### Edge Cases

- Empty library: the researcher is told there is nothing to export or delete.
- A document has no associations; it remains eligible for export and deletion.
- A document's stored file is missing or unreadable: whole-library export fails rather than presenting a partial export as complete; its record is not silently removed, and deletion remains deliberate.
- Project associations or summaries change between the consequence display and confirmation: the researcher must not unknowingly confirm an outdated impact.
- Documents, associations, roles or stored summaries change during export: a successful export represents one consistent library state, not a mixture that never existed. Summary generation or regeneration finishes, fails or is cancelled during deletion: no stale result may resurrect deleted data.
- Deletion of a selection can succeed for some documents and remain incomplete for others; outcomes must distinguish each document and never describe the whole selection as deleted.
- Exports and deletion can fail because storage is unavailable or full; user-facing results must not claim success for incomplete work.
- Copies in earlier exports, original import locations, backups, browser downloads or external services are outside LexScholar's direct control; deletion must not claim to erase them or guarantee physical secure erasure.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The researcher MUST be able to request a local export of the whole shared library, including unassociated documents.
- **FR-002**: A complete export MUST include every library document's original PDF content, identity and stored descriptive metadata; project identity, associations and per-project roles; and every stored summary for those associations. These contents MUST together represent one logically consistent state of the library, even when changes occur during export.
- **FR-003**: Exported summaries MUST retain their document and project identity, generated-for-review disclosure, points and page references, coverage and unreadable pages, omitted-point count, generation date, service and model when stored. The export MUST NOT imply generated content is verified or authored by the researcher.
- **FR-004**: The researcher MUST be able to inspect and use exported content outside LexScholar; the app MUST explain what is included and that in-app restoration is not provided by this feature.
- **FR-005**: An export MUST NOT alter the library, associations, summaries or ongoing summary work. If any required original PDF is missing or unreadable, or any other required material or output cannot be completed, the export MUST fail; the app MUST report the failure, identify unavailable material when applicable, and MUST NOT present a partial result as a complete export.
- **FR-006**: The researcher MUST be able to select one or more shared-library documents for permanent deletion, including documents not associated with a project.
- **FR-007**: Before confirmation the app MUST identify selected documents, list affected projects and indicate affected stored summaries, explain what will be removed and that the action is permanent, and offer cancellation. If the impact changes before confirmation, the app MUST show the updated impact and require confirmation again.
- **FR-008**: For each selected document whose permanent library deletion completes after explicit confirmation, the app MUST remove the document, all its project associations and their stored summaries. Deletion of a selection MAY complete for some documents but not others.
- **FR-009**: For each association actually deleted, running or preparing summaries and replacements MUST be stopped, with no further requests sent for them; late completions MUST NOT recreate deleted data. Stored summaries for those associations MUST be deleted. Unselected documents and their associations and summaries MUST remain unchanged.
- **FR-010**: The app MUST accurately report the outcome of each selected document as deleted or not completed, including if deletion is interrupted or fails; it MUST NOT report the whole selection as deleted unless every selected document was deleted, or report a document as deleted while retaining its active library record or managed original. Incomplete work MUST remain visible or be recoverable after restart without silently removing unrelated data.
- **FR-011**: Deletion MUST explain that external copies, prior exports, backups and data already sent to external services are not deleted by this action, and MUST NOT promise secure physical erasure.
- **FR-012**: Export and deletion MUST be local by default and MUST NOT transmit research materials, metadata or summaries to external services. Export MUST exclude sign-in credentials and unrelated application settings.
- **FR-013**: Existing project-association removal remains distinct: removing an association preserves the shared PDF and other project associations, and removes only that association's summary as in F001 and F004. No behavior changes except those explicitly approved here.
- **FR-014**: User-facing controls and messages MUST follow the existing Brazilian Portuguese interface and avoid revealing sensitive content in errors.

### Key Entities

- **Shared library document**: One managed original PDF and its stored identity and descriptive metadata; it can have zero or more project associations.
- **Project association**: The relationship of a document to a project, including that project's role.
- **Stored summary**: Generated material for a specific project-document pair, including page citations and available generation context; each project has its own.
- **Export**: A researcher-requested local copy of library materials and their context; it is separate from the live library.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a representative library with an unassociated document and one shared by two projects, 100% of PDFs, project roles and stored summaries can be matched to one consistent source state after successful export. In tested concurrent changes, every successful export matches a consistent state.
- **SC-002**: In that export, every tested summary point retains its cited source document and pages, and every summary is identified as generated for review.
- **SC-003**: In both supported browsers, cancellation leaves all selected documents unchanged, and confirmed deletion removes every tested unassociated or shared document and its associations and stored summaries, including after restart.
- **SC-004**: In tested missing or unreadable PDF cases, whole-library export fails and no partial result is reported as complete. In tested partial deletion and interruption cases, each selected document's outcome is accurate and the selection is not reported as fully deleted unless all succeeded; no deleted summary returns, and unrelated data remains available.
- **SC-005**: The researcher can inspect the consequences and cancel before every tested deletion; no export or deletion sends material outside the machine.

## Assumptions

- One researcher on a local workspace; Firefox and Chromium on Linux remain supported.
- F001 retains a shared original after its final association is removed. F004 stores summaries separately per project-document pair; F006 keeps the previous stored summary during regeneration until a replacement succeeds.
- The export format is deliberately not selected here. A portable readable collection favors simple access; a structured, versioned export can preserve relationships more reliably. The chosen format and technical tradeoffs belong in planning; an export must not be represented as restorable in LexScholar.
- Scope excludes in-app restoration, project deletion, automated backups, cloud sync, bulk editing, secure erasure and changing summary generation. Restoration is deferred, not promised by this feature.
- Export/deletion of the managed library cannot retract material already copied elsewhere or previously sent to a service.
- The two stories are intended as one coherent control feature; if planning shows more than roughly 10–15 tasks, stop and propose a split before implementation.
- Principle II's outstanding control obligation remains tracked by $12y and is not represented as satisfied until the approved controls are delivered and verified.
