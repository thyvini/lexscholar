# Feature Specification: Summary first, in Portuguese

**Feature Branch**: `docs/f006-summary-first-portuguese` (spec work); implementation branches follow AGENTS.md

**Created**: 2026-11-16

**Status**: Implemented in v0.4.0; historical specification (see [tasks](tasks.md) and [specification index](../../docs/specification-index.md) for provenance and later changes).

**Input**: User description: "Rework the workspace around the summary, without the built-in reader, and translate the interface into Brazilian Portuguese." The researcher is a Brazilian law master's student who reads whole documents in her own PDF software and uses LexScholar day to day for summaries (F003, F004, F005).

**Owner clarification ($153, T016)**: All new summaries are in Brazilian Portuguese regardless of source language. This supersedes F003 FR-002 and its majority-language assumption, and F006's earlier deferral of Portuguese summary content to F007. Existing stored summaries are preserved, not translated or regenerated.

## Clarifications

**Owner-directed scope extension (2026-11-19, T023)**: The optional commentary/instruction before a first summary (screen 08) is now in F006. Regeneration/feedback remains a separate upcoming slice. This supersedes the original F007 deferral of this field only.

**Owner-approved feedback extension (T025)**: Existing summaries now offer optional free-text feedback to regenerate, with fresh explicit sending consent. Feedback uses the existing bounded instruction pipeline in every generation stage. The current summary stays available through running work, failure, cancellation and navigation; only successful validated completion replaces it atomically. Cancellation is nondestructive. Feedback is transient, not separately saved or logged. This supersedes the earlier regeneration/feedback deferral; ratings, flags and history remain out of scope.

### Session 2026-11-16

- Q: Does "Excluir" ask for confirmation or delete at once? → A: It asks first ("Excluir" / "Cancelar"); nothing is deleted on "Cancelar".
- Q: Where does she see that a document's stored file is missing or unreadable when she opens it or a citation? → A: The new tab shows a short Portuguese error page; the library record and project are unchanged.
- Q: Where does she change a document's role once the reader is gone? → A: In the project's document list, on each document.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The summary first, PDFs in a browser tab (Priority: P1)

When the researcher opens a document in a project, the main area shows its summary. If the document has no summary yet, the main area shows the request to summarize it; if one is generating, its progress. The built-in reader is gone. She opens the whole document in a new browser tab with "Open PDF", and each page citation in the summary is a link written like a reference (*p. 3*, *pp. 4–6*) that opens the same document in a new tab at the cited page. Her ChatGPT plan is the only summary service the interface offers. "Dismiss" becomes "Delete".

**Why this priority**: The summary is what she uses LexScholar for; the in-app reader duplicates the better PDF software she already uses and pushes the summary aside.

**Independent Test**: In Firefox and in Chromium, open a project document with a stored summary: the summary fills the main area. Select a citation *p. 3*: a new tab shows page 3 of the same document. Select "Open PDF": a new tab shows the document. Open a document with no summary: the request to summarize it is shown, offering only her ChatGPT plan. Delete the summary: it is gone after a restart.

**Acceptance Scenarios**:

1. **Given** a document in the active project has a stored summary, **When** she opens it, **Then** the main area shows the summary with its generated-content label, its context (F003 FR-008) and its citations, and no in-app page view.
2. **Given** a document has no summary and none is generating, **When** she opens it, **Then** the main area shows the request to summarize it, with F003, F004 and F005 pre-send rules unchanged.
3. **Given** a summary of the document is generating, **When** she opens it, **Then** the main area shows its progress and Stop (F004).
4. **Given** she has opened a document, **When** she selects "Open PDF", **Then** the document opens in a new browser tab and the summary stays where it was in the app.
5. **Given** a summary point cites page 3, **When** she selects its citation *p. 3*, **Then** a new tab shows page 3 of that same document; for a range *pp. 4–6* the tab opens at page 4.
6. **Given** the settings file configures a summary service, **When** she requests a new summary, **Then** only her ChatGPT plan is offered. Historical summaries retain their recorded service host, model, date and context, without ChatGPT-plan attribution for configured-service summaries.
7. **Given** a stored summary is shown, **When** she selects "Delete" and confirms, **Then** the summary is deleted from storage as F004 FR-009 describes, and the request to summarize the document is shown again.
8. **Given** a summary failed, **When** she selects "Fechar", **Then** the failure is removed at once, with no confirmation, and the request is shown again.
9. **Given** a document has no summary, **When** she enters an optional instruction and requests a summary, **Then** the instruction accompanies the document through generation, including long-document reductions, without changing Portuguese output or page-citation checks. Leaving it blank keeps the default main-points behavior.
10. **Given** a draft instruction, **When** she cancels send confirmation, **Then** nothing is sent and the draft remains editable; changing document/project clears it. The confirmation discloses that the optional instruction is sent too.

---

### User Story 2 - The interface in Brazilian Portuguese (Priority: P1)

Every text she sees is in Brazilian Portuguese, with Brazilian dates and times (14/11/2026 18:54): labels, buttons, statuses, confirmations, empty states, errors and failure messages, including messages that come from the server. There is no language choice. The terms in FR-012 are used consistently.

**Why this priority**: She works in Portuguese; an English interface slows her down and makes failure messages harder to act on.

**Independent Test**: Walk through every screen and state (projects, library, adding documents, summary request, confirmation, generating, ready, failed, usage limit, signed out) and each server failure that can be provoked in a test workspace: no English text appears, the glossary terms are used, and dates read as dd/mm/aaaa, hh:mm.

**Acceptance Scenarios**:

1. **Given** any screen or state of the app, **When** she views it, **Then** every visible text is in Brazilian Portuguese.
2. **Given** an operation fails on the server (for example an invalid PDF on import, or a summary that cannot be completed), **When** the failure is shown, **Then** its message is in Brazilian Portuguese and says what happened and what she can do.
3. **Given** a date or time is shown (for example when a summary was made), **When** she reads it, **Then** it reads as 14/11/2026 18:54: dd/mm/aaaa, a space, then 24-hour hh:mm, with no comma.
4. **Given** she is not signed in, **When** she is offered sign-in, **Then** the button reads "Continuar com o ChatGPT", and the confirmation's help link opens the Portuguese version of OpenAI's help article.
5. **Given** a summary is shown, **When** she reads its label, **Then** it reads "Gerado para sua revisão. Não verificado."
6. **Given** a document in English, Portuguese or mixed languages, **When** she requests a new summary, **Then** its points are in Brazilian Portuguese, including for documents summarized in chunks; existing stored summaries remain unchanged.

---

### Edge Cases

- A citation to the last page, or a single-page document: the tab opens at that page.
- She selects several citations in a row: each opens its own tab at its own page; none reuses a tab that shows a different page.
- The same document is in two projects: citations always open that document, whatever project is active.
- She deletes a summary while another document's summary is generating: only the deleted one is affected.
- A summary is generating when she opens "Open PDF": generation continues (F004).
- Her browser blocks new tabs: the browser's own notice applies; the app does not work around it.
- A summary stored before this feature, with points and omitted points: shown with the new citation links, unchanged otherwise.
- A summary stored before this feature through the settings-file service: its context retains the recorded service host (`generated.ServiceHost`), model and date, alongside the existing context. This historical provenance is an exception to hiding configured-service UI; it never receives ChatGPT-plan attribution.
- A server message with no Portuguese text (an unforeseen failure): she sees a generic Portuguese failure message, never English or technical text.
- The document's own text is unchanged. New summaries are in Brazilian Portuguese regardless of source language; existing stored summary content is unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Replaces F001 FR-008. She MUST be able to view the documents of the active project and open one; opening a document MUST show its summary in the main area, or the request to summarize it when it has none, or its progress when one is generating. The app MUST NOT offer an in-app page view of a document.
- **FR-002**: An "Open PDF" action MUST open the opened document in a new browser tab.
- **FR-003**: Replaces F003 FR-005. Each page citation MUST be a link written like a reference: *p. N* for one page, *pp. N–M* for a range. Selecting it MUST open the same document in a new browser tab at page N. A citation MUST NOT open another document or another page (constitution Principle I).
- **FR-004**: The summary, its generated-content label and its context MUST stay visible in the app while she checks citations in browser tabs (constitution Principle III).
- **FR-005**: Replaces F003 FR-010 and F005 FR-002 as far as new requests in the interface go. The interface MUST offer only her ChatGPT plan for new summaries and MUST hide configured-service choices and settings-file explanations. Historical summary provenance is an explicit exception: retain the recorded service host, model, date and existing context (FR-014), without attributing configured-service summaries to her ChatGPT plan. The settings file and the backend's support for it are unchanged.
- **FR-006**: Replaces F003 FR-006's "dismiss" wording. On a finished summary, the action that removes it MUST be called "Excluir" (Delete) and MUST delete it from storage as F004 FR-009 describes. Before deleting it MUST ask her to confirm, offering "Excluir" and "Cancelar"; "Cancelar" MUST leave the summary unchanged. On a failed summary, "Fechar" MUST remove the failure at once, with no confirmation, as F004's dismissal does.
- **FR-007**: When the stored file of a document is missing or unreadable (F001 FR-017) and she selects "Open PDF" or a citation, the app MUST inform her and MUST NOT change the library record or project associations. The new tab MUST show a short Portuguese page saying the document's file was not found or cannot be read, and no technical error.
- **FR-008**: She MUST be able to change a document's role in the active project (Fonte principal / Fonte de referência) from that document's entry in the project's document list, with the same rules as before; the action moves there from the removed reader.
- **FR-009**: Every text the interface shows MUST be in Brazilian Portuguese: labels, buttons, statuses, confirmations, empty states, errors and failure messages, including messages that originate on the server. A server failure with no specific Portuguese message MUST be shown as a generic Portuguese failure message, never as English or technical text.
- **FR-010**: Dates MUST be shown as dd/mm/aaaa (14/11/2026) and times as 24-hour hh:mm (18:54); a date with its time reads "14/11/2026 18:54", with no comma (mockup 06 shows a comma; this requirement wins).
- **FR-011**: The interface MUST NOT offer a language choice.
- **FR-012**: The interface MUST use these terms consistently: Projetos; Biblioteca; Fonte principal / Fontes principais (focused source); Fonte de referência / Fontes de referência (reference source); Adicionar documentos; Resumo; Resumir este documento; Resumir; Resumo pronto; Parar; Excluir; Abrir PDF; Cancelar; "Gerado para sua revisão. Não verificado."; Conectado ao ChatGPT; Usando seu plano ChatGPT (replaces F005 FR-006's English label); Gerenciar uso; Enviar documentos ao ChatGPT?; Enviar e resumir; Continuar com o ChatGPT (the sign-in button); citations p. 3, pp. 4–6.
- **FR-013**: Replaces F005 FR-010's link. The confirmation before sending MUST link to the Portuguese version of OpenAI's help article: https://help.openai.com/pt-br/articles/20001542-using-your-chatgpt-plan-in-other-apps-and-sites
- **FR-014**: Everything else F003, F004 and F005 decide MUST stay unchanged: refusals before sending, the confirmation once per sign-in, citation checks and omitted points, background generation, status markers, Stop, sign-out cancelling, and the context shown with a summary.
- **FR-016**: New summaries MUST be written in Brazilian Portuguese regardless of the source document's language, including mixed-language and chunked documents. Existing stored summaries MUST NOT be translated or regenerated by this change. This replaces F003 FR-002's source-language requirement.
- **FR-015**: All behavior in this feature MUST be checkable in Firefox and Chromium, including a citation opening the cited page (constitution Principle IV).
- **FR-017**: Before a first summary, she MAY enter commentary/instructions in the optional "O que você quer desta fonte?" field (screen 08). The field MUST be bounded to 2,000 characters; blank means the existing default summary. Invalid instructions MUST NOT silently produce an uninstructed summary. Instructions MUST accompany all generation stages (single, map, intermediate and final combine), as user input subordinate to Portuguese, source-grounding, output-format and citation rules. Existing summaries MUST NOT be regenerated or changed by this slice.
- **FR-018**: Treat the instruction as sensitive research input: no instruction logging or separate persistence; no recent-instruction history. The field MUST explain that the instruction is sent with the document; existing send confirmation MUST disclose it too, without changing once-per-sign-in consent. Cancel preserves the editable draft; navigating to another document/project clears it. The draft is transient, so reloading loses it. Generated summaries can reflect the requested focus and remain stored under existing rules.

### Key Entities

- **Page citation link**: a reference to one page or a page range of the summarized document; it identifies that document and its first cited page.
- **Interface text**: every text the app shows, in Brazilian Portuguese, with the FR-012 glossary as the source of truth for its terms.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In both supported browsers, 100% of tested citation links open the same document at the cited page, and none opens another document or page.
- **SC-002**: Opening a document with a stored summary shows the summary with no further action, in one step.
- **SC-003**: A walk through every screen, state and provocable server failure in both browsers finds no English text and no glossary term used inconsistently.
- **SC-004**: Every date and time shown follows dd/mm/aaaa and hh:mm.
- **SC-005**: With a settings-file service configured, new requests offer only her ChatGPT plan, with no configured-service choice or settings-file explanation. Historical configured-service summaries still show their recorded host, model, date and context, never ChatGPT-plan attribution.
- **SC-006**: All F003, F004 and F005 acceptance scenarios not replaced here still pass, with the Portuguese texts.

## Assumptions

- OpenAI's guidelines label the sign-in button "Continue with ChatGPT" and give no official Portuguese wording (checked 2026-11-16 in OpenAI's Sign in with ChatGPT documentation; the Portuguese help article could not be read automatically). "Continuar com o ChatGPT" is the direct translation and is used; the button keeps OpenAI's approved branding.
- Brazilian date and time formats apply whatever the browser or system language is.
- Her PDF opens in the browser's own PDF viewer in the new tab; she can save it or open it in her own PDF software from there.
- A range citation opens at its first page.
- New summary content is in Brazilian Portuguese (FR-016); stored summary content retains its original language.
- Out of scope in T023: regenerating, feedback, flagging points, rating a summary, instruction history/shortcuts and displaying saved instructions on summaries. Also out of scope: workspace export and deletion (F002), a settings screen, documents over 100 pages, and quotes as evidence.
- Size: two P1 stories. If tasks exceed about 15, US2 (Portuguese) moves to its own feature, per AGENTS.md.
