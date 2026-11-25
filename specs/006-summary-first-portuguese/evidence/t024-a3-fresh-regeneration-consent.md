# T024 / audit A3 — fresh regeneration consent

- RED: `scripts/check.sh T024` — 46 tests, 2 failed, 0 skipped. An already-confirmed plan and a local configured service both admitted unconfirmed replacements (`202`, expected `409`). Log: `/tmp/lexscholar-check.XYV2NZ`.
- GREEN: `LOG=/tmp/opencode/a3-green-final.log scripts/check.sh T024 T011 T007 T023` — warning-as-error Release build passed; 290 tests, 0 failed, 0 skipped.
- Full suite: deferred to PR CI; no PR opened by this workstream.

New HTTP coverage rejects missing, false, string and numeric confirmation for local/remote configured services and an already-confirmed plan. Rejections preserve the saved summary and issue no outgoing requests (including refresh/model calls). Explicit consent admits replacements, remote host/model matching remains required, subsequent plan replacements require consent again, and running configured replacements return their state without renewed consent or duplicate sends. Ordinary local admission remains unchanged.

Existing persistence/restart, cancellation during extraction/refresh, library-deletion fencing, legacy state isolation, instruction privacy and browser replacement-isolation tests pass. Their direct configured regeneration setup now explicitly confirms the send.

Environment: Linux, .NET 10 SDK pinned by `global.json`; isolated temporary workspaces and fake providers. These checks establish admission and request counts, not real-provider behavior. No registry, storage, host-security or ordinary-send policy changes.
