# T006 evidence: long documents in chunks with progress

- RED (`scripts/check.sh T006`, commit bdf6994): `Tests: 22 total, 8 failed`. `chunk` and `groupPoints` were stubs, the endpoint streamed no `progress` or `combining` events and the client ignored progress, so tests failed on the asserted chunk lists, group sizes, call counts, event kinds and `Progress` values.
- GREEN (`scripts/check.sh T003 T004 T005 T006`): `Tests: 112 total, 0 failed, 0 skipped`.
- Covered: whole pages packed in order within `MaxChunkCharacters`; a long page split into parts that keep its number; points grouped to the limit; map then combine with progress counting unreadable pages from the first report; a round that does not shrink the points is `service_failed` with no further call; a combined point citing outside its group is omitted and counted; `long-40.pdf` through the endpoint streams `progress` up to 40 of 40, then `combining`, then one `summary`; client state keeps and clears progress and drops progress of older requests.
- Limits: a one-chunk document emits no `progress` event (nothing to report; the contract does not require one). The sixth-round limit is implemented but not exercised by a test (a round shrinking by exactly one point per round needs a contrived fake). Failed events stay minimal (T010).
- Full suite: CI run on the PR.
