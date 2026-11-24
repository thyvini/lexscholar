# T018 — Automated security/privacy; owner validation pending

**Status: automated portion complete; owner manual validation PENDING. T018 remains
unchecked. No owner acceptance is asserted.**

Base: `origin/release/0.5.0` at `1848a3b` (T017 merged). Developer B, GPT-6.1 Sol.
Linux 7.3.5-2-cachyos x86_64, .NET SDK 10.0.114; installed Playwright Chromium
revision 1248 and Firefox revision 1555. All storage uses `TemporaryWorkspace`,
with `TMPDIR=/tmp/opencode`, synthetic `tests/Fixtures/valid.pdf` and synthetic
metadata/summaries. No real research documents or external credentials.

## Coverage

- `LibraryControlsSecurityTests.fs`: all five new POST routes (export, preview,
  confirmation, retry, acknowledgement) reject forged Host with matching forged
  Origin, foreign Origin, missing/invalid CSRF token and a token without its cookie.
  Assert exact security problem code, permitted handler status as a positive
  control, and no admitted deletion intent.
- Real public export and deletion lifecycle through completion, retry and
  acknowledgement: original byte equality; credential/settings/API-key/path
  canaries absent from all ZIP entry names/content; preview omits summary prose;
  outcome omits private content/paths; operation logs contain no sensitive canaries.
  The injected outgoing summary/auth handler records zero calls.
- Injected export read exception and deletion processor exception include private
  content/path canaries: safe errors, durable partial/storage_error, no prose leak
  in responses or logs. Malformed confirmation input is not echoed.
- `LibraryControlsJourneyTests.fs`: Chromium and Firefox export/download, preview,
  cancel, confirm, complete and acknowledge; exact ZIP allowlist; no secrets/paths;
  cancel preserves the original and confirmation removes it. Context-wide routing
  begins before initial navigation, records and aborts every off-origin request.

## Test-first / sensitivity

These protect existing behavior, so no fabricated implementation RED was used.
Tests were added first and passed on the unchanged production implementation.
Each mutation below was temporary and removed; none is committed.

All commands: `TMPDIR=/tmp/opencode LOG=<log> scripts/check.sh T018`.
The script builds the solution with `-warnaserror`, then runs only task-tagged
tests. T018 also matches 12 existing browser cases from earlier features.

| Deliberate mutation | Log under `/tmp/opencode/` | Result |
|---|---|---|
| Bypass `validHost` in HostSecurity | `t018-host-red.log` | RED: 21 total, 5 failed; all routes reach domain status instead of 403 |
| Restore Host; bypass `validOrigin` | `t018-origin-red.log` | RED: 21 total, 5 failed; all routes reach domain status instead of 403 |
| Restore Origin; bypass antiforgery verdict | `t018-csrf-red.log` | RED: 21 total, 5 failed; tokenless calls reach domain status instead of 403 |
| Restore CSRF; include unrelated appsettings file in ZIP | `t018-zip-red.log` | RED: 21 total, 3 failed; server finds settings canary, both browsers reject extra ZIP entry |
| Restore ZIP; add sensitive export log line and off-origin fetch in download JS | `t018-egress-log-red.log` | RED: 21 total, 3 failed; server finds log canary, both browsers find blocked off-origin URL |
| Restore all production code | `t018-green.log` | GREEN: see final result below |

Initial log assertions included the ASP.NET startup “Content root path” banner.
`TestHarness` deliberately makes content root equal its temporary data root.
Observation now starts after startup/fixture setup, before the first control
request; assertions still cover every operation log, including captured exception
text. This is not a change to production logging or a claim about startup banners.

Final command:
`TMPDIR=/tmp/opencode LOG=/tmp/opencode/t018-green.log scripts/check.sh T018`

Final result: **21 passed, 0 failed, 0 skipped** (14 browser-project cases,
including the 2 new Chromium/Firefox cases; 7 new server cases). Build passed with
`-warnaserror`. `git diff --check` passed. No production mutations remain.

## Scope and limitations

- No production code changes or new packages. All sensitivity mutations restored.
- Outgoing-server assertion observes the existing injected summary/auth HTTP
  capability, not an OS-wide packet monitor or arbitrary future `HttpClient`.
  Browser assertions cover this context's routed HTTP(S) requests, not all machine
  traffic. No external request in the deliberate browser mutation was sent:
  context routing aborted it.
- Standard startup lifecycle banners are outside operation-log observations.
  Legitimate export content includes names and saved summaries; unavailable-document
  errors may identify the document by name/ID per contract.
- Partial failure/recovery and summary cancellation at every durable/async boundary
  remain the earlier task-tagged proofs (T006/T011/T014/T016/T017), not a new claim of
  power-loss durability, physical secure erasure or external-copy removal.
- Full suite not run locally. CI URL: **pending PR/full-suite run** (no push or PR
  authorized for this implementation handoff).

## Owner manual validation — PENDING

Follow [quickstart](../quickstart.md) separately in Firefox and Chromium with
fresh isolated fixture workspaces. The permission-based partial-result check
requires a non-root user. Automated headless journeys do not replace these checks.

| Required record | Firefox | Chromium |
|---|---|---|
| Owner/date, browser version, OS/runtime, commit | Pending | Pending |
| ZIP originals, summaries/context, page citations checked manually | Pending | Pending |
| Preview/cancel, changed impact, association-removal distinction | Pending | Pending |
| Partial result, unchanged-permission restart, retry/recovery | Pending | Pending |
| Privacy/network, interrupted download, external ZIP remains | Pending | Pending |
| Observed limitations/failures and acceptance decision | Pending | Pending |

Owner manual acceptance: **not performed / not recorded**.
Full-suite CI link: **pending**.

## PR #158 review follow-up

Both GPT P2 findings: **must fix**.

- Quickstart now explicitly exports the copied `F002_DATA` path in the second
  shell before Python reads it; the placeholder must be replaced with the setup
  shell's printed fixture path.
- Log canary assertions now execute after the acknowledgement POST, covering that
  final operation as well as export, preview, confirmation and retry.
- Sensitivity: temporarily added an acknowledgement-only `PRIVATE-SUMMARY-T018`
  warning in `LibraryDeletionEndpoints.fs`. Command
  `TMPDIR=/tmp/opencode LOG=/tmp/opencode/t018-ack-log-red.log scripts/check.sh T018`:
  **RED, 21 total, 1 failed, 0 skipped**; the lifecycle test detected the sensitive
  acknowledgement log. Mutation removed, with no production diff retained.
- Restored command
  `TMPDIR=/tmp/opencode LOG=/tmp/opencode/t018-review-green.log scripts/check.sh T018`:
  **GREEN, 21 passed, 0 failed, 0 skipped**; solution build with `-warnaserror`
  and `git diff --check` passed.

Owner manual validation remains **pending**; T018 is still unchecked. These fixes
do not assert owner acceptance or a new full-suite CI result.
