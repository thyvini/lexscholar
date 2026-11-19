# T019 — name activation and inline options ($156)

Base: latest `origin/release/0.4.0` at `7600e76` (includes #125 and its Firefox selection wait, plus #127 selection styling); isolated branch `feat/f006-item-options`. Initial RED/GREEN used preceding release head `f8738cc`.

- RED: `LOG=/tmp/opencode/t019-red.log scripts/check.sh T019` — build passed; both new browser cases failed the assertion that the source name is an accessible button (8 total, 2 failed, zero skips).
- GREEN: `LOG=/tmp/opencode/t019-green.log scripts/check.sh T019` — 8 passed, zero skips.
- State/interaction regressions: `LOG=/tmp/opencode/t019-regression.log scripts/check.sh T019 T002 T004 T015 T018` — 141 passed, zero skips; build uses `-warnaserror`.
- After latest-release integration: `LOG=/tmp/opencode/t019-rebased-green.log scripts/check.sh T019 T002 T004 T015 T018 T033 T007` — 268 passed, zero failures/skips; warning-as-error build passed. This includes #127's four browser selection cases. The preceding broader run exposed a missing repeated-activation step in the migrated role-change journey; corrected that test step without changing its assertions.
- Full suite: deferred to CI on the eventual PR; this delegated slice does not open a PR.

Chromium and Firefox verify name-button keyboard activation/focus, repeated activation revealing source options, role movement while the PDF control remains visible, removal retaining the library PDF, absence of separate `Abrir`, and active-project options. State tests check option lifetime, invalid source activation, project changes, guarded duplicate removal, removal failure/success, and guarded project selection.

Project-operation limitation: no project rename/delete workflow exists. The only project option exposed is the existing `Atualizar` action, which refreshes the workspace lists; no new API or project operation was invented. Confirm with the owner if different project options are desired.

Existing journeys keep action-based helpers but target the sole name button for opening/selecting. Source-option journeys explicitly open/re-activate before role changes or removal. The background marker-removal journey opens the summarized source before removing it, since options belong only to the opened source; marker assertions remain.

Coordination: #127 initially overlapped `App.fs`, CSS, workspace journeys and feature artifacts. After its merge, rebased onto `7600e76`: retained project tint without outline, source selection outline, both selection regressions, and transferred its source `aria-current` from the removed `Abrir` control to the name button. Retained #125's Firefox active-project wait. No branch merge or PR was performed.

## PR #128 review corrections

- P1 (remembered source incorrectly discloses options on return from library): **must fix**. Opening and revealing are separate messages; the view reveals only when the source is actually displayed. Library navigation clears disclosure without clearing the remembered source or summary.
- P2 (failed project selection prematurely closes library): **must fix**. Selecting a different project keeps the current view until the existing selection workflow reports success. Selecting the already-active project still opens immediately.
- RED: `LOG=/tmp/opencode/t019-review-red2.log scripts/check.sh T019` — 16 total, six assertion failures, zero skips. Both browsers reproduced hidden-options failure after returning to a remembered source, retained disclosure while in the library, and the missing import view after a failed project selection. An earlier run hit the command time limit after capturing the four source failures; it is not counted as the complete RED.
- GREEN: `LOG=/tmp/opencode/t019-review-green.log scripts/check.sh T019 T002 T004 T015 T018 T033 T007` — 274 passed, zero failures/skips, warning-as-error build passed.
- New Chromium/Firefox regressions cover library navigation with options previously open **and** closed, absence of source options in the library, first activation returning to the summary without options, second activation revealing them, and a failed project-selection HTTP request retaining the library and active project.
- Full-suite CI and renewed GPT review remain required on the new PR head; no merge performed.
