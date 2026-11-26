# Team validation ($16f)

2026-11-26; isolated worktree at release `f54ce98b`, OpenCode v2.4.2,
Linux, Python 3. No research data, notification destination, merge request or
release mutation was used. Raw local probe output and private synthetic state
are preserved under `/tmp/opencode/ls053-validation-*`.

## Observed

- **Actual Scout session:** `opencode run --agent scout --model
  'openai/gpt-6.1-terra#low' --format json --archived '<read-only missing
  fixture probe>'` attempted `docs/discovery/16f-nonexistent-fixture.md`.
  The read returned file not found; Scout reported the limitation and asked
  for the correct path or an authorized fixture. This is a tool failure
  handled by a successful session, not a native failed session.
- **Actual watchdog CLI and OpenCode observation:** in a private synthetic
  directory, `python3 -B scripts/watchdog.py --state <state> put --revision 0
  --assignment <assignment>` returned revision 1. The assignment referenced
  that Scout session with an intentionally expired checkpoint. A separate
  `check` returned `checkpoint_expired` and `notification: unconfigured`;
  `show` retained revision 1, the original writer and observation
  (`failed=false`, `busy=false`, no pending permission/form). This is a
  manufactured expiry of a completed session, **not** a real stall.
- **Interruption and fencing:** a child acquired `watchdog.locked`, changed
  its in-memory synthetic progress and exited via `os._exit(23)` before
  `persist`. A fresh process observed revision 1 and the original progress
  and writer. Separate CLI `put --revision 1` attempts to replace the active
  writer and to assign its worktree to a second card both exited 1; no
  assignment changed. This tests interruption before persistence, not a crash
  during fsync/rename, actual writer interruption, or end-to-end recovery.
- **Offline Bridge:** `python3 -m unittest discover -s tests/tooling -p
  'test_integration.py'` passed 18 tests. Additional synthetic fixture probes
  called `validate`: pending `build-and-test` and failed `gpt-review` were
  blocked. Mocked `integrate` with a pending snapshot followed by a changed
  head returned `head changed; restart validation explicitly`, without a
  diff or API call. These are not live GitHub gates or a real merge.
- **Offline watchdog:** `python3 -m unittest discover -s tests/tooling -p
  'test_watchdog.py'` passed 18 tests. Tests mock native signals and notification
  transport; they do not demonstrate delivery or a real failed/stalled worker.
- `gh pr list --base release/0.5.3 --state open --json
  number,title,headRefName,mergeStateStatus,url` returned `[]` during the
  probe. No eligible real PR was available for routine integration; no live
  integration was attempted.

## Synthetic reconstruction follow-up

On release `2a2b5a1`, an interrupted validation session's record showed that
the synthetic writer had signalled its `ready` FIFO before the session wrote
an assignment file and stopped without persisting its v2 ledger. The writer
process remained present, waiting at a FIFO; it was not released or replaced.
The separate older synthetic ledger refers to a different writer.

In `/tmp/opencode/ls053-reconstruction-v2`, the release watchdog copy's
`put --revision 0` persisted the existing assignment in `state/ledger.json`
at revision 1. A subsequent `show` retained the recorded session, worktree,
branch and working state. An archived actual OpenCode Conductor session
(`ses_de844e3cbffebOvYdO4WGw5581`) read the assignment and ledger, matched
their contents, distinguished recorded state from live process status and
recommended keeping the writer held rather than assigning a replacement.
Its JSON output is preserved in `state/conductor-probe.jsonl`. The prompt
explicitly prohibited dispatch, subagents, shell operations, external calls
and changes. The sandbox lacked `docs/agents/team.md` and
`docs/agents/operations.md`; this was a bounded read-only reconstruction,
not proof of active dispatch, interruption of Conductor during its own work,
or an end-to-end handoff.

The watchdog `check` recorded `status_unknown`, with `busy` and `failed` null,
no permission or form request, and `notification: unconfigured`. A process
listing separately showed the synthetic writer still present and waiting.
These observations do not establish delivery of a notification or prove the
writer's execution status from the watchdog. Offline tests at `2a2b5a1`
passed: 21 watchdog tests and 18 integration tests. They do not prove live
integration or notification delivery.

## Limits and follow-up

The watchdog is a one-shot observer, not a scheduler or execution lease. It
does not automatically resume or replace writers; the documented owner fencing
protocol is still needed. Full Conductor recovery and child handoff, a genuinely
failed or stalled Scout session, configured role models,
notification delivery, Bridge's effective shell permissions and a real gated
routine integration remain unverified. Coordinate any integration with the
central integrator. The release manifest authorizes `test/team-acceptance` for
`$16f` within its listed files, but not the `docs/agents/team.md` correction;
authorization does not replace the required integration gates.
