# Feature Specification: Document Summary with Page References

**Feature Branch**: `docs/f003-document-summary`

**Created**: 2026-11-12

**Status**: Draft

**Input**: User description: "Summarize one library document with page references. While reading a document in a project, the researcher asks for a summary of that document. She gets a short summary, in the document's language, of its main points. Every point cites the page(s) it comes from, and she can jump from a citation to that page in the reader to check it." (Full description, including the constitution requirements and scope limits, in the F003 planning request.)

## Clarifications

### Session 2026-11-12

- Q: Where should LexScholar process a document's text to summarize it? → A: Local only: no document text leaves the researcher's machine.
- Q: What is the largest document the first version must summarize, and what happens beyond it? → A: Any document the library accepts, up to its import page limit; long documents may be processed in parts.
- Q: How long may a summary take for a typical article, and what progress should she see? → A: An article of up to 40 pages within 2 minutes on her machine; progress shown as pages processed out of total.
- Q: If she switches document or project while a summary is being generated, is the work abandoned or kept? → A: It continues in the background; the result waits in memory while the app runs (never saved) and is shown when she opens that document again.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize the open document and check each point (Priority: P1)

While reading a document in a project, the researcher asks for a summary of it. She gets a short list of the document's main points, in the document's language. Each point cites the page or pages it comes from. Selecting a citation shows that page in the reader, so she can check the point against the source. The summary is clearly labelled as generated and for her review, and she can dismiss it.

**Why this priority**: It is the first AI assistance in LexScholar and the first capability the researcher would use day to day: it helps her decide quickly what a document says and where, without trusting anything she can't check.

**Independent Test**: Open a project document with a text layer, request its summary, and check that every point cites existing pages and that selecting each citation shows the cited page.

**Acceptance Scenarios**:

1. **Given** a document with extractable text is open in the reader, **When** the researcher asks for a summary, **Then** she sees a list of main points in the document's language, each citing at least one page, labelled as a generated summary for her review.
2. **Given** a summary is shown, **When** she selects a citation, **Then** the reader shows the cited page of the same document.
3. **Given** a summary is shown, **When** she dismisses it, **Then** it disappears and nothing about the document, its library record or its project associations has changed.
4. **Given** a document with no extractable text (for example, a scanned PDF without a text layer), **When** she asks for a summary, **Then** she is told plainly that the document has no readable text to summarize, and no summary is shown.
5. **Given** some pages of a document have no extractable text, **When** she asks for a summary, **Then** the summary covers only the pages with text and states which pages could not be read.
6. **Given** a summary is being generated, **When** she switches to another document or project, **Then** generation continues; **When** she opens that document again while the app is still running, **Then** she sees its progress or its finished summary.
7. **Given** a summary is being generated, **When** she dismisses it, **Then** generation stops and its result is discarded.

---

### Edge Cases

- A generated point cites no page, or cites a page that does not exist in the document: the point is not shown as a source-grounded point (FR-004).
- Too little supportable content to produce any point (very short or mostly non-text document): she is told the document doesn't contain enough readable text for a summary, rather than shown a padded or guessed one.
- The stored file is unavailable (missing, unreadable or not the recorded size, as defined in F001): the same unavailable-document message as the reader; no summary.
- Generation fails or can't run (for example, the summarization capability is unavailable): a clear message that the summary could not be produced; she can try again; nothing else changes.
- A very long document (hundreds of pages): it is summarized in full, possibly in parts (FR-011); she sees progress while she waits (FR-012).
- The document mixes languages: see Assumptions.
- She asks again for the same document while its summary is being generated or waiting: no second request is started; she sees the existing one.
- The app is closed while summaries are being generated or waiting: they are lost; nothing is left half-written (FR-009).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The researcher MUST be able to request a summary of the document currently open in the reader, within the active project.
- **FR-002**: A summary MUST be a short list of the document's main points, written in the document's language.
- **FR-003**: Every point shown MUST cite one or more pages of the document. Page numbers MUST be the document's page positions as the reader shows them (first page = 1), not printed page labels.
- **FR-004**: The system MUST NOT show a point as source-grounded if it cites no page or cites a page outside the document. Such points are omitted, and the summary states how many were omitted.
- **FR-005**: Selecting a citation MUST show the cited page of the same document in the reader.
- **FR-006**: The summary MUST be labelled as generated content for the researcher's review, not as verified fact or as her own text, and MUST be dismissable.
- **FR-007**: When the document has no extractable text, the system MUST say so plainly and MUST NOT produce a summary. When only some pages lack text, the summary MUST state which pages it could not read.
- **FR-008**: With each summary, the system MUST show the context needed to understand and reproduce it: the document, the pages it covered, when it was generated, and which summarization method and version produced it.
- **FR-009**: Summaries MUST NOT be saved. A summary in progress or finished MUST be kept in memory while the app runs, including after the researcher switches document or project, and shown when she opens that document again (in any project). Dismissing a summary discards it; closing the app discards all of them.
- **FR-010**: All processing of document text MUST happen on the researcher's machine. No document text, and nothing derived from it, may leave the machine.
- **FR-011**: The system MUST summarize any document the library accepts, up to the library's import page limit. Long documents MAY be processed in parts, but the summary MUST cover the whole document, and every point MUST still cite the pages it comes from.
- **FR-012**: While a summary is being generated, the researcher MUST see its progress as pages processed out of the document's total, and MUST be able to dismiss it.
- **FR-013**: Requesting, viewing or dismissing a summary MUST NOT change the document's library record, its stored file, or any project association or role.
- **FR-014**: At most one summary MUST be generated at a time. A request made while another summary is being generated MUST be shown as waiting its turn, and the researcher can dismiss it.

### Key Entities

- **Document summary** (kept in memory while the app runs, never saved): its state (waiting, in progress with pages processed, finished, failed), the document it describes, its points, the pages it covered, the pages it could not read, the number of omitted points, when it was generated, and the summarization method and version.
- **Summary point**: one main point of the document, in the document's language, with its cited page numbers.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a reference set of representative, openly licensed documents (including one with no text layer, one with some pages without text, and one with little readable content), 100% of shown points cite pages that exist in their document, and the no-text document always produces the no-text message instead of a summary.
- **SC-002**: In a manual check of points from the reference set, at least 9 out of 10 are supported by the page they cite.
- **SC-003**: From any citation, the researcher reaches the cited page in one action, and the reader shows that page in 100% of tested citations.
- **SC-004**: In her normal use, the researcher can check any summary point against its cited page in under 30 seconds.
- **SC-005**: Requesting and dismissing summaries leaves the library, stored files and project associations unchanged (verified before and after on the reference set).
- **SC-006**: Summarizing every document in the reference set makes no network connection to any destination outside the researcher's machine.
- **SC-007**: On the researcher's machine, a summary of a document of up to 40 pages is shown within 2 minutes of her request; longer documents take roughly proportionally longer.

## Assumptions

- Single local researcher, as in F001; the summary works on documents already in the library and open in the reader.
- Summary length: by default about 5 to 10 points; revisit in `/speckit-clarify` if needed.
- Mixed-language documents: by default the summary uses the language of most of the document's text; revisit in `/speckit-clarify` if needed.
- Nothing new is stored, so this feature adds no export or deletion obligations under Principle II. Library export and deletion remain tracked for F002 (Codecks `$12y`).
- The reference set for SC-001 and SC-002 uses openly licensed documents in `tests/Fixtures/`, never the researcher's own research PDFs.
- Out of scope: saving or editing summaries, summaries across several documents, questions and answers, highlights, citation export, and choosing the summarization method in the UI.
- Numbering: this feature is F003 (`specs/003-document-summary`); F002 is reserved for library export and deletion, as referenced by the constitution.
