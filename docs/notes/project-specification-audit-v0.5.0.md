# Project-wide specification audit — v0.5.0

**Purpose:** Give the owner and independent reviewers enough context to challenge the
findings and reason about proportionate solutions before deciding what to change.
This document is an audit snapshot, not an approved implementation plan or a change
to the constitution.

**Baseline:** `v0.5.0`, merge commit `4297d7919d9f6845b6b774cac3a30f9ed30fe7b3`.
The audit was performed on the same source tree as that release. Relative file paths
below are from the repository root. Tracker state is a point-in-time observation.

**Method and limits:** A static, risk-focused review of the six feature specs, plans
and task lists, the constitution, selected contracts and evidence, and implementation
paths relevant to the findings. This is not a line-by-line audit of all source code,
an independent execution of all acceptance scenarios, or proof that every requirement
is satisfied. No tests were run for this audit. The
[release CI run](https://github.com/thyvini/lexscholar/actions/runs/45479289118/job/138747772722)
passed; historical evidence is identified below rather than presented as a fresh run.

## How to use this document

For each finding, separate:

1. **Observation:** what the cited artifacts or code actually say or do.
2. **Impact and uncertainty:** what follows, and what has not been established.
3. **Options:** possible resolutions, not approved requirements.
4. **Decision:** the owner's choice; do not treat a recommendation as approval.

Reviewers should check the cited files on the current branch before relying on this
snapshot. Give each finding an explicit verdict: **must fix**, **accepted risk**, or
**overkill**. Do not implement changes, alter the constitution, run participant
studies, or expand feature scope solely because this document suggests an option.

## Context: how the features fit together

| Feature | Current intent and important later changes |
|---|---|
| F001 — research workspace and PDF library | Local projects and shared PDFs; per-project roles and associations. F006 replaced its in-app reader with browser-tab PDF access. F002 later added whole-library export and selected-document deletion. |
| F003 — document summary | Page-cited generated summaries. F004 later stores finished summaries and runs jobs in the background; F006 changes the interface and requires new summaries in Brazilian Portuguese regardless of source language. |
| F005 — ChatGPT sign-in | Sign in and use the researcher's plan with explicit sending consent. F006 offers only the plan for new requests in the UI, while retaining the configured-service backend and historical provenance. |
| F004 — background summaries | Jobs continue while the researcher navigates; finished summaries persist. Stop, association removal and sign-out have defined effects on running work. |
| F006 — summary-first Portuguese UI | Summary in the main area, PDF/citations in new tabs, Portuguese text, optional first-summary instruction, and owner-approved feedback/regeneration extensions. |
| F002 — library export and deletion | ZIP of library documents and their associated context; previewed and confirmed deletion, durable per-document outcomes, retry and restart recovery. In-app restoration is excluded. |

Old specifications and evidence can describe a rule that was correct at the time
but was deliberately superseded. A difference is not a regression unless the later
change lacks approval or the current behavior breaks the resulting requirements.

## Findings and decisions

### A1 — User-facing privacy guide incorrectly promises no external processing

- **Proposed severity:** Critical, constitution alignment. **Proposed verdict: must fix.**
- **Observation:** `docs/local-workspace.md:40–48` says the application uses no
  cloud services and sends nothing elsewhere. Current summary requests can send
  document text and optional instructions to OpenAI after the relevant consent.
  The guide's data-directory inventory also predates stored summaries and ChatGPT
  sign-in credentials. The consent behavior is described in
  `specs/005-chatgpt-sign-in/spec.md` and
  `specs/006-summary-first-portuguese/spec.md`; `src/LexScholar.Client/Texts.fs`
  contains the current in-app disclosure.
- **Impact:** The guide can give a researcher a false understanding of where
  sensitive material is processed, contrary to constitution Principle II's
  user-facing data-handling requirement. This does not establish that the app
  sends text without the required in-app consent.
- **Options:** Update the existing guide to distinguish local storage from optional
  external processing, describe stored summaries and sign-in data, and explain
  sign-out and external data controls; or replace it with a clearly linked current
  guide. Do not invent claims about OpenAI retention or training.
- **Decision needed:** Preferred documentation location and level of detail; no
  change to the application's consent policy is implied by fixing the guide.

### A2 — Export description implies all projects are included

- **Proposed severity:** High. **Proposed verdict: must fix.**
- **Observation:** `src/LexScholar.Client/Texts.fs:31` says the export includes
  “todos os PDFs, projetos, vínculos, papéis e resumos salvos”.
  `src/LexScholar.Infrastructure/LibraryExportSnapshot.fs:26–30` includes only
  projects referenced by exported associations. This matches the explicit scope of
  `specs/002-shared-library-export-deletion/contracts/export-format.md:14–18`:
  empty/unassociated projects are not exported.
- **Impact:** The UI can lead someone to expect empty projects in the ZIP. The
  implementation is not inconsistent with the existing export-format contract.
- **Options:** (1) Narrow the UI wording to projects associated with exported
  documents and update the corresponding browser assertion; or (2) expand the
  format and implementation to include all projects, with an explicit product
  decision and appropriate tests.
- **Decision needed:** Is the contract's existing library-focused scope the desired
  behavior, or should an export also contain empty projects?

### A3 — Fresh regeneration consent is enforced by the UI but not independently by the server

- **Proposed severity:** Medium. **Proposed verdict: must resolve the inconsistency.**
- **Observation:** The owner-approved F006 feedback extension and
  `specs/006-summary-first-portuguese/contracts/summary-regeneration.md:5–10`
  describe fresh confirmation for each regeneration. The normal client opens
  consent before `RegenerateSummary` (`src/LexScholar.Client/Summary.fs:370–377`).
  The server passes the request's `ConfirmSend` to the existing once-per-sign-in
  check (`src/LexScholar.Server/SummaryEndpoints.fs:263–276` and
  `src/LexScholar.Application/ChatGptAccess.fs:281–294`). Once the sign-in is
  already confirmed, the latter does not require a fresh `confirmSend`.
- **Impact and boundary:** This is a contract/enforcement ambiguity for callers of
  the protected local endpoint. It is **not** evidence that the ordinary UI
  bypasses its fresh-consent dialog or that the initial sign-in consent is absent.
- **Options:** (1) Make fresh confirmation a server-side precondition for new
  regeneration admissions, defining its response and tests without disturbing
  existing-job behavior; or (2) explicitly define it as a UI guarantee while
  documenting that the backend retains once-per-sign-in authorization.
- **Decision needed:** Which boundary is intended? Consider the privacy principle,
  supported clients, existing CSRF/Origin protections and implementation cost.

### A4 — F002 tracker hero does not reflect the released feature

- **Proposed severity:** Medium. **Proposed verdict: must fix tracking.**
- **Observation:** At audit time, Codecks hero
  [$12y](https://thyago-cunha-dev.codecks.io/card/12y) was `not_started`, while
  its F002 subcards were done and F002 was merged in v0.5.0.
- **Impact:** Tracker and released state disagree. The agent that closed the final
  subcard did not reconcile the hero.
- **Options:** Verify current tracker state and mark the hero done if the owner
  considers the feature complete. Do not alter unrelated cards.
- **Decision needed:** Whether to perform the tracker reconciliation now.

### A5 — F001 formal usability acceptance was deferred, not passed

- **Proposed severity:** Medium. **Proposed verdict: accepted risk; owner decision.**
- **Observation:** `specs/001-research-workspace/tasks.md:81–82` leaves T046
  unchecked. `specs/001-research-workspace/verification.md:260–269` says the
  original SC-001/SC-006 participant-based evaluations were deferred to beta,
  not passed or waived. F001's technical work has nevertheless been released
  and its hero is done. Current `AGENTS.md` says success criteria should be
  checkable by the one researcher and not to plan participant studies unless
  the owner asks.
- **Impact:** “F001 complete” must not be interpreted as proof that the original
  formal usability thresholds passed. This is an evidence/acceptance issue, not
  proof of missing project or PDF functionality.
- **Options:** (1) Retain the historical deferral and clearly distinguish technical
  completion from full formal acceptance; or (2) propose explicit owner-checkable
  replacement criteria, documenting the change and its effect on historical claims.
- **Decision needed:** Which acceptance standard should apply going forward?
  Do not silently mark the original evaluation as passed or arrange a study.

### A6 — Real-document summary accuracy evidence is weak

- **Proposed severity:** Medium. **Proposed verdict: accepted risk; reassess if useful.**
- **Observation:** `specs/003-document-summary/evidence/t012-manual-validation.md:15–36`
  records 9 of 10 sampled points supported on two fixture articles, but explicitly
  notes the repeated passages make the check weak. `src/LexScholar.Domain/Summaries.fs:194–206`
  validates citation page membership and output shape, not semantic entailment.
  The current interface labels generated summaries unverified.
- **Impact:** Valid page references do not prove the cited page supports a point.
  The existing evidence should not be generalized to a real-world accuracy rate.
- **Options:** Keep the disclosed limitation; or have the owner check a modest
  sample from a representative openly licensed document, recording the model,
  source, page references and results without committing private research material.
  A check would measure that setup, not guarantee future model outputs.
- **Decision needed:** Is stronger evidence useful now? Automated semantic
  verification is not an existing requirement implied by this finding.

### A7 — Current data-control guidance still says F002 is undelivered

- **Proposed severity:** Medium. **Proposed verdict: must fix current-facing guidance.**
- **Observation:** `docs/local-workspace.md:63–73` says library export and
  document deletion are unavailable. `.specify/memory/constitution.md:29–31`
  tracks them as outstanding until delivered and verified. F002 has since been
  released with owner acceptance. Historical feature records also contain their
  original deferrals.
- **Impact:** Current guidance conflicts with the delivered controls. Historical
  statements can remain useful as historical records if labelled as such.
- **Options:** Correct the user guide; separately propose an explicit constitution
  tracking update with the required version/date and impact review; add historical
  pointers where readers could otherwise mistake old deferrals for current state.
- **Decision needed:** Approve any constitution update separately. Delivery of
  these controls does not by itself prove every aspect of Principle II is met.

### A8 — Feature status and requirement precedence are hard to determine

- **Proposed severity:** Low. **Proposed verdict: must fix proportionately.**
- **Observation:** `specs/002-shared-library-export-deletion/spec.md:7` says Draft,
  while its plan identifies the spec as approved. F003–F006 also retain Draft
  headers despite implementation. Superseding rules are recorded, but the earlier
  specs are not uniformly annotated: for example, F004 replaces F003's no-storage
  rule and F006 replaces its source-language rule and reader behavior.
- **Impact:** Another reviewer or model can mistake historical requirements or
  status for current requirements, creating false regressions or missed changes.
- **Options:** Add a short current-behavior/supersession index; correct misleading
  status headers with provenance; or add narrowly scoped notices linking affected
  requirements to their successors. Preserve historical decisions and evidence.
- **Decision needed:** Which minimal navigation aid is preferred? Avoid a wholesale
  rewrite or declaring old acceptance evidence to be new evidence.

### A9 — F002 acceptance evidence should identify owner attestation explicitly

- **Proposed severity:** Low. **Proposed verdict: must fix provenance wording.**
- **Observation:** The owner confirmed that checklist steps passed in both browsers,
  including step 4 after the fixture timestamp repair. The documenting agent did
  not observe the manual runs or collect separate per-step notes.
  `specs/002-shared-library-export-deletion/evidence/t018-owner-validation.md:80–107`
  lists individual rows as passed and identifies installed browser versions as
  unconfirmed run versions. F006's evidence explicitly labels comparable blanket
  acceptance as owner-attested.
- **Impact:** Individual rows can be mistaken for independently observed test
  records. This does not invalidate the owner's acceptance.
- **Options:** Label the table and scope as owner-attested, preserve the timestamp
  workaround and CI link, and distinguish observed environment from details
  confirmed for each run. Do not invent per-step data.
- **Decision needed:** Approve a documentation correction; do not require repeating
  tests merely to fix wording unless the owner wants to.

## Other known boundaries

| Boundary | Evidence and proposed verdict |
|---|---|
| A lost initial-summary POST response can race a dismissal before server admission; a job may still start and its marker can be delayed. | F004 plan accepted risk 11, `specs/004-background-summaries/plan.md:25`. F002 fences preparations already registered, but a request not yet received cannot be cancelled by that registration. **Accepted risk**; revisit if unwanted sends or cost warrant a stronger protocol. |
| Managed PDFs modified outside the application without a size change are not detected on open/export. | F001 plan `:21, :56` and F002 research R2. **Accepted risk**; do not claim fresh cryptographic verification. |
| Export is not in-app restore; project deletion, OCR, search and summaries over 100 pages are outside completed feature scope. | F002 spec `:104`; F004 spec `:93`; F001 spec `:149–153`. **Not missing implementations** unless a later approved requirement says otherwise. |
| Local-model network observation and some live-provider failure states were not manually exercised. | F003 `evidence/t012-manual-validation.md:34–36`; F005 `evidence/t012-manual-validation.md:18, :26`. Automated/fake-service coverage exists for stated cases. **Accepted evidence limitations**, not fresh real-service passes. |

## Completion and coverage snapshot

| Feature | What the current evidence supports | Qualification |
|---|---|---|
| F001 | Core workspace/library implementation and historical automated/corpus evidence; 100 of 100 corpus PDFs imported. | T046 and the original participant-based criteria remain deferred; see A5. |
| F002 | All 18 tasks checked; owner accepted manual checklist in both supported browsers; feature released. | A2, A4, A7 and A9 remain tracking/disclosure concerns. |
| F003 | Summary behavior, citation validation and historical manual evidence. | Later features replace some rules; semantic quality and local-model manual checks have the limitations in A6 and above. |
| F004 | Background work, persistence, stop and status behavior have targeted evidence. | Lost-response dismissal race is an accepted risk. |
| F005 | Sign-in, consent and real-plan smoke evidence; fake-service failure coverage. | Not every failure was provoked on the live account. |
| F006 | Approved summary-first, Portuguese, instructions and feedback/regeneration changes; owner-attested manual validation. | A3's server/UI consent distinction needs a decision. |

The task lists contain 128 numbered task entries: 127 checked, with F001 T046
unchecked. This is a tracking count, not independent requirement-by-requirement
proof. F002's checked state includes the owner's explicit acceptance.

## Simplicity and overengineering

The F002 journal, snapshot, recovery and coordination machinery is substantial.
Its need is tied to approved partial-failure, concurrent-operation and restart
requirements; `specs/002-shared-library-export-deletion/research.md` records the
cross-store tradeoffs. **Do not call it overengineered based on size alone.**

The client regeneration lifecycle in `src/LexScholar.Client/Summary.fs` has
accumulated significant state for in-flight submissions, cancellation and uncertain
responses. The stated purpose is to preserve the durable summary and avoid stale
work; targeted tests exist. **Proposed verdict: accepted complexity**, not a verified
defect. A simplification proposal should identify concrete redundant state and
demonstrate preservation of the existing race and data-protection behavior.

Historical specs, task lists, review notes and current behavior coexist in long
artifacts. A small navigation/supersession index may reduce review cost more safely
than rewriting history.

## Constitution: possible improvement, not an amendment

Candidate workflow wording for discussion:

> When an approved feature changes or supersedes existing behavior, its
> specification and verification identify affected earlier requirements, and
> current user-facing data-handling guidance is updated before the feature is
> considered complete. Historical records may remain unchanged when clearly
> identified as historical.

This addresses stale guidance and precedence (A1, A7, A8), but overlaps existing
Principles II, IV and VI. An alternative is to keep it in `AGENTS.md` or a review
checklist rather than expand the constitution. If adopted in the constitution,
governance requires an explicit proposal, impact review, version and amendment
date; no amendment is authorized by this audit.

## Questions for an independent reviewer or another model

1. Which findings are supported by the cited evidence? Identify any incorrect
   assumptions, missing later supersession or overstated severity.
2. For A2, does the current export promise require a wording fix, a format change,
   or an owner decision? What is the least surprising user-facing wording?
3. For A3, is fresh confirmation intended as a client guarantee or a backend
   authorization requirement? What concrete failure does each option prevent,
   and what is the simplest testable contract?
4. For A5 and A6, what remains unverified versus actually broken? Suggest
   single-researcher checks only; do not assume a participant study is wanted.
5. Which complexities are justified by approved behaviors, and is there any
   specific removable complexity with a safe, testable alternative?
6. Does the proposed constitution wording add a meaningful obligation, or would
   a current-state index and ordinary workflow guidance be sufficient?
7. Propose a small, ordered set of remediation slices; identify dependencies,
   tests and owner decisions, but do not treat the plan as approved.

## Suggested order for discussion

1. Correct false current-facing privacy and export descriptions (A1/A2).
2. Decide and align the regeneration-consent boundary (A3).
3. Reconcile tracker, statuses and evidence wording (A4, A7–A9).
4. Decide how to handle deferred usability and quality evidence (A5/A6).
5. Separately decide whether a constitution amendment is worth the governance cost.

No code, spec, tracker or constitution changes are authorized by this document.
