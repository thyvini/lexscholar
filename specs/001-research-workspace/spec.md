# Feature Specification: Research Workspace and PDF Library

**Feature Branch**: `001-research-workspace`

**Feature ID**: F001

**Created**: 2026-11-04

**Status**: Approved

**Input**: User description: "Create Feature 001 for LexScholar: Research Workspace and PDF Library. Researchers maintain multiple projects in one workspace, import PDFs to a shared library, and associate them with projects using per-project document roles."

## Clarifications

### Session 2026-11-04

- Q: What qualifies two imported PDFs as identical? → A: Their file contents must be byte-for-byte identical, regardless of filename; an identical re-import reuses the shared library document.
- Q: What happens when a shared library document has no project associations? → A: Keep it in the shared library for future association; removing the last association does not delete it.
- Q: How should password-protected PDFs be handled? → A: Reject them with a clear explanation and leave existing library data unchanged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Organize Research Projects (Priority: P1)

As a researcher, I want to create, list, select, and switch among independent research projects in my workspace so I can keep separate research contexts organized.

**Why this priority**: Projects provide the contexts needed to associate and distinguish sources.

**Independent Test**: Create two named projects, view both in the project list, select each in turn, and verify the active project changes without changing the other project's associations.

**Acceptance Scenarios**:

1. **Given** a workspace with no projects, **When** the researcher creates a project with a valid name, **Then** the project appears in the project list and becomes available for selection.
2. **Given** multiple projects, **When** the researcher selects a project, **Then** it becomes the active project and its associated documents are shown.
3. **Given** an active project, **When** the researcher switches to another project, **Then** the second project becomes active and the first project's associations remain unchanged.
4. **Given** a workspace with a project named "Family Arbitration", **When** the researcher attempts to create a project named " family arbitration ", **Then** the application explains that the name is already in use and preserves all existing projects.

---

### User Story 2 - Import and Reuse PDF Documents (Priority: P1)

As a researcher, I want to import PDFs once into a shared library and associate them with one or more projects so I can reuse the same source without creating independent copies for each project.

**Why this priority**: Reuse across projects and preservation of the shared original are central to the requested library model.

**Independent Test**: Import a PDF, associate it with two projects using different roles, and verify that both associations refer to the same library document and the original remains available after removing one association.

**Acceptance Scenarios**:

1. **Given** a supported PDF that can be read, **When** the researcher imports it, **Then** it is added to the shared library and can be associated with a project.
2. **Given** a PDF already in the library, **When** a PDF with byte-for-byte identical file contents is imported again under the same or a different filename, **Then** the existing shared library document is reused and no duplicate library document is created.
3. **Given** one library document associated with multiple projects, **When** the researcher views each project, **Then** the document appears in each project with that project's independently assigned role.
4. **Given** a document associated with a project, **When** the researcher removes that association, **Then** it is absent from that project's document list and the shared original is preserved.

---

### User Story 3 - Read and Classify Project Sources (Priority: P1)

As a researcher, I want to view and read documents in my active project, navigate their pages, and change their project-specific roles so I can work with sources in the context where they are relevant.

**Why this priority**: Reading and classifying sources is the main researcher workflow this feature supports.

**Independent Test**: Open an associated PDF from a project, move between pages, change its role, and verify the role is updated only in that project.

**Acceptance Scenarios**:

1. **Given** an active project with associated documents, **When** the researcher opens a document, **Then** the document's pages can be read and navigated.
2. **Given** a document associated with a project, **When** the researcher assigns Focused Source or Reference Source, **Then** the selected role is displayed for that association.
3. **Given** the same document has different roles in two projects, **When** a role is changed in one project, **Then** the other project's role remains unchanged.

### Edge Cases

- Importing a file that is not a PDF MUST be rejected with a clear explanation; it MUST NOT be added to the library.
- A PDF that is corrupt, unreadable, or otherwise cannot be imported MUST produce a clear failure message and MUST NOT replace or alter any existing library document.
- If an imported PDF becomes unavailable or unreadable, the researcher MUST receive a clear error; the library record and all project associations MUST remain unchanged, and the application MUST NOT automatically delete them.
- Creating a project with a name that duplicates an existing name after ignoring capitalization and surrounding whitespace MUST be rejected with a clear explanation; all existing projects MUST be preserved.
- If the active project has no associated documents, the empty state MUST make clear that no documents are associated with that project.
- Re-importing a PDF with byte-for-byte identical contents MUST reuse the existing library document regardless of filename; a PDF with different bytes MUST NOT be treated as identical under this rule.
- Removing an association MUST preserve the shared original and every other project's association.
- Removing a document's final project association MUST leave the shared library document available for future association with any project.
- Importing a password-protected PDF MUST be rejected with a clear explanation and MUST NOT modify existing library data.
- A role change MUST affect only the selected project-document association.
- Page navigation MUST not move outside the document's available page range.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow a researcher to create, list, select, and switch among research projects in a workspace.
- **FR-002**: The system MUST identify the currently active project and display that project's document associations.
- **FR-003**: The system MUST accept PDF files only for import and MUST clearly reject unsupported file types.
- **FR-004**: The system MUST store each imported PDF as a shared library document that can be associated with multiple projects.
- **FR-005**: The system MUST consider two PDFs identical only when their file contents are byte-for-byte identical, regardless of filename, and MUST reuse the existing shared library document when an identical PDF is re-imported.
- **FR-006**: The system MUST allow a library document to be associated with multiple research projects.
- **FR-007**: The system MUST assign a Document Role independently to each project-document association; valid roles are Focused Source and Reference Source.
- **FR-008**: The system MUST allow the researcher to view the documents associated with the active project and open them for reading with page navigation.
- **FR-009**: The system MUST allow a researcher to change a document's role within a project without changing that document's role in another project.
- **FR-010**: The system MUST allow the researcher to remove a document association from a project without deleting or altering the shared original PDF or other project associations. Removing the final project association MUST leave the document in the shared library available for later association.
- **FR-011**: The system MUST persist projects, shared library documents, and project-document associations between sessions.
- **FR-012**: The system MUST preserve the original imported PDF and provide a clear, actionable failure message when import or opening fails.
- **FR-013**: The system MUST keep each project's active context and associations isolated from other projects, except for intentional reuse of shared library documents.
- **FR-014**: The system MUST use local storage and processing by default. Any external storage or processing MUST require informed, explicit opt-in as required by the constitution.
- **FR-015**: The system MUST reject password-protected PDFs with a clear explanation and MUST NOT modify existing library data during the rejected import.
- **FR-016**: Project names MUST be unique within a workspace when compared without regard to capitalization and after ignoring surrounding whitespace. The system MUST reject an attempted duplicate with a clear explanation and MUST preserve existing projects.
- **FR-017**: If a previously imported PDF becomes unavailable or unreadable, the system MUST inform the researcher and MUST NOT automatically delete its shared library record or change any project associations.

### Key Entities *(include if data involved)*

- **Workspace**: Contains the researcher's projects and shared document library.
- **Research Project**: An independent research context within a workspace.
- **Active Project**: The project currently selected by the researcher.
- **Document**: A PDF imported once and held in the shared library; its original content is preserved.
- **Project Document**: The association between one shared Document and one Research Project, including its project-specific role.
- **Document Role**: The classification assigned to a Project Document; either Focused Source or Reference Source.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a usability evaluation, at least 90% of researchers can create two projects, switch between them, and identify which one is active without assistance.
- **SC-002**: In a test set of 100 supported PDFs that are valid and readable, at least 99% import successfully or produce a clear, correct failure explanation.
- **SC-003**: In all tested cases, re-importing a byte-for-byte identical PDF, regardless of filename, creates zero additional shared library documents; PDFs with different file contents are not treated as identical by this rule.
- **SC-004**: In all tested multi-project cases, changing a role or removing an association in one project leaves every other project's role and association unchanged.
- **SC-005**: After closing and reopening the application in 100 consecutive persistence checks, all projects, library documents, and associations remain available and consistent.
- **SC-006**: At least 90% of researchers in a usability evaluation can open an associated PDF and navigate to a specified page without assistance.
- **SC-007**: Every tested import or opening failure displays an understandable message and leaves the original PDF and unrelated library data unchanged.

## Assumptions

- The feature supports one researcher's workspace; multi-user access and collaboration are outside this feature.
- A workspace is available when the researcher first uses project management; workspace creation and deletion behavior are not specified here.
- PDFs are the only supported document format in this feature.
- A PDF is a duplicate only if its complete file contents are byte-for-byte identical; filenames do not affect identity.
- Password-protected PDFs are unsupported and rejected without changing existing library data.
- A library document remains stored and available even when it has no project associations.
- Project names are required and must distinguish projects within the same workspace; rename and project deletion behavior are not specified because they were not requested.
- The application follows the constitution's local-first storage and processing default; external processing requires informed, explicit user opt-in.
- AI features, OCR, full-text search, bibliography integration, and collaboration are out of scope.
