# Specification index

The feature specifications are a historical record, not six independent descriptions of the current interface. Read a feature's spec with its later, explicitly identified replacements below; do not silently rewrite the older requirements. Plans, contracts, task evidence and release history explain delivery and verification, but a checked task is not proof of behavior outside its recorded evidence.

| Feature | Specification | Delivery provenance |
|---|---|---|
| F001 — Research workspace and PDF library | [Spec](../specs/001-research-workspace/spec.md) · [Tasks](../specs/001-research-workspace/tasks.md) | Approved spec; initial workspace in v0.1.0. Later interface changes are in F006. |
| F002 — Shared library export and deletion | [Spec](../specs/002-shared-library-export-deletion/spec.md) · [Tasks](../specs/002-shared-library-export-deletion/tasks.md) | v0.5.0; [T018 evidence](../specs/002-shared-library-export-deletion/evidence/t018-owner-validation.md) distinguishes automated checks from owner-attested manual acceptance. |
| F003 — Document summary | [Spec](../specs/003-document-summary/spec.md) · [Tasks](../specs/003-document-summary/tasks.md) | v0.2.0; subsequently changed by F004 and F006. |
| F004 — Background summaries | [Spec](../specs/004-background-summaries/spec.md) · [Tasks](../specs/004-background-summaries/tasks.md) | v0.3.0; summary export and full library deletion delivered later in F002. |
| F005 — ChatGPT sign-in | [Spec](../specs/005-chatgpt-sign-in/spec.md) · [Tasks](../specs/005-chatgpt-sign-in/tasks.md) | v0.2.0; new-request interface changed by F006. |
| F006 — Summary first, in Portuguese | [Spec](../specs/006-summary-first-portuguese/spec.md) · [Tasks](../specs/006-summary-first-portuguese/tasks.md) | v0.4.0, including owner-directed T016 and T023–T025 extensions. |

The version references identify release history, not a claim that every historical scenario remains current. See each feature's tasks and linked evidence for the actual verification and limitations.

## Explicit precedence

- [F006 FR-001](../specs/006-summary-first-portuguese/spec.md) replaces F001 FR-008's in-app reader with summary-first opening; FR-003 replaces F003 FR-005's citation navigation with a new tab at the cited page.
- F004 replaces F003 FR-009's abandonment on navigation with background generation. Its [FR-010](../specs/004-background-summaries/spec.md) originally deferred full export and deletion; [F002](../specs/002-shared-library-export-deletion/spec.md) subsequently includes stored summaries in library export and deletion. F004's historical statement that these did not exist is not a current limitation.
- F006 FR-005 replaces F003 FR-010 and F005 FR-002 **only for new requests in the interface**: the interface offers the ChatGPT plan, while settings-file backend support and historical summary provenance remain. F006 FR-006 replaces F003 FR-006's dismissal wording for finished summaries, with confirmation and storage deletion under F004 FR-009. F006 FR-012 and FR-013 replace F005 FR-006's English label and FR-010's help link respectively.
- F006 FR-016 replaces F003 FR-002's source/majority-language rule for **new** summaries with Brazilian Portuguese; existing stored summaries are not translated or regenerated. F006 FR-014 preserves the other F003–F005 decisions except where F006 explicitly changes them.
- F006's owner-directed T023 instruction and owner-approved T025 feedback extensions supersede the earlier F007 deferrals **only for those fields and regeneration**. The [F006 spec](../specs/006-summary-first-portuguese/spec.md) retains the original scope wording as historical context; ratings, flags and history remain outside those extensions.

## Deferred scope and limits

- [F002 scope](../specs/002-shared-library-export-deletion/spec.md) defers in-app restore and excludes automated backups, cloud sync and secure erasure. Export is not a promise to restore, erase external copies, or guarantee survival of hardware failure; see [F002 research](../specs/002-shared-library-export-deletion/research.md) and [T018 limitations](../specs/002-shared-library-export-deletion/evidence/t018-owner-validation.md).
- F006 did not add a settings screen, support for documents over 100 pages, or quotes as evidence; see its [assumptions](../specs/006-summary-first-portuguese/spec.md). Earlier feature plans mentioning these are not evidence of delivery.
- Browser automation does not replace owner manual acceptance or establish native PDF viewer behavior in all environments. Consult the feature evidence and quickstarts for the precise checks and remaining limitations rather than inferring them from a release label.
- F001 T046 and its historical participant-based usability criteria remain deferred, not passed; representative summary-quality evidence is also limited. See the [owner-check proposals](notes/workstream-e-governance-verification.md) and open cards [$166](https://thyago-cunha-dev.codecks.io/card/166) and [$167](https://thyago-cunha-dev.codecks.io/card/167).
- An intermittent CI restart-lock failure is under investigation in [$168](https://thyago-cunha-dev.codecks.io/card/168). Its cause and production impact are not established; the successful CI rerun does not explain the original failure.
