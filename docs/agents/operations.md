# Conductor operations ($16e)

`scripts/watchdog.py` is a Python 3 standard-library, Linux, **one-shot**
observer. `scripts/notify.py` owns its controlled ntfy adapter. No framework,
daemon, shared OpenCode configuration or application behavior changes.

## Set up durable assignments

Run from the tooling checkout. Keep operational state and credentials outside
Git, backed up locally. Do not put research, messages, raw commands, logs or
secrets into assignment prose. Use the seven existing team cards; do not create
replacement cards. Only Conductor owns assignments; workers report checkpoints
to Conductor, rather than independently editing this file.

```sh
umask 077
mkdir -p "$HOME/.local/state/lexscholar-team"
chmod 700 "$HOME/.local/state/lexscholar-team"
STATE="$HOME/.local/state/lexscholar-team/state.json"
python3 -B scripts/watchdog.py --state "$STATE" show
```

Create an owner-only `assignment.json` in that directory. Example (replace the
session/path/checkpoint with the actual assignment; timestamps are Unix seconds):

```json
{
  "card": "$16e",
  "task": "Deliver team watchdog",
  "role": "developer",
  "session": "ses_REPLACE",
  "worktree": "/tmp/opencode/ls053-watchdog",
  "branch": "chore/team-watchdog",
  "pr": null,
  "dependencies": [],
  "decisions": ["Tooling-only plan approved"],
  "state": "working",
  "progress": "Writing offline tests",
  "checkpoint": 1795668000,
  "blockers": [],
  "attempts": 0,
  "wait": null,
  "alert_epoch": 0
}
```

```sh
python3 -B scripts/watchdog.py --state "$STATE" put --revision 0 \
  --assignment "$HOME/.local/state/lexscholar-team/assignment.json"
python3 -B scripts/watchdog.py --state "$STATE" check
```

For each subsequent update, first `show`, then supply its current `revision`
and a **complete** assignment. Stale writes fail. `flock` serializes all updates
and checks; same-directory replace plus file/directory fsync makes state durable.
The lock file must not be removed while any invocation is running. State files
are 0600. Assignment revision changes only on assignment writes, not observations.
Do not manually edit state while an invocation is active.

`dependencies` names existing card keys (register prerequisites first); cycles
are rejected. Each unfinished assignment uniquely owns its session, canonical
worktree and branch. `state` is `working`, `blocked` or `done`. `attempts` counts
owner-approved writer recovery attempts; it is not a heartbeat. Set `pr` to the
PR number when opened. Decisions and blockers persist across invocations.

## Observe and recover safely

The installed OpenCode **v2.4.2** was checked against the official
[V2 API](https://opencode.ai/docs/api) and the installed `/openapi.json`.
Only these targeted GETs are used, via `opencode api` (native discovery/auth):

- `/api/session/{sessionID}`: unwrap `data`, project `outcome` and `time.busy/idle`.
- `/api/session/{sessionID}/permission`: pending native permission count.
- `/api/session/{sessionID}/form`: pending native question count.

No session listing, message/history/log reading, auth dump, global permission
listing or execution mutation. Permissions/forms are observed, never answered
or approved. Each targeted read makes at most two immediate attempts; successful
signals are retained if another read fails. Each subprocess is limited to 10
seconds (at most 60 seconds per assignment). Output and
errors from OpenCode stay captured and are not relayed to ntfy.

Classification prioritizes native permission, question, explicit failed outcome,
then observation unavailable, recorded blocker, expected wait and checkpoint.
A checkpoint expiry is **not** a failed session. Native busy/idle is separately
recorded; idle is not proof of task completion or permission to replace a writer.
Success never marks the assignment done automatically. There is no portable
native CI/command/external-wait inference: declare a bounded wait explicitly:

```json
{"kind": "ci", "until": 1795668600}
```

`kind` can also be `command` or `external`. Until that time the result is
`expected_wait`; afterwards `wait_expired`, distinct from failure. Independent
assignments are still inspected when another assignment is blocked, awaiting a
dependency or has invalid notifier configuration.

Recovery is deliberately bounded and read-only: retry observation once, then
ask Conductor. **Never automatically resume, interrupt, fork, replace or
reassign a writer.** On failure/expiry, inspect the existing targeted session.
If replacement is necessary, the owner first confirms old tools/processes have
stopped (idle alone is insufficient), reconciles uncommitted changes and records
the fencing decision, marks the old assignment `done`, then assigns the new
session with incremented `attempts`. Stop after two unsuccessful writer recovery
attempts and ask the owner; the watchdog itself makes zero writer attempts.
This is a human fencing protocol, not an OpenCode distributed execution lease.

## Time-boxed native trigger (no new service)

Prefer the existing native session completion/permission/form notifications for
immediate attention. Add an explicit **one-shot** native automation at the next
checkpoint so silence is noticed too. With the automation connector available,
Conductor requests the directly exposed `automation_create` tool (Code Mode
is denied in this project's configuration):

```js
{
  name: "LexScholar checkpoint (one shot)",
  trigger: {type: "at", at: "in 10 minutes"},
  action: {
    type: "shell",
    command: "cd /ABSOLUTE/TOOLING/CHECKOUT && python3 -B scripts/watchdog.py --state /ABSOLUTE/PRIVATE/state.json check"
  }
}
```

Replace the three absolute paths before submitting; quote shell paths if needed.
Use `--notify-config /ABSOLUTE/PRIVATE/ntfy.json` only after the setup below.
Save the returned automation ID in Conductor's durable decisions. Arm the next
one-shot only after reconciling this checkpoint; cancel obsolete IDs with
`automation_delete` with the ID. Both require per-call operator approval; the
operator must check the complete payload, not save a broad approval. Do not
create an unbounded repeating
job or run a permanent shell loop. No automation was installed by this change.
Without the connector, run the one-shot command manually at the declared
checkpoint and report scheduling as **unconfigured**, not monitored.

## Private ntfy opt-in

No default destination exists. With no `--notify-config`, actionable results
report `notification: unconfigured`, with **no delivery claim and no dedup
marker**. Setup must be explicitly authorized by the owner:

1. Choose an owner-approved HTTPS ntfy server and one dedicated topic. Configure
   server-side ACLs to deny anonymous read/write and grant only intended accounts
   publish/subscribe. A hard-to-guess public topic is **not** private.
2. Create a restricted publish token and subscribe the owner's device using its
   authenticated account. Verify anonymous read/write are denied using the
   server's admin controls. The script cannot prove server-side privacy.
3. Outside every Git checkout, create a private directory and 0600 `ntfy.json`
   with the following shape; fill secrets locally, never in chat or arguments:

   ```json
   {
     "url": "https://YOUR-APPROVED-SERVER/YOUR-PRIVATE-TOPIC",
     "token": "YOUR_RESTRICTED_PUBLISH_TOKEN",
     "private_topic_confirmed": true
   }
   ```

4. Run `check --notify-config /absolute/private/ntfy.json` with an actual
   actionable assignment. This is the first possible external send. Confirm on
   the subscribed device. `delivered` means HTTP 2xx acknowledgement, not that a
   person read it. Do not run this step without explicit destination approval.

The adapter requires owner-only regular credentials outside Git, authenticated
HTTPS, exactly one topic URL, no URL credentials/query/fragment, verified TLS,
no redirects or environment proxies and a 10-second network timeout. There is
no arbitrary destination/message CLI. It sends only allowlisted kind,
fixed question/recommendation/impact, card code and PR number. Assignment prose,
paths, native prompts, raw errors and research never enter the payload.

Dedup identity is card + session + `alert_epoch` + kind. The private state records
`send_uncertain` **before** network I/O. Timeout, crash or ambiguous send is never
automatically retried; repeated invocations do not duplicate that incident.
`delivered` is persisted only after acknowledgement. Inspect the topic locally
after uncertainty. If a deliberate resend/new incident is warranted, Conductor
records the decision and increments `alert_epoch` via `put`; this may duplicate
an earlier uncertain send and must be an explicit choice. Do not delete the
alerts ledger. Invalid config reports `configuration_error`, never delivered.

## Verification ($16e)

- RED: `python3 -B -m unittest discover -s tests/tooling -v`: 5 tests,
  4 assertion failures against initial no-op classifier/notifier.
- GREEN: same command: 16 tests passed (classification, native projection,
  bounded reads, independent work, atomic persistence, stale revision,
  duplicate writers, dependency cycles, private HTTPS, redirects and
  uncertain/success dedup). Entirely offline, fake transport; no ntfy sent.
- `scripts/check.sh`: Release solution build with `-warnaserror` passed.
- Review regressions: two assertion failures before the fixes, then 18 offline
  tests passed; classification uses the time after each assignment's observation,
  and partial reads preserve known permission, question and failure signals.
- Native smoke: only this developer's session metadata, forms and permissions
  queried; verified `data` envelope and zero pending forms/permissions. No
  messages, credentials or unrelated session contents inspected.
- CI runs the full .NET suite on the PR; parent owns tooling CI wiring.
  No installed scheduler or end-to-end private ntfy delivery is claimed.
