# Feature Specification: Document Summary with Page References

**Feature Branch**: `docs/f003-document-summary`

**Created**: 2026-11-12

**Status**: Draft

**Input**: User description: "Summarize one library document with page references. While reading a document in a project, the researcher asks for a summary of that document. She gets a short summary, in the document's language, of its main points. Every point cites the page(s) it comes from, and she can jump from a citation to that page in the reader to check it." (Full description, including the constitution requirements and scope limits, in the F003 planning request.)

## Clarifications

### Session 2026-11-12

- Q: Where should LexScholar process a document's text to summarize it? → A: Bring your own model: the researcher chooses the summarization service. A service on her own machine needs no confirmation; a service elsewhere is used only after she explicitly confirms that the document's text will leave her machine. (Replaces an earlier "local only" answer.)
- Q: Where does she choose the service? → A: In this feature, in the app's settings file, alongside the data directory and import limits. A settings screen is planned for F004.
- Q: What is the largest document this feature must summarize? → A: Up to 100 pages; longer documents get a clear message that they aren't supported yet. Longer documents are planned for F004. (Replaces an earlier "any length" answer.)
- Q: How long may a summary take for a typical article, and what progress should she see? → A: An article of up to 40 pages within 2 minutes with the service she has configured; progress shown as pages processed out of total.
- Q: If she switches document or project while a summary is being generated, is the work kept? → A: In this feature it is abandoned. Background generation is planned for F004. (Replaces an earlier "continue in the background" answer.)
- Q: How is the work split to fit the size budget? → A: F003 (this spec) is the core summary; F004 adds the settings screen, documents over 100 pages and background generation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize the open document and check each point (Priority: P1)

While reading a document in a project, the researcher asks for a summary of it. She gets a short list of the document's main points, in the document's language. Each point cites the page or pages it comes from. Selecting a citation shows that page in the reader, so she can check the point against the source. The summary is clearly labelled as generated and for her review, and she can dismiss it. The summary is produced by the summarization service she has chosen.

**Why this priority**: It is the first AI assistance in LexScholar and the first capability the researcher would use day to day: it helps her decide quickly what a document says and where, without trusting anything she can't check.

**Independent Test**: With a summarization service configured, open a project document with a text layer, request its summary, and check that every point cites existing pages and that selecting each citation shows the cited page.

**Acceptance Scenarios**:

1. **Given** a document with extractable text is open in the reader and a summarization service is configured, **When** the researcher asks for a summary, **Then** she sees a list of main points in the document's language, each citing at least one page, labelled as a generated summary for her review.
2. **Given** a summary is shown, **When** she selects a citation, **Then** the reader shows the cited page of the same document.
3. **Given** a summary is shown, **When** she dismisses it, **Then** it disappears and nothing about the document, its library record or its project associations has changed.
4. **Given** a document with no extractable text (for example, a scanned PDF without a text layer), **When** she asks for a summary, **Then** she is told plainly that the document has no readable text to summarize, and no summary is shown.
5. **Given** some pages of a document have no extractable text, **When** she asks for a summary, **Then** the summary covers only the pages with text and states which pages could not be read.
6. **Given** the configured service is not on her machine, **When** she asks for a summary, **Then** before any text is sent she is told that the document's text will leave her machine and which service will receive it, and nothing is sent unless she confirms.
7. **Given** no summarization service is configured, **When** she asks for a summary, **Then** she is told that summaries need a service and how to configure one, and nothing else happens.
8. **Given** a summary is being generated, **When** she dismisses it, switches project, or closes the document, **Then** the request is abandoned and no result appears later for a different document.

### Edge Cases

- A generated point cites no page, or cites a page that does not exist in the document: the point is not shown as a source-grounded point (FR-004).
- Too little supportable content to produce any point (very short or mostly non-text document): she is told the document doesn't contain enough readable text for a summary, rather than shown a padded or guessed one.
- The stored file is unavailable (missing, unreadable or not the recorded size, as defined in F001): the same unavailable-document message as the reader; no summary.
- The configured service can't be reached, fails, or returns something unusable: a clear message that the summary could not be produced; she can try again; nothing else changes.
- The document has more than 100 pages: a clear message that documents this long aren't supported yet; nothing is sent to the service (FR-011).
- She declines the confirmation for a service outside her machine: nothing is sent and no summary is produced.
- The document mixes languages: see Assumptions.
- She asks again for the same document while a summary is being generated: no second request is started.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The researcher MUST be able to request a summary of the document currently open in the reader, within the active project.
- **FR-002**: A summary MUST be a short list of the document's main points, written in the document's language.
- **FR-003**: Every point shown MUST cite one or more pages of the document. Page numbers MUST be the document's page positions as the reader shows them (first page = 1), not printed page labels.
- **FR-004**: The system MUST NOT show a point as source-grounded if it cites no page or cites a page outside the document. Such points are omitted, and the summary states how many were omitted.
- **FR-005**: Selecting a citation MUST show the cited page of the same document in the reader.
- **FR-006**: The summary MUST be labelled as generated content for the researcher's review, not as verified fact or as her own text, and MUST be dismissable.
- **FR-007**: When the document has no extractable text, the system MUST say so plainly and MUST NOT produce a summary. When only some pages lack text, the summary MUST state which pages it could not read.
- **FR-008**: With each summary, the system MUST show the context needed to understand and reproduce it: the document, the pages it covered, when it was generated, and which service and model produced it.
- **FR-009**: Summaries MUST NOT be stored. Dismissing a summary, closing the document or switching project discards it, and a request in progress for a document that is no longer open MUST NOT show its result.
- **FR-010**: The researcher MUST be able to choose the summarization service in the app's settings file. When no service is configured, requesting a summary MUST explain how to configure one.
- **FR-011**: The system MUST summarize documents of up to 100 pages. For longer documents it MUST say clearly that they aren't supported yet and MUST NOT send any of their text to the service.
- **FR-012**: While a summary is being generated, the researcher MUST see its progress as pages processed out of the document's total, and MUST be able to dismiss it.
- **FR-013**: Requesting, viewing or dismissing a summary MUST NOT change the document's library record, its stored file, or any project association or role.
- **FR-014**: When the configured service is not on the researcher's machine, the system MUST, before sending any text for each summary, tell her that the document's text will leave her machine and which service will receive it, and MUST send nothing unless she confirms that service (host and model) as currently configured. A service on her machine MUST NOT require this confirmation.

### Key Entities

- **Summarization service** (configured in the settings file): which service and model to use, and where it runs (on the researcher's machine or elsewhere).
- **Document summary** (not stored): the document it describes, its points, the pages it covered, the pages it could not read, the number of omitted points, when it was generated, and the service and model that produced it.
- **Summary point**: one main point of the document, in the document's language, with its cited page numbers.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a reference set of representative, openly licensed documents (including one with no text layer, one with some pages without text, and one with little readable content), 100% of shown points cite pages that exist in their document, and the no-text document always produces the no-text message instead of a summary.
- **SC-002**: In a manual check of points from the reference set, using the service the researcher configures, at least 9 out of 10 are supported by the page they cite.
- **SC-003**: From any citation, the researcher reaches the cited page in one action, and the reader shows that page in 100% of tested citations.
- **SC-004**: In her normal use, the researcher can check any summary point against its cited page in under 30 seconds.
- **SC-005**: Requesting and dismissing summaries leaves the library, stored files and project associations unchanged (verified before and after on the reference set).
- **SC-006**: With a service on her machine configured, summarizing the reference set makes no network connection to any destination outside her machine; with a service elsewhere, no text is sent before she confirms, and nothing is sent when she declines.
- **SC-007**: With the service the researcher configures, a summary of a document of up to 40 pages is shown within 2 minutes of her request (or of her confirmation, for a service elsewhere).

## Assumptions

- Single local researcher, as in F001; the summary works on documents already in the library and open in the reader.
- The researcher (or the owner on her behalf) installs or subscribes to the summarization service herself; LexScholar does not include one. Whether the configured model is good enough is checked by SC-002 and SC-007 on her setup.
- Summary length: by default about 5 to 10 points.
- Mixed-language documents: by default the summary uses the language of most of the document's text.
- Nothing new is stored, so this feature adds no export or deletion obligations under Principle II. Library export and deletion remain tracked for F002 (Codecks `$12y`). The service configuration lives in the existing settings file.
- The reference set for SC-001 and SC-002 uses openly licensed documents in `tests/Fixtures/`, never the researcher's own research PDFs.
- Out of scope: saving or editing summaries, summaries across several documents, questions and answers, highlights, and citation export.
- Planned for F004 (not this feature): a settings screen for choosing the service, documents longer than 100 pages, and generation that continues in the background when she switches document.
- Numbering: this feature is F003 (`specs/003-document-summary`); F002 is reserved for library export and deletion, as referenced by the constitution.
