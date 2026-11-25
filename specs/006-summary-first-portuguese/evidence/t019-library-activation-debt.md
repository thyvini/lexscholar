# Distinct product debt — library activation lost during project creation

**Disposition:** demonstrated user-visible issue; resolution requires a separate
scope decision. The test-precondition correction does not fix this product debt.
Existing card statuses are unchanged.

## Reproduction and impact

1. Open a workspace with active Alpha and an opened article.
2. Create Beta; hold its follow-up GET `/api/projects` response.
3. Press the center of Biblioteca without releasing the pointer.
4. Release the GET response and await Beta's enabled name button.
5. Release the pointer at its original position.

The library does not open: the article remains visible. Completing the creation
reload moves the 38 px library control down about 20.8 px; its old center is now
outside the button. DOM identity is preserved. This was reproduced in Chromium
and Firefox with awaited response handshakes and native pointer operations, no
sleeps or retries. It is not merely a theoretical explanation for a test.

A researcher clicking Biblioteca while creation completes can experience an
apparently ignored click. No data loss, wrong active project, or failed-selection
state corruption was demonstrated.

## Proposed resolution, not implemented

Keep the library navigation target stable while project creation/loading alters
the list above it. A focused view/CSS correction is proportional if approved,
but the desired placement should be agreed first: the current navigation sits
in normal flow after the project list. Anchoring it separately changes layout
and needs overflow/viewport review. Do not synthesize a click after the pointer
has left the target or weaken native pointer semantics.

Likely product files: `src/LexScholar.Client/App.fs` and/or
`src/LexScholar.Client/wwwroot/app.css`. A regression belongs in
`tests/LexScholar.Browser.Tests/WorkspaceJourneyTests.fs`. No product file is
changed by the present test correction.

## Verification for an approved fix

Retain the held-response/pointer-down/release-response/pointer-up schedule above
in both browsers; require the library import control to become visible after
that single activation. Verify target stability and ordinary keyboard access at
supported viewport widths, including a long project list and overflow. Run
`scripts/check.sh T019`; CI runs the full suite. Capture meaningful RED against
the current behavior, then GREEN after the layout correction.

## Existing evidence and attribution limit

See [investigation](t019-failed-selection-ci.md) for measured geometry, environment
and commands. A temporary diagnostic already verified the missed activation in
both browsers. It was removed rather than encoding lost activation as desired
behavior. The saved investigation harness is
`/tmp/opencode/selection-layout-diagnostic.fs` (local, not a portable artifact);
the schedule above is the reproducible record.

The exact pointer timing in CI run 45683098346 was not captured, so attribution
of that historical failure remains probable rather than proven. That uncertainty
does not weaken the independently demonstrated product issue.
