# T012 — Code-map audit and owner-attested manual validation

## Provenance and result

Recorded on 2026-11-19. The owner explicitly confirmed that **every F006 validation was performed and passed**, including both Firefox and Chromium, PDF citations, feedback preservation and the real ChatGPT plan. This records the owner's confirmation; the documenting agent did not perform or observe those manual runs.

The attestation covers quickstart manual steps 1–9 (including 7b): onboarding, summary-first presentation/consent/progress, citation-page navigation, Abrir PDF with the summary retained, missing-file handling, roles, confirmed deletion/restart, failed-summary close, plan-only new requests with historical configured-service provenance preserved, and Portuguese interface/failure text. It also covers the later instruction/feedback checks and language validation recorded in [T013](t013-owner-smoke-check.md). These are the checklist's scope, not reconstructed individual observations.

No browser versions, document names, citation page numbers, screenshots, per-step notes, run dates or tested build identifier were supplied. None are inferred from the release commit or automated fixtures. The blanket pass is owner-attested evidence, not a detailed reproducible browser transcript.

## Code-map audit (agent, static inspection)

Audited the current release tree at `a0d13b571c2c912eee7c6aa932e027489f01c042` (PR #134), separately from the owner attestation:

- Checked client/server and browser-test compile lists, `Texts.fs`, `Library.fs`, `Summary.fs`, `App.fs`, `DocumentEndpoints.fs`, `Summarize.fs`, summary endpoint routes and `send-dialog.js` against the incremental F006 entries.
- The existing map documents Texts/formatters/fonts, workspace frame, new-tab citations, reader removal, role/open state, HTML content errors, plan-only requests, deletion confirmation, failure mapping/compile order and Portuguese summary/account text.
- Later entries cover onboarding/single-origin lifecycle, typography, item activation, modal consent, summary cards, Portuguese prompts, transient first instructions, backend replacement lifecycle and feedback/navigation/cancellation guards.
- Corrected stale overview claims about a destination chooser and immediate finished-summary dismissal; current behavior is plan-only requests and explicit deletion confirmation. Added Texts/FailureTexts to the client overview.
- Corrected references to the removed reader journey helper, obsolete “later slices” wording and claims that regeneration/feedback were still upcoming. The owner-approved T023–T025 extensions are now clearly distinguished from the earlier slices.
- Reconciled quickstart onboarding completion, repeated name activation for role options, account prerequisites, manual versus automated sections and the obsolete claim that no feedback controls exist.

No source, tests, contracts or schema changed. This audit is static documentation verification, not runtime validation.

## Visual defect reconciliation — $155

Read-only Codecks inspection found [$155](https://thyago-cunha-dev.codecks.io/card/155) still **started**: “Workspace design differs substantially from the mockups,” with the owner's project/source selection clarification.

The release includes the recorded corrections: [T018 selection](t018-workspace-selection.md), [T020 send dialog](t020-send-confirmation.md) and [T021 summary presentation](t021-summary-presentation.md). The owner's all-validation-passed confirmation supplies the manual acceptance previously outstanding after those corrections. No additional visual defect was reported in that confirmation.

No Codecks status, content or comment was changed. Tracker reconciliation remains for the lead/owner; this document does not assert that the card is closed or that an agent independently verified pixel-level equivalence.

## Verification boundary

Documentation-only: no build or tests run, as requested and per AGENTS.md's documentation-only rule. No RED/GREEN or new automated pass is claimed. Existing automated evidence remains in the individual slice records; full-suite CI is separate from this manual attestation. No PR opened or merge performed by this task.
