# Feature Specification: Background summaries

**Feature Branch**: `docs/f004-specify` (spec work); implementation branches follow AGENTS.md

**Created**: 2026-11-14

**Status**: Implemented in v0.3.0; historical specification (see [tasks](tasks.md) and [specification index](../../docs/specification-index.md) for provenance and later changes).

**Input**: User description: "Summaries keep generating in the background." The researcher asks for a summary of a document and keeps working; she can open another document or project while it is generated, and when she comes back she finds the summary finished, its progress, or why it failed. This replaces F003's FR-009 rule that switching document or project abandons a summary in progress (F003 clarification: "Background generation is planned for F004").

## Clarifications

### Session 2026-11-14

- Q: Does a finished summary survive an app restart? → A: Yes. It is stored and survives restarts until she dismisses it, and it is included in the workspace's export and deletion. (Workspace export and full deletion are deferred to F002, $12y; see FR-010.)
- Q: How many summaries may run at once, and what happens when she asks for another? → A: One per document, any number of documents at once; asking again for a document whose summary is running shows its progress instead of starting another.
- Q: Where does she see that a summary is running or finished for a document she isn't viewing? → A: A status marker (running, finished, failed) next to the document in the project's document list, and on the project in the project list when the document is in another project.
- Q: Can she cancel a background summary, and how? → A: Yes, with a "Stop" action on the running summary when she views its document; stopping discards it and nothing more is sent.
- Q: What happens to a summary if its document is removed from the project? → A: Removing the document stops its running summary (nothing more is sent) and deletes its finished summary.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Keep working while a summary is generated (Priority: P1)

The researcher asks for a summary of a document, confirms where it is sent as today, and then opens another document or another project. The summary keeps being generated. When she comes back to the first document, she sees the finished summary with the same page citations, labels and dismiss action as today; or, if it is still running, its progress; or, if it failed, the same failure message she would have seen had she stayed.

**Why this priority**: It is the whole feature. A summary of a long document takes long enough that waiting on the page interrupts her reading; today, leaving the page throws the work away and, on her ChatGPT plan, the usage it cost.

**Independent Test**: Ask for a summary of a document of several pages, switch to another document straight after confirming, wait, return: the finished summary is shown with working page citations. Repeat switching project instead of document. Repeat with a service failure: the failure is shown on return.

**Acceptance Scenarios**:

1. **Given** she asked for and confirmed a summary of document A, **When** she opens document B in the same project before it finishes, **Then** the summary of A keeps being generated, and nothing about A's summary is shown on B.
2. **Given** a summary of A was generated while she was elsewhere, **When** she opens A again, **Then** she sees the finished summary with its page citations, its labels (service used, generated text) and the dismiss action, exactly as if she had stayed.
3. **Given** a summary of A is still being generated, **When** she opens A again, **Then** she sees its progress, and the summary appears when it finishes without her asking again.
4. **Given** the summary of A failed while she was elsewhere (for example her plan's usage limit was reached), **When** she opens A again, **Then** she sees the same failure message and actions as today.
5. **Given** she opened another project while a summary of A was generated, **When** she returns to A's project and opens A, **Then** scenarios 2–4 apply.
6. **Given** a summary is being generated, **When** she signs out of ChatGPT, **Then** a summary running on her plan is cancelled as today and A shows that she was signed out, not a summary.
7. **Given** she has not confirmed sending, **When** she leaves the document while the confirmation is open, **Then** nothing is sent.
8. **Given** a summary of A is running and she is viewing A, **When** she chooses Stop, **Then** the summary is discarded, no further request is sent for it, A shows no summary and its list marker clears, and she can ask again.

### Edge Cases

- The document is too short, has no text or is over 100 pages: refused before sending, as today; nothing runs in the background.
- She dismisses a finished summary: it is gone, as today.
- She asks again for a summary of a document whose summary is running: she sees its progress; no second summary starts, and the running one continues as before. A document with a finished summary shows that summary; she dismisses it to ask for a new one.
- She asks for summaries of several documents in turn: each runs at the same time as the others.
- The document is in two projects: each project has its own summary of it, asked for, shown, stopped and removed separately.
- The document is removed from the project while its summary runs: the summary stops and nothing more is sent. Removed after it finished: the stored summary is deleted with it.
- The app is closed while a summary runs: the running work is lost, and on restart the document shows no summary (she can ask again). A finished summary is still there after a restart.
- A page citation in a summary finished in the background points at the right page when she follows it, and a citation that fails its check is handled as today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Once she has confirmed sending, a summary MUST keep being generated when she opens another document or another project.
- **FR-002**: When she opens a document whose summary is running, the app MUST show its progress and then the summary when it finishes, without her asking again.
- **FR-003**: When she opens a document whose summary finished while she was elsewhere, the app MUST show it with the same page citations, citation checks, labels and dismiss action as a summary she waited for.
- **FR-004**: When she opens a document whose summary failed while she was elsewhere, the app MUST show the same failure message and actions as when she waits.
- **FR-005**: A summary's result, progress or failure MUST only ever be shown on the document it was asked for.
- **FR-006**: Everything F003 and F005 decide before sending MUST stay unchanged: the refusals (no text, too little text, over 100 pages), the confirmation for a service elsewhere and for her ChatGPT plan, and nothing sent to a service elsewhere or through her plan without her confirmation; a local service needs none (F003 FR-014). Leaving a document while a required confirmation is open MUST send nothing.
- **FR-007**: Signing out of ChatGPT MUST cancel any summary running on her plan, as today, including one running for a document she is not viewing.
- **FR-008**: The project's document list MUST show next to each document whether its summary is running, finished or failed, and the project list MUST show on a project that one of its documents has a summary running, finished or failed. The markers MUST update without her reloading.
- **FR-009**: Replaces F003 FR-009. A finished summary MUST be stored and still be shown after the app restarts, until she dismisses it; dismissing it MUST delete it from storage.
- **FR-010**: Stored summaries are derived data under constitution Principle II. In this feature she controls them by dismissing a summary (FR-009) and by removing its document from the project (FR-013). Full workspace export and deletion do not exist yet: they are deferred to F002 (Codecks $12y), which MUST include stored summaries, and this feature's storage MUST NOT block that. This feature MUST NOT be described as giving export or full deletion of summaries.
- **FR-011**: A summary belongs to one document in one project. Each document in a project MUST have at most one summary running at a time; any number of documents, in any projects, may have one running at once. A document shared by two projects has a separate summary in each. Asking for a summary of a document whose summary is running in that project MUST show its progress and MUST NOT start a second summary; the running one continues unchanged.
- **FR-012**: While she views a document whose summary is running, the app MUST offer a Stop action. Stopping MUST end the work, send no further request for it, keep nothing, and clear the document's status marker.
- **FR-013**: Removing a document from a project MUST stop that project's running summary of it, sending nothing more, and MUST delete that project's stored summary of it. Its summary in any other project is unaffected.

### Key Entities

- **Background summary**: one summary asked for one document in one project. It has a state (running with progress, finished with its summary, or failed with its reason) and the service it uses. A finished one is stored, with the summary text, its citations and labels, until she dismisses it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In normal use, she can ask for a summary, switch to another document or project straight after confirming, and on return find the finished summary, in every attempt during manual validation (at least 3 attempts, including one project switch).
- **SC-002**: A summary finished in the background has the same page citations as one she waited for: every citation she follows during manual validation opens the cited page.
- **SC-003**: A failure while she was elsewhere is shown on return with the same message as when she waits (checked at least once, for example with her plan's usage limit or a disconnected service).
- **SC-004**: She never sees a summary, progress or failure on a document other than the one it was asked for during manual validation.
- **SC-005**: Signing out while a summary runs for a document she is not viewing leaves no summary to find on that document, and no new request to her plan after the sign-out. A summary that had already finished before the sign-out may stay (plan, accepted risk 9).
- **SC-006**: Switching documents or projects is no slower with a summary running than without one, as she perceives it.
- **SC-007**: A summary finished before she closes the app is shown on its document after she reopens it, and is gone for good after she dismisses it.
- **SC-008**: While viewing another document, she can tell from the document list which documents have a summary running, finished or failed, and the marker changes when a summary finishes.

## Assumptions

- **Riskiest assumption**: the summary work can be owned by the app independently of the page she is viewing, so that leaving a document neither stops the work nor loses its result, while sign-out still reaches it. Today the work and its result are tied to the open document. **Checked early**: the first implementation slice after the shared setup is a thin end-to-end path (ask, switch document, return, see the result) covered by an automated browser test against the fake service, before any status display or other polish; if it shows the assumption does not hold, stop and re-plan with the owner.
- One researcher, one app instance, as in F001–F005.
- Summary quality, the services, their limits and their messages are unchanged from F003/F005.
- Out of scope (later features): documents over 100 pages, a settings screen, model choice, and the small UX follow-ups from F003/F005's manual validation.
- The five open questions from the first draft are settled in Clarifications (session 2026-11-14).
