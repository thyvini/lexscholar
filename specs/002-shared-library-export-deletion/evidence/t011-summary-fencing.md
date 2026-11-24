# T011 — Fence summary preparations and jobs

## Verification

- RED: `LOG=/tmp/opencode/t011-red.log scripts/check.sh T011` — build passed; 44 tests, 2 failing assertions, zero skips. After test-only durable admission, ordinary and replacement jobs remained `Running` instead of absent/the previous saved version. Earlier invalid test settings were corrected before this meaningful RED.
- GREEN: `LOG=/tmp/opencode/t011-green.log scripts/check.sh T011` — 44 tests passed, zero skips.
- Sensitivity: `LOG=/tmp/opencode/t011-sensitivity.log scripts/check.sh T011` — temporarily treating every missing association as a cancelled preparation made the missing-document contract regression fail (`404` expected, `409` actual). Mutation removed.
- Final GREEN (expanded races and regressions): `LOG=/tmp/opencode/t011-green-final.log scripts/check.sh T011 T010 T024 T003` — build with `-warnaserror`; 251 tests passed, zero failures/skips.
- Full suite/CI: not run locally. No push/PR requested; CI run pending the integrator's PR.

## Boundaries exercised

Linux, .NET 10 pinned SDK; in-process host, real SQLite and isolated `TemporaryWorkspace` directories, synthetic fixture PDFs only. No real provider calls; configured-service and ChatGPT HTTP handlers are fakes.

Thirteen T011 cases cover both ordinary and replacement extraction preparations (including an extractor ignoring cancellation), credential refresh, configured multi-chunk jobs, plan multi-chunk jobs, completion before intent, two projects sharing one document, late complete/fail/progress after intent, stale confirmation, failed journal persistence, unrelated work, and preservation of existing missing-document/association HTTP responses. All waits are task handshakes bounded to 20 seconds; no sleep or readiness polling. The configured/plan provider cancellation tests retain the running task before fencing removes its registry entry, then await cancellation and settlement and verify exactly one content request and no new stored version. A synthetic noncooperative worker invokes late callbacks after fencing and verifies no save/state change.

Admission holds workspace mutation → registry → SQLite through the intent commit and subsequent cancellation. No await or PDF I/O occurs under the registry lock. Failed commit or changed impact does not cancel jobs. Completion winning first changes the revision and requires a fresh preview; later intent cancellation does not itself remove that saved row.

Only the existing isolated test endpoint composes `admitFenced`. Production confirmation/retry remain unmapped (T010 regression verified); no metadata/original removal or startup deletion recovery is enabled. Pending-item checks reject new preparations; fencing cancels preparations registered before awaits. Existing T003 lock-order and T024 nondestructive replacement regressions remain green.

## Limitations

Cancellation cannot recall content already sent to a provider. A noncooperative extraction adapter can finish its local read, but cannot admit a job afterward. This slice does not execute durable metadata/file deletion, crash recovery or retries (T012–T016); it retains originals and saved rows until those steps. No new technology or package.

## T006 release integration

Integrated `origin/release/0.5.0` at `a5e1595` (T006), preserving both guide sections. `LOG=/tmp/opencode/t011-t006-integration-final.log scripts/check.sh T011 T010 T006` passed: warning-as-error build, 197 tests, zero failures/skips. The first attempt hit the shell's 120-second timeout without reported failures; the complete rerun used a 300-second limit. No production deletion enablement or PR merge.
