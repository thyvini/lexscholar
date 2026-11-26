# T007 — published Linux journey (partial)

- Candidate: locally built native Linux archive from this branch, extracted into a disposable private root; Chromium and Firefox use separate roots and the synthetic `valid.pdf` fixture. No account or external summary request.
- Sensitivity: temporarily append one byte to the expected fixture bytes; `scripts/check.sh T007` fails the original-byte assertion in both browsers (2 failures, 0 skipped). The mutation was removed.
- GREEN: `scripts/check.sh T007` — 161 tests passed, 0 failed, 0 skipped after removing the mutation, including the SIGTERM restart path.
- The journey creates and associates a document, opens its original, checks a second process is refused, restarts, exports and compares the PDF, confirms library deletion and restarts with the export intact. The bundled browsers' PDF navigation is checked; native PDF viewer rendering is not established.
- Full-suite CI: pending PR.
- This is Linux-only partial evidence. Native macOS verification and the remaining T007 acceptance gates are outstanding; no Mac support claim is made.
