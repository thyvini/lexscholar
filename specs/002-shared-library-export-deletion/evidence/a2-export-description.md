# A2 — Export scope description

The library export description now identifies projects linked to exported PDFs
and explicitly excludes projects without linked documents. The existing v1
export-format contract and implementation are unchanged.

- RED: `LOG=/tmp/opencode/a2-red-complete.log scripts/check.sh A2` — build
  passed with `-warnaserror`; both browser assertions failed because the corrected
  description was absent (Chromium and Firefox; 2 failed, 0 skipped).
- GREEN: `LOG=/tmp/opencode/a2-green.log scripts/check.sh A2` — build passed
  with `-warnaserror`; both complete export journeys passed (2 passed, 0 skipped).
- Full-suite CI: [PR #165](https://github.com/thyvini/lexscholar/actions/runs/45661127801/job/139370918510) passed; not run locally.
