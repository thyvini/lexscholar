# T026 — Stable library navigation

- **RED:** `LOG=/tmp/opencode/052-t026-red.log scripts/check.sh T026` — the held-response/native-pointer regression failed in Firefox and Chromium because the library did not open (2 failed, 0 skipped).
- **GREEN:** `LOG=/tmp/opencode/052-green.log scripts/check.sh T026 T019 T018` — warning-as-error build passed; 43 tests passed, 0 failed, 0 skipped. The suite covers the same pointer interleaving, long project lists, keyboard activation, two viewport widths, project selection and failed-selection preservation.
- Full-suite CI: [PR #175 build-and-test](https://github.com/thyvini/lexscholar/actions/runs/45716911620/job/139565198358) passed; GPT review also passed on the merged head.

The existing library button is placed ahead of the variable-height project list; no click is synthesized and no pointer semantics are changed. The [prior investigation](t019-library-activation-debt.md) remains historical: its documented movement was reproduced, but the exact timing of the original CI incident was not established.
