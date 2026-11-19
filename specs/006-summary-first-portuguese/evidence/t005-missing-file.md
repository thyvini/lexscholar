# T005 — missing-file navigation

- RED: `scripts/check.sh T005` — four failures: both contract cases returned problem JSON, Chromium lacked the heading and Firefox rejected the error response.
- GREEN: `scripts/check.sh T005` — build with warnings as errors; 60 tests passed, zero skipped (including Chromium and Firefox).
- CI full suite: see PR `build-and-test` check.
- Storage/security: isolated workspaces; tests compare all document and association columns before and after the error responses, and check no storage path is disclosed.
- Limitation: the actual “Abrir PDF” click on a missing file is the T012 manual check; the automated browser test navigates directly.
