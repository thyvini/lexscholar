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
