# T002 evidence: local checks before anything is sent

- RED (`scripts/check.sh T002`, commit 5f72d42): `Tests: 14 total, 12 failed`. Contract tests failed on `Expected: UnprocessableEntity / NotFound / ServiceUnavailable, Actual: NotImplemented`; `readability` and `PdfText.extractPages` were stubs, so their tests failed on the asserted page numbers and readable/unreadable lists.
- GREEN (`scripts/check.sh T001 T002`): `Tests: 34 total, 0 failed, 0 skipped`.
- Covered: readability rules (letter or digit, 200-character minimum); page numbers by position and the unreadable pages 2 and 5 of `partial.pdf`; `too-long.pdf` refused before extraction; 404 `not_found` / `document_not_found`, 503 `document_unavailable`, 422 `too_many_pages` / `no_text` / `not_enough_text`, each with the fake service recording zero requests.
- Decision: an unassociated document answers `document_not_found` when no such library document exists and `not_found` when it exists but is not associated with the project (or the project does not exist).
- Full suite: CI run on the PR.
- Sensitivity check (uncommitted): making the extraction exception handler never match fails `an original that cannot be parsed is reported unavailable, not thrown`.
