# T019 — failed-selection journey setup race

## Incident and scope

PR #169 changes only `docs/notes/summary-quality-manual-check.md`. CI run
[45683098346](https://github.com/thyvini/lexscholar/actions/runs/45683098346)
reported 1,194 passed, one failed, zero skipped: Firefox
`failed project selection preserves the visible library`, at its final import
visibility assertion. The snapshot contains the selection-error alert, active
Alpha, and the original article view. There was no assertion that the library
was visible **before** selecting Beta.

Investigation branches from `origin/release/0.5.1`, commit
`a463b362e2e3dafd7f57a75c082cf84546608125`, independently of the docs PR.
Linux, .NET SDK 10.0.114, Playwright Chromium 1248 / Firefox 1555.

## Controlled reproduction

A temporary T019 diagnostic held the GET `/api/projects` response after creating
Beta (route fetch, arrival/release `TaskCompletionSource` handshakes). While the
creation reload was held, it pressed the library button at its center, released
the response, awaited Beta's enabled name button, then released the pointer.
Both browsers retained the original document and had no import input.
`scripts/check.sh T019`: 20 passed, zero failed/skipped (18 existing cases plus
the two diagnostic cases). No sleeps, retries, or production changes.

Geometry instrumentation established why: the library control was 38 px high
and moved down about 20.8 px between pointer-down and pointer-up:

| Browser | Before Y | After Y |
|---|---:|---:|
| Chromium | 437.71875 | 458.5 |
| Firefox | 437.76666 | 458.56668 |

Its previous center is above its new top, so releasing there does not activate
the button. The original DOM node remained connected; a replacement-node
hypothesis was rejected. The temporary diagnostic is not retained as a test
requiring a missed click to remain product behavior.

This deterministically reproduces an unopened library during the original
journey's asynchronous setup. It does **not** prove the precise historical CI
interleaving: the CI failure has no pointer trace. The strongest supported
explanation is a setup click racing the creation reload, not a failed selection
changing an already-visible library. Ordinary users can also miss a pointer
activation during this layout movement; no broader layout correction is
proposed here without a separate scope decision.

The demonstrated missed activation is tracked as
[distinct product debt](t019-library-activation-debt.md), not resolved or dismissed
by the test correction.

## Smallest correction and verification

The journey now awaits Beta's enabled name button before clicking the library,
then requires the import input to be visible **before** injecting the failed
selection. Existing post-failure import/active-project/error assertions remain.
This adds a missing precondition rather than retries or weaker expectations.

Baseline: `scripts/check.sh T019` — 18 passed, zero failed/skipped.

Sensitivity RED: temporarily added `| Selected (Error _) -> false` to
`App.fs`'s `showLibrary` match, deliberately hiding the library after a failed
selection. `scripts/check.sh T019` — 18 total, exactly the Chromium/Firefox
failed-selection cases failed their post-failure import visibility assertions,
zero skipped. Both passed the new pre-failure visibility assertion first.
Full local log: `/tmp/lexscholar-check.OS9oZt`. The mutation was removed and is
not part of the commit.

Final GREEN: `scripts/check.sh T019` — build passed with `-warnaserror`,
18 passed, zero failed/skipped after removing the mutation.

CI full-suite verification belongs to a future PR; no push or merge was made
during this investigation.
