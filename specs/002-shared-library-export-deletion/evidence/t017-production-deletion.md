# T017 — Production deletion and Portuguese controls

## Environment and safety

- Base: `origin/release/0.5.0` at `f337768` (T008 and T009–T016 merged).
- Linux x86_64, .NET SDK `10.0.114`, Playwright `1.64.1`; bundled Chromium and Firefox, headless.
- All destructive tests use `TemporaryWorkspace`/`TestHarness` isolated directories and fixture bytes. No real researcher workspace, research PDFs or credentials were used. No production host was launched against the developer's data directory.
- Existing mutation/file coordination, summary fencing, journal transactions and managed-key unlinking are reused unchanged. No package, migration, service or security exemption added.

## RED, sensitivity and GREEN

| Stage | Command | Result |
|---|---|---|
| RED, before enabling production | `scripts/check.sh T017` | 5 tests, 3 assertion failures: confirmation/retry returned 404, startup retained the original instead of completing the admitted deletion. Build succeeded. |
| Reconciliation sensitivity | `scripts/check.sh T017` with the `Deletion.Select` initial-reconciliation guard temporarily removed | 23 tests, 1 assertion failure: a new selection became possible before restoration/after failed lookup. Mutation reverted; not committed. |
| GREEN with prerequisite regressions | `scripts/check.sh T017 T008 T009 T010 T011 T012 T013 T014 T015 T016` | 412 tests passed, 0 failures, 0 skips; Release solution build with `-warnaserror`, zero warnings. |
| Final GREEN after strengthening the navigation assertion | `scripts/check.sh T017` | 25 tests passed, 0 failures, 0 skips; Release solution build with `-warnaserror`, zero warnings. |

Initial RED log: `/tmp/lexscholar-check.NOrV68`; sensitivity log: `/tmp/lexscholar-check.seFoJE`. Successful check logs are automatically removed by the script. `git diff --check` passed.

## What was verified

- Real production routes now admit and process confirmed selections, accept retry, expose retained results and preserve Host/Origin/CSRF protections for every new POST.
- Ordinary and Production-environment startup recover durable intents before orphan cleanup; a test-only recovery override cannot disable production recovery. Journal-read failure remains sanitized and does not authorize orphan removal.
- Chromium and Firefox: labelled native checkboxes select a shared PDF and an unassociated PDF; preview names both projects, roles and saved-summary impact, permanence and external-copy/physical-erasure limitations.
- Cancel creates no journal intent and preserves all PDFs/summaries. Changing a project name after preview causes refreshed impact and explicit reconfirmation before admission.
- Complete results correspond to absent selected managed originals and summaries; the unselected PDF remains available, including after host restart.
- Lost admission responses are injected both **before** admission (empty retained-operation list) and **after** real admission. Reload retains the frozen UUID/IDs/revision; retry sends the byte-identical confirmation, never infers success or authorizes a fresh operation from an empty lookup.
- Injected original-removal failure reports `not_completed`/partial, survives browser reload, offers retry and becomes complete only after real removal succeeds. Only completed results can be dismissed.
- Existing project-only removal still preserves the PDF, other association and other project's summary in both browsers.
- Re-ran T008–T016 tests, including real crash-boundary recovery, summary/import races, transaction rollback, storage failures and export. Historic pre-T017 visibility assertions were updated to current activation/validation expectations; tests intentionally examining pending restart/retry state explicitly suppress recovery in isolated Testing hosts.

## Limits and remaining gates

- No full-suite local run, push or PR, as instructed. CI `build-and-test` and `gpt-review` must run on the eventual PR head; no CI URL exists yet.
- T018 owner/manual acceptance in both browsers remains unchecked. Headless journeys verify behavior and accessible native controls, not owner visual acceptance or physical secure erasure.
- Storage, hardware/power-loss and external-copy limits remain those in the approved plan; no secure erase or retraction from external services is claimed.
- An in-flight/uncertain confirmation is retained in same-tab session storage before its POST; retained server operations supply cross-tab/restart results. New selection is blocked until initial reconciliation succeeds. Standard application result polling is not used as a test readiness mechanism; tests await HTTP/Playwright assertions and existing processor handshakes.

## PR #157 review follow-up — four P1 findings

Read the full GPT review of `f550911` and the independent review. All four findings are **must fix**:

1. A matching retained result left the session marker pending while allowing acknowledgement.
2. Definitive admission rejections were incorrectly retained as uncertainty.
3. Vanished documents remained in the editable selection after refresh.
4. An automatic result fetch hid a failed retry/acknowledgement error.

### RED and GREEN

- **RED:** `scripts/check.sh T017` — 33 tests, **12 assertion/journey failures**, zero skips; log `/tmp/lexscholar-check.rAETVq`. Four pure-state regressions each failed on the original behavior. Both browsers reproduced the acknowledgement-with-stale-marker lockout, definitive rejection lockout, vanished-selection preview failure and hidden failed-retry feedback. Only the new failure-case union scaffold existed at RED; no fix was active.
- **GREEN:** `scripts/check.sh T017` — **38 passed**, zero skips/warnings.
- **Final prerequisite regression GREEN:** `scripts/check.sh T017 T008 T009 T010 T011 T012 T013 T014 T015 T016` — **425 passed**, zero failures/skips/warnings.
- These baseline failures are the regression sensitivity evidence for all four findings; no manufactured compiler-error RED.

### Safety and race checks

- Matching requires the operation UUID and exact full item-ID list, not merely a complete-looking status. Matching running/partial/complete results reconcile; acknowledgement waits for successful session-marker removal. Failed cleanup retains pending state; stale cleanup callbacks cannot alter another operation.
- Browser cleanup is an atomic compare-and-remove against the frozen serialized confirmation. Both browsers verify a late cleanup cannot erase a newer marker. API rejection/acceptance uses the same helper before releasing pending state.
- Only specified confirmation-POST status/code pairs prove rejection. Classifier tests keep generic/lookup 404, malformed responses, 403, unknown codes and 5xx uncertain. Existing lost-response-before-admission journeys still preserve and resend the byte-identical UUID/IDs/revision after an empty retained list.
- Both browsers now recover a known completed operation after response loss/reload, acknowledge it with no persisted pending marker left behind, and successfully delete a second document. A competing real processor removes a document after preview; the original admission gets definitive `document_not_found`, releases its marker and permits another confirmed deletion.
- Refresh intersects editable selections with current document IDs, including documents removed from local results. Frozen previews/pending requests retain their approved IDs; cancelling returns to a pruned selection.
- Both browsers inject failed retry **and** failed acknowledgement, then observe a later result snapshot with a changed display-name sentinel before checking that failure feedback remains. This prevents a transient pre-fetch assertion from passing. The next successful explicit action clears the error.
- Server admission/storage/coordination/security are unchanged. All testing remains in isolated fixture workspaces. CI/review must rerun on the pushed fix; no merge performed.
