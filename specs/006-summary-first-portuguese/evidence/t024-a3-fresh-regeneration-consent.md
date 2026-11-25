# T024 / audit A3 — fresh regeneration consent

- RED: `scripts/check.sh T024` — 46 tests, 2 failed, 0 skipped. An already-confirmed plan and a local configured service both admitted unconfirmed replacements (`202`, expected `409`). Log: `/tmp/lexscholar-check.XYV2NZ`.
- GREEN: `LOG=/tmp/opencode/a3-green-final.log scripts/check.sh T024 T011 T007 T023` — warning-as-error Release build passed; 290 tests, 0 failed, 0 skipped.
- Full suite: deferred to PR CI; no PR opened by this workstream.

New HTTP coverage rejects missing, false, string and numeric confirmation for local/remote configured services and an already-confirmed plan. Rejections preserve the saved summary and issue no outgoing requests (including refresh/model calls). Explicit consent admits replacements, remote host/model matching remains required, subsequent plan replacements require consent again, and running configured replacements return their state without renewed consent or duplicate sends. Ordinary local admission remains unchanged.

Existing persistence/restart, cancellation during extraction/refresh, library-deletion fencing, legacy state isolation, instruction privacy and browser replacement-isolation tests pass. Their direct configured regeneration setup now explicitly confirms the send.

Environment: Linux, .NET 10 SDK pinned by `global.json`; isolated temporary workspaces and fake providers. These checks establish admission and request counts, not real-provider behavior. No registry, storage, host-security or ordinary-send policy changes.

## PR #166 review follow-up

- **P1 concurrent admission — must fix:** an unconfirmed request paused inside extraction must adopt a replacement admitted meanwhile. The confirmation boundary now rechecks running state under the existing registry lock, with cancellation/association fencing first. Ordinary confirmation behavior is unchanged.
- RED: `LOG=/tmp/opencode/a3-p1-red.log scripts/check.sh T024` — all three deterministic extraction-race cases (local configured, remote configured, ChatGPT) returned `409`, expected `200 running`. An additional test initially used invalid credential-fixture metadata; corrected to a valid UUID URN and plan scope before its sensitivity check.
- **P2 expiring confirmed credentials coverage — must fix:** a new regression seeds valid already-confirmed credentials expiring in five minutes and asserts zero outgoing requests, unchanged credentials and unchanged saved summary on unconfirmed rejection.
- Sensitivity: temporarily disabled only the fresh ChatGPT consent guard (uncommitted); `LOG=/tmp/opencode/a3-p2-sensitivity.log scripts/check.sh T024` — the new test failed `Assert.Empty`, observing `POST auth.openai.com/api/accounts/oauth/token`. Restored the guard before GREEN.
- GREEN: `LOG=/tmp/opencode/a3-review-green.log scripts/check.sh T024 T011 T007 T023` — warning-as-error Release build passed; 294 tests, 0 failed, 0 skipped. Includes existing cancellation/deletion fences, ordinary sends, persistence and browser isolation. No sleeps/polling; race tests use held extraction and provider handshakes.
- Full-suite CI and independent re-review remain PR gates; no self-review performed.
