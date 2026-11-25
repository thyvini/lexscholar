# Workstream E: governance proposal and verification debt

**Status: tracking amendment approved and applied on 2026-11-25; verification proposals remain unapproved.**
Cards [$166](https://thyago-cunha-dev.codecks.io/card/166) and
[$167](https://thyago-cunha-dev.codecks.io/card/167) remain open. This note
records only the approved tracking amendment below; it does not change historical
feature records or acceptance results.

## Approved constitution tracking clarification ($165)

The constitution's “Current user-control delivery tracking” previously described
F002 export and deletion as deferred. F002's
[T018 evidence](../../specs/002-shared-library-export-deletion/evidence/t018-owner-validation.md)
records owner acceptance in Firefox and Chromium. Delivery of those controls
alone does not establish every aspect of Principle II.

**Approved exact replacement**, now applied to the paragraph under “Current user-control
delivery tracking” (not for Principle II itself):

> F001 retains shared documents after their final project association is removed. Full-library export and deletion were deferred from F001 and tracked for F002 in [Codecks card $12y](https://thyago-cunha-dev.codecks.io/card/12y). F002 delivered and verified those controls; its acceptance and limitations are recorded in [T018 evidence](../../specs/002-shared-library-export-deletion/evidence/t018-owner-validation.md). This delivery does not by itself establish full compliance with Principle II. F001's approved feature scope and historical deferral remain unchanged.

Applied metadata:

> **Version**: 2.0.1 | **Ratified**: 2026-11-04 | **Last Amended**: 2026-11-25

The owner explicitly approved this exact narrow amendment and its detailed
implementation plan in the Workstream A ($165) instruction on **2026-11-25**.
The actual adoption date is **2026-11-25**; ratification remains **2026-11-04**.
Version 2.0.1 is a patch because this corrects delivery tracking without changing
any principle or obligation. The impact is confined to current tracking and
metadata: F001's historical scope and evidence remain intact, F002's recorded
limitations still apply, and no blanket Principle II compliance is claimed.
No core principle, development-workflow obligation or governance rule changed.
This approval does not approve the $166/$167 proposals below, run their checks,
accept their evidence risks or retroactively pass their historical criteria.

### Amendment verification

Focused Python assertions before editing: **RED**, the approved paragraph was
absent. After editing: **GREEN**, exact approved paragraph and metadata matched;
the rest of the constitution and both verification proposals were byte-for-byte
unchanged from the branch base; approval/status wording and all six local links
in these two files passed checks. `git diff --check` passed.
`scripts/check.sh` passed the solution build with `-warnaserror`.
No application tests were run for this documentation-only amendment.

## $166: owner-checkable usability evidence

F001 [SC-001 and SC-006](../../specs/001-research-workspace/spec.md) set
90% unassisted participant thresholds; [T046 and the participant evaluations
remain deferred](../../specs/001-research-workspace/verification.md). Technical
completion is not proof that those thresholds passed. Do not change those
historical records or organize a participant study as part of this proposal.

Proposed current-workflow checks, for the owner to approve or decline:

1. In an isolated workspace, create two projects, switch between them, and
   identify the active project from the interface after each switch.
2. Import an openly licensed or fixture PDF with multiple pages, associate it
   with a project, open it using the current PDF link, and navigate to a
   specified page in Firefox and Chromium on supported Linux.
3. Record each action's actual outcome and any confusion, assistance or
   workaround. For each browser record date, app revision, Linux and browser
   versions, PDF identifier and target page. Mark each check passed, failed or
   not run with an explanation; link any supporting evidence.

These are practical single-researcher checks, not a participant study or a
statistical replacement for the original thresholds. The owner should record
an explicit decision: accept the proposed checks as a *new* current-workflow
standard and record actual results, or retain the evidence deferral. A failure
or incomplete check remains visible; adopting a new standard does not
retroactively pass T046, SC-001 or SC-006. Keep $166 open until the owner
decides and records results or explicitly retains the deferral.

## $167: representative summary quality evidence

The [F003 manual evidence](../../specs/003-document-summary/evidence/t012-manual-validation.md)
reports 9 of 10 sampled fixture points supported, but repetitive passages limit
its strength. Valid page references alone do not establish semantic support.

Proposed owner-reviewed check, not yet run:

1. Select one representative academic PDF with an independently verifiable
   open licence (for example CC BY 4.0); record title, authors, version, source
   URL, licence and licence URL, access date, page count and PDF hash. Verify
   licence and permission to send it before use. Do not use or commit private
   research PDFs. Record why the document is representative and any limitations
   in its text layer or layout.
2. In an isolated workspace, generate a summary using the current workflow and
   informed confirmation for any external processing. Record date, app
   revision, destination, model and available model version, relevant settings
   or instruction, and generation context. Do not record credentials.
3. Review up to ten sequential displayed points, or all if fewer, against every
   page cited for each point. Record the sampling rule and total point count.
   For each, record a brief claim identifier or permitted excerpt, cited page
   numbers, whether the cited text supports the whole claim (**supported**,
   **unsupported**, or **uncertain**), and a brief rationale. Record missing
   or inaccessible citations as such rather than treating them as supported.
4. Record counts for all three categories, failures or omissions, reviewer,
   date and an evidence link. The owner decides whether the observed evidence
   is sufficient or explicitly accepts the remaining evidence risk; retain
   unresolved results and limitations either way.

One document and one model run cannot establish a general accuracy rate or
guarantee later outputs. Page numbering, extraction quality, multi-page claims
and subjective interpretation can limit review; report these and any
unavailable model settings. This is manual evidence, not automated semantic
verification. Keep $167 open until actual evidence and limitations are
documented or the owner explicitly accepts the remaining risk.
