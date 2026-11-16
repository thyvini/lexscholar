# Implementation Plan: Summary first, in Portuguese

**Branch**: `docs/f006-summary-first-portuguese` | **Date**: 2026-11-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/006-summary-first-portuguese/spec.md`; look and texts from [mockups/README.md](mockups/README.md) (screens 04, 05, 06 and 08; screen 07 and the F007 parts listed there are not built).

## Summary

The client is reorganized around the summary. Three columns (projects, the active project's documents, main area) replace the single column, and the main area shows the opened document's summary, its request or its progress. The built-in reader goes: "Abrir PDF" and each citation (`p. 3`, `pp. 4–6`) are links to the existing content URL in a new tab, with `#page=N` for a citation. The content endpoint answers a browser navigation for a missing file with a short Portuguese page. Role changes move into the document list. "Excluir" asks before it deletes. Only her ChatGPT plan is offered. Every text comes from one client module, `Texts.fs`, in Brazilian Portuguese. Server failures become Portuguese through the client's existing code-to-case mapping, and dates use a fixed `dd/MM/yyyy HH:mm`. The server changes only in the content endpoint's navigation answer and a module rename. Decisions: [research.md](research.md); data: [data-model.md](data-model.md); interfaces: [contracts/](contracts/); validation: [quickstart.md](quickstart.md).

## Technical Context

**Language/Version**: F# on .NET 10 (SDK pinned in `global.json`)

**Primary Dependencies**: existing only (ASP.NET Core, Bolero, Playwright for tests). **No new packages.** New static assets: Inter and Source Serif 4 variable WOFF2 fonts (SIL OFL), served locally (R8).

**Storage**: unchanged. No schema change, no migration, no new files in the data directory.

**Testing**: xUnit v3. Client state tests for `Library` (opened document, role change), `Summary` (plan only, delete confirmation) and `Texts` (citation and date formats). Server contract tests for the content endpoint's HTML answer. Playwright journeys in Firefox and Chromium, selecting by role and accessible name from `Texts` (R5). Manual check in both browsers that a citation shows the right page (R1).

**Target Platform**: Linux, Firefox and Chromium.

**Project Type**: local web application (ASP.NET Core host + Bolero WebAssembly client).

**Performance Goals**: opening a document with a stored summary shows it in one step (SC-002); no new request on open (the reader's content check is removed).

**Constraints**: host security unchanged (loopback, Host/Origin, antiforgery); API problem codes unchanged; backend settings-file service unchanged (FR-005); no external requests added (fonts local); F007 parts of the mockups not built.

**Scale/Scope**: one researcher; about 120 interface strings; 4 mockup screens; ~70 English test selectors migrated as their screens change.

## Constitution Check

*Checked before research and again after design: passes, no exceptions.*

| Principle | How the design meets it |
|---|---|
| I. Integrity, verifiable evidence | A citation link's document is the opened document and its page is the first page of the citation's validated pages; nothing else builds the URL (R1). The journey tests check both the `href` and the request of the opened tab in both browsers; citation checks and omitted points are unchanged (FR-014). |
| II. Privacy and control | No new data leaves the machine; fonts are served locally rather than from a CDN (R8). The confirmation before sending is unchanged except its language and the Portuguese help link (FR-013). Deleting a finished summary now asks first (FR-006); closing a failed one ("Fechar") stays immediate and still deletes it from storage; F002 tracking (`$12y`) unchanged. |
| III. Human oversight | The label "Gerado para sua revisão. Não verificado." stays at the top of every summary; the summary stays visible in the app while she checks pages in browser tabs (FR-004). |
| IV. Testability, reproducibility | Every behavior has a state, contract or journey test in both browsers; the visual page check is manual in both browsers and recorded as evidence (R1). The summary's context (service, model, date) stays, with the Brazilian date format. |
| V. Simplicity | Removes a module (`Reader.fs`) and the content pre-check; one texts module instead of a localization framework (R3); no new packages; the server changes in one endpoint. |
| VI. Decisions recorded | [research.md](research.md) R1–R9. |

## Project Structure

### Documentation (this feature)

```text
specs/006-summary-first-portuguese/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R9
├── data-model.md        # client state changes; no storage change
├── quickstart.md        # validation in both browsers
├── contracts/
│   ├── document-content.md   # content endpoint: navigation answer for a missing file
│   └── interface-texts.md    # glossary, citation and date formats, F006/F007 boundary
├── mockups/             # reference screens (owner, 2026-11-16)
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/LexScholar.Client/
├── Texts.fs              # NEW, compiled first: every interface text, citation and date formats, failure messages
├── Projects.fs           # texts only
├── Library.fs            # + Opened document, + role change (from Reader.fs)
├── Reader.fs             # REMOVED
├── Summary.fs            # followReader → followOpened; plan-only destination; delete confirmation
├── ChatGpt.fs            # texts only
├── SummaryStatus.fs      # unchanged logic
├── App.fs                # three-column layout, main area, links, views use Texts
└── wwwroot/
    ├── index.html        # lang="pt-BR"
    ├── app.css           # rewritten around the tokens
    └── fonts/            # NEW: Inter, Source Serif 4 (WOFF2) + OFL texts
src/LexScholar.Server/
├── ReaderEndpoints.fs    # RENAMED DocumentEndpoints.fs; HTML answer for navigations
└── Endpoints.fs, LexScholar.Server.fsproj   # rename registration
tests/LexScholar.Browser.Tests/
├── Ui.fs                 # NEW: role/name helpers over Texts, shared journey setup
├── TextsTests.fs         # NEW: citation and date formats, glossary literals
├── WorkspaceJourneyTests.fs  # NEW: open document → main area, Abrir PDF, citations, roles
├── Reader*Tests.fs       # REMOVED (cases moved, R6)
└── other *JourneyTests.fs    # selectors migrated when their screen changes
tests/LexScholar.Server.Tests/
└── DocumentContentContractTests.fs   # NEW or extended: navigation answer
docs/agents/codebase.md   # updated: Texts, Library opened/role, DocumentEndpoints, fonts
```

**Structure Decision**: existing projects only. One new client module (`Texts.fs`), one removed (`Reader.fs`), one server module renamed.

## Task outline (for /speckit-tasks)

13 tasks, in the 10–15 budget, so no split (R9). US1 slices write their texts straight into `Texts.fs` in Portuguese; US2 slices cover the screens and messages US1 doesn't touch.

| # | Slice | Story | Main files |
|---|---|---|---|
| T001 | Shared pieces: `Texts.fs` (glossary, `pages`/`pageRange`, `dateTime`), `Ui.fs` test helpers, tokens and fonts in `app.css`/`fonts/`, `lang="pt-BR"` | — | Texts.fs, Ui.fs, TextsTests.fs, app.css, index.html, fonts/ |
| T002 | Three-column layout and top bar (screen 06 frame): projects sidebar, project document list with sections "Fontes principais"/"Fontes de referência" and status markers, main area empty state | US1 | App.fs (layout), app.css |
| T003 | Opening a document fills the main area with its summary, request or progress; `Opened` and the role-change state in `Library.fs`; `Reader.fs` and its tests removed | US1 | Library.fs, Summary.fs, App.fs, LibraryStateTests, WorkspaceJourneyTests |
| T004 | "Abrir PDF" and citations as `p. N`/`pp. N–M` links in a new tab at the page; both browsers | US1 | App.fs, WorkspaceJourneyTests |
| T005 | Missing file: Portuguese page in the new tab (navigation answer); rename `ReaderEndpoints` → `DocumentEndpoints` | US1 | DocumentEndpoints.fs, Endpoints.fs, Server fsproj, DocumentContentContractTests, WorkspaceJourneyTests |
| T006 | Role change in the document list (state moved into Library.fs by T003) | US1 | App.fs, WorkspaceJourneyTests |
| T007 | Only her ChatGPT plan offered; settings-file texts and choice gone | US1 | Summary.fs, App.fs, SummaryStateTests, PreSend/Summary journeys |
| T008 | "Excluir" with confirmation ("Excluir"/"Cancelar") on a finished summary; "Fechar" stays immediate on a failed one | US1 | Summary.fs, App.fs, SummaryStateTests, BackgroundSummaryJourneyTests |
| T009 | Projects, library and import in Portuguese, first-run screen (05) layout and texts, their failures | US2 | Texts.fs, App.fs, Project/Library journeys |
| T010 | Summary and ChatGPT texts: request (08, without the instruction), confirmation (04) with the pt-BR help link, progress, ready, failures with generic fallback, "Usando seu plano ChatGPT · Gerenciar uso" | US2 | Texts.fs, App.fs, ChatGpt journeys |
| T011 | Dates in `dd/MM/yyyy HH:mm` wherever shown; the remaining texts moved to `Texts`. SC-003 (no English) is checked by hand, quickstart step 9 | US2 | Texts.fs, App.fs, TextsTests.fs |
| T012 | `docs/agents/codebase.md` updated; quickstart manual validation in Firefox and Chromium (citation shows the page) | — | codebase.md, evidence |
| T013 | Owner smoke check with her real ChatGPT plan, recorded | — | evidence |

T002 blocks T003–T011 (same `App.fs` view), except T005, which needs only T001 (server and its own test files). T009 and T010 touch different views and can run in parallel after T008.

## Complexity Tracking

No constitution violations.
