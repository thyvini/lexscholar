# Feature Specification: Use the local workspace on Linux and Mac

**Feature branch:** `docs/platform-expansion-phase3` · **Created:** 2026-11-26 · **Status:** Owner-approved feature boundary; not a support announcement.

**Authority:** The owner approved scoped implementation for Linux x86-64 and macOS ARM64 while preserving existing Linux support; this planning branch contains no production implementation. Feature approval does not accept new risks or authorize release. The [roadmap](../../docs/product/vision-roadmap.md) identifies Firefox and Chromium and distinguishes targets from shipped support.

## Clarifications

### Session 2026-11-26

- Q: What does a reported macOS version establish? → A: It identifies the version actually verified on that target, not a minimum or a supported range. Source: owner clarification in this session, “Record only actually verified versions and targets.” Sage (GPT-6.2 Astra) records this as a reversible wording decision: later evidence can add a verified configuration without making a promise about untested ones.

## User Scenarios & Testing

### User Story 1 — Work with a local research library on either computer (P1)

As a researcher, I want to use the documented LexScholar distribution on my Apple Silicon Mac or Linux computer and find my project and PDFs intact when I return. I should not need to move research data to another service or give up existing library controls.

**Why this priority:** Starting the app is not enough if the researcher cannot work with and safely remove the same materials as on Linux.

**Independent test and acceptance scenarios:**

1. Given a clean test workspace and the candidate distribution for the target, follow its documented launch instructions. The researcher can open the local interface, create a project, import a fixture PDF and open its summary-first view and original PDF.
2. Given that workspace is open, start a second instance against it. The second instance refuses access without changing the project; after stopping the first, relaunch and find the project and identical PDF.
3. Given the fixture in the library, export it, verify the original against the fixture, then confirm library deletion and restart. Deleted material does not reappear; the separate export remains intact.

### Edge cases

- Deletion acts only on the selected document's validated storage key in the opened originals directory. Replacing the directory path must not redirect removal or its durability check to another directory. A storage error is not proof of absence; an incomplete deletion must not be reported as complete. See [F002 FR-008–010](../002-shared-library-export-deletion/spec.md#requirements-mandatory), the [deletion outcome contract](../002-shared-library-export-deletion/contracts/http-api.md), and [T013 managed-original evidence](../002-shared-library-export-deletion/evidence/t013-managed-originals.md).
- If the researcher reimports the PDF through LexScholar during deletion, the pending document's identity must not be reused. A successful fresh import has its own identity and storage key; completing the old deletion leaves the new PDF intact ([T015 reimport evidence](../002-shared-library-export-deletion/evidence/t015-reimport-safety.md)).
- External replacement of a file **at the same key inside the already-open directory** is distinct from either case above. The [Linux removal operation](../../src/LexScholar.Infrastructure/DocumentStore.fs#L93-L110) addresses the entry by directory and name; it does not verify that the entry still identifies the earlier file. F002 does not establish a protection for that substitution. This spec neither adds one nor records acceptance of that risk.
- After abrupt process termination, restart must not require bypassing exclusive access or discard previously confirmed work. This is not a guarantee against hardware failure.
- A build, simulated target or browser-only test cannot establish that a researcher can run the distribution on a physical Mac.

## Requirements

- **FR-001:** Provide a runnable distribution and accurate prerequisites and launch instructions for each target. The documented launch must serve the browser interface, not just respond to service requests.
- **FR-002:** Preserve shipped workspace behavior in both browsers on both targets, including local-only access and existing external-processing consent. Use the [specification index](../../docs/specification-index.md#explicit-precedence) for current behavior, not superseded reader or summary flows.
- **FR-003:** Preserve single-writer exclusion, restart, safe deletion and the existing durability boundary on each target. Neither a weaker Mac guarantee nor an exception for an intermittent failure is approved. See [F002 limits and the release-specific lock risk](../../docs/specification-index.md#deferred-scope-and-limits).
- **FR-004:** Verify both targets in CI without dropping Linux coverage or counting skipped protections as passes. Demonstrate equivalent observable checks where tests assume Linux facilities.
- **FR-005:** Before any Mac support claim, obtain the owner's smoke result on a physical ARM64 Mac using the candidate distribution. Identify build, machine architecture, OS, browser versions, steps, results and limitations. Keep CI and owner evidence distinct.

## Success Criteria

- **SC-001 (FR-001/002):** Scenario 1 succeeds in all four platform/browser combinations; the published candidate is the one exercised.
- **SC-002 (FR-003):** On both targets, scenarios 2–3 and the restart check pass without simultaneous writers or loss of the confirmed project. In the deletion checks, changing the directory path does not redirect removal or its durability check, unrelated managed documents and a fresh LexScholar reimport remain intact, and a storage error leaves the affected item incomplete.
- **SC-003 (FR-002/004):** Candidate evidence shows passing existing behavior and protection checks on both targets, no skips, and no loss of Linux coverage. Missing or non-equivalent evidence leaves the criterion open.
- **SC-004 (FR-001/004/005):** Owner records scenarios 1–3 on the physical Mac before support is claimed. Launch guidance and coverage statements identify as verified only the targets and versions backed by candidate evidence. They must not present a tested macOS version as a minimum or imply compatibility with untested versions. No physical Mac result is supplied here.

## Assumptions and boundaries

One story, sized for 10–15 eventual tasks, not a task plan. No new product workflows, migration between computers, cloud, mobile, browser targets, installer experience, privacy or retention policy. The [constitution](../../.specify/memory/constitution.md) II, IV–VI governs both targets unchanged.

Audit dependencies for verification, not design decisions: `DataDirectoryLock` uses Linux flags (SC-002); `DocumentStore` anchored unlink and directory sync protect removal and persistence (SC-002); `/proc`-dependent tests cannot establish Mac behavior unchanged (SC-003). CI target identity and the contents and launch location of packaged assets matter to SC-001/003/004.

**Delegated editorial choices — Sage (GPT-6.2 Astra), 2026-11-26:** Use directory prefix 012 to avoid the roadmap's F007–F011 names, not to set delivery order. Use a disposable fixture for scenarios 1–3 so FR-003 deletion is repeatable without risking the owner's library. Neither choice expands support.
