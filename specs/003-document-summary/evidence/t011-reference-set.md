# T011 reference-set checks (SC-001, SC-005, FR-013)

- No RED commit: the behaviour already exists (T003, T006, T007, T008), so a sensitivity check replaces it.
- GREEN: `scripts/check.sh T011`: `Tests: 18 total, 0 failed, 0 skipped.`
- CI: full suite on the PR (see PR checks).

## Sensitivity checks (temporary, uncommitted, reverted)

- Citation validation: `Summarize.ask` given page 999 as an allowed page (`Set.union allowed (Set.ofList [ 999 ])`). `scripts/check.sh T011`: 4 failed, the page-validity theory rows (`Assert.InRange` failure, actual 999). Reverted: 18 pass.
- Data left unchanged: the endpoint appended a byte to a stored original (file mode restored) before extracting pages. `scripts/check.sh T011`: 10 failed, including the three SC-005 tests (request, decline/confirm, abandon). Most of the other failures are the appended byte also breaking the PDF for the rest of the tests, so this check shows the SC-005 comparison is sensitive to an original changing, not that it isolates one cause. A changed database row is covered by the same snapshot but was not separately broken. Reverted: 18 pass.

## Limits

- The openly licensed (CC BY 4.0) real article from research section 7 is still deferred (T001 GPT P2, accepted); the reference set here is the generated PDFs. It would only add realistic layout, not new behaviour, and is not needed for SC-001 as written.
- For `long-40.pdf` the fake cites page 1 in every chunk, so only `omittedPoints >= 1` is asserted there; exact counts are asserted for the single-call documents.
- SC-005 compares the library, project-link, project and workspace rows plus the SHA-256 of every file in the originals directory.

## Greptile (first review, two P2)

- Project fields missing from the snapshot (`WorkspaceId`, `NormalizedName`): must fix, done.
- Abandon snapshot taken before the endpoint finished: must fix, done. The final comparison now runs after the host is disposed (which waits for the request to end), on a shared `TemporaryWorkspace`. `scripts/check.sh T011`: 18 total, 0 failed.
