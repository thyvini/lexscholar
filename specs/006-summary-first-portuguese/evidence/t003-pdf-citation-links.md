# T003 — PDFs and citations in new tabs

- RED: `scripts/check.sh T003` — 79 tests, 4 failed, zero skipped. Both browsers failed assertions for citation links, which were still buttons.
- GREEN: `scripts/check.sh T003` — 79 passed, zero failures/skips; build with `-warnaserror`.
- Affected-task verification: `scripts/check.sh T002 T003 T004 T005 T006 T007 T008 T009 T010 T040` — 530 passed, zero failures/skips; build with `-warnaserror`.
- Existing summary, background-summary, ChatGPT and reader journeys migrated from citation buttons/reader navigation and the old PDF link label to reference links. Their remaining assertions are retained.
- The broader regression run exposed a T002 Firefox setup race: project selection was not awaited before opening Biblioteca. Added the existing `expectActive` handshake; no product behavior, sleeps or timeouts changed.
- CI full suite: [initial run 44355541522](https://github.com/thyvini/lexscholar/actions/runs/44355541522); current-head CI and GPT review are tracked on [PR #114 checks](https://github.com/thyvini/lexscholar/pull/114/checks).
- GPT review round 1: approve, coverage complete; one P2 about the stale `button.citation` description in the client map. Verdict: **must fix** (verified contradiction). Updated the map to describe anchors and new-tab URLs. Documentation-only follow-up; no repeated local test run.

Both Chromium and Firefox check `p. 3`, `pp. 1–2`, non-consecutive `p. 3, p. 5`, and the last page `p. 12`. Each click (including a repeated citation and Abrir PDF) opens a distinct page; all five captured document-navigation requests identify only the opened document. The first tab keeps its generated-content label, context and points; the existing reader stays unchanged.

Popup characterization: a temporary failing assertion recorded `popup.Url = ""` in **both browsers** after the PDF request. The link's `href` therefore supplies the page fragment in automated verification (R1); fragments never reach the server. Bundled browsers download PDFs rather than expose their native viewer. Actual page rendering is intentionally reserved for the Firefox/Chromium manual check in T012.
