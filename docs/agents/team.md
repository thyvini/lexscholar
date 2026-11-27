# Permanent agent team

Roles are persistent definitions; sessions are disposable. Conductor assigns
only ready work and does not keep simultaneous sessions alive without ready
work. Every writer works in an isolated worktree; parallel assignments must
have non-overlapping file ownership. Follow `AGENTS.md` for planning, tests,
review, Codecks and release gates.

| Role | Responsibility |
| --- | --- |
| Conductor | Operational coordination, dependency scheduling and escalation. |
| Sage | Product direction within delegated authority; Spec Kit specify and clarify; acceptance against the approved specification. |
| Atlas | Technical lead; Spec Kit plan and tasks, dependencies and technical decisions. |
| Scout | Bounded discovery and evidence for the team. |
| Lambda | Assigned backend implementation. |
| Pixel | Assigned frontend implementation. |
| Sentinel | Independent review and findings, not implementation. |
| Bridge | Integration and reconciliation of delivery evidence and Codecks. |

## Working sequence

1. Preserve the owner's intent and identify the approved feature boundary.
   Sage consults [`../product/vision-roadmap.md`](../product/vision-roadmap.md),
   [`../product/decision-log.md`](../product/decision-log.md), and the relevant
   authoritative feature artifacts. Load the actual `speckit-specify` and
   `speckit-clarify` skills when those workflows are needed; don't substitute a
   bare slash-command reference for the skill instructions.
2. Escalate decisions reserved to the owner. Atlas plans and orders bounded
   vertical slices once product scope is sufficiently resolved. Respect the
   size budget and approval requirements in `AGENTS.md`.
3. Conductor dispatches only unblocked work with a clear brief, owned files,
   worktree and completion criteria. Lambda and Pixel implement their assigned
   slices; Scout investigates only the assigned questions. Avoid duplicate
   writers and preserve existing work when resuming.
4. Sentinel reviews independently. Bridge reconciles checks, reviews and
   Codecks against the actual PR head and integration result. Routine task PR
   integration is authorized only within the approved feature and after the
   gates in `AGENTS.md`; an exception or failed gate goes to the owner.
5. Sage checks delivered behavior and evidence against the approved feature.
   The owner alone authorizes publication to `main`, tagging and release.

Role prompts and tool permissions are operational aids, not proof of a
security boundary: shell commands and indirect tools can have side effects.
Use the least access practical, verify the effective configuration, and do not
rely on a prompt or a single deny rule to enforce reserved decisions.

The team profiles use OpenCode V2's `.opencode/agents/` discovery and
`opencode.jsonc` configuration. Skills are loaded by their exact IDs through
the skill tool. Configured models, effective permissions, child-session
handoffs and tracker access must be verified in the actual deployment; a
profile alone does not establish that a model or integration is available.
Integration ([PR #182](https://github.com/thyvini/lexscholar/pull/182)) and
watchdog ([PR #181](https://github.com/thyvini/lexscholar/pull/181)) have landed
on `release/0.5.3`. They are separate release tooling, documented in
[`integration.md`](integration.md) and [`operations.md`](operations.md).
The integration script is `scripts/integrate-pr.py`, not `scripts/integrate-pr.sh`.
No `scripts/team-branch.sh` is supplied; Conductor prepares persistent worktrees.
Conductor can run the documented watchdog commands and one-shot automation.
Bridge prepares and runs the documented persistent trusted-copy command
and verified as described in `integration.md`. Do not substitute candidate-PR
tooling.

These permissions are **not unattended enforcement**: `automation_create`
accepts arbitrary actions, the shell rules match text rather than Python
arguments or script content, and a writable script can change after inspection.
Use only the documented one-shot watchdog action or the intended trusted Bridge
invocation, with its scope and gates. Conductor's shell allowlist also permits
repository inspection, worktree preparation and private checkpoint setup; it
does not assign application implementation to itself. If a trusted copy is
unavailable, report the workflow blocked.

Routine developer tests, inspection and ordinary branch pushes are permitted by
the project rules within an approved feature. Shell patterns are not an argument
parser or a security boundary: their push deny patterns do not cover every
spelling or indirect invocation. Never use force, delete remote refs, push
directly to release or main, or bypass the review and CI gates. Unmatched shell
commands and external directories ask for approval; `/tmp/opencode/*` is
allowed for worktrees and trusted Bridge tooling. Sentinel's role still denies
external access. These patterns match command text, not parsed arguments: even
an allowed Git command or script can have side effects, and shell directory
inference is best effort. They are not filesystem or network isolation. Saved
approvals and higher-priority configuration can change effective behavior;
broader managed or harness policy can also prompt or deny actions.

See
[team acceptance evidence](team-acceptance-16f.md) for observed limitations;
merged tooling does not itself prove end-to-end team operation.

### Writer handoff

The delegation tool returns a child session ID only after starting the child.
Before starting one, Conductor records a private reservation naming the card,
task, branch, persistent worktree, ownership and fencing decision; this reserves
the writer slot and is **not** a watchdog assignment or a fabricated session.
Start the child with a read-only readiness brief: it must not edit, run mutating
commands, launch writers or begin implementation, and must stop and report
ready. Record the returned actual session ID and create the complete watchdog
assignment using the current revision. Only after that write succeeds may
Conductor follow up in the **same** child session with the implementation brief.
If the child does not stop ready, its ID is unavailable, the checkpoint fails,
or follow-up to that session is unsupported, do not authorize any writer.
Retain the reservation until the child is fenced under `operations.md`;
an unknown child is not permission to start a replacement.

For separate tool calls, create the private state directory and set mode 700
before writing files; create reservation/assignment JSON there with the file
tool and set mode 600. A standalone `umask` call does not affect later calls.
Likewise, shell variables do not persist across calls: pass the full state path
to every watchdog command, even where an older operations example uses `STATE`.

### Local verification (2026-11-26)

In this worktree, OpenCode v2.4.2 `opencode debug agents` discovered all eight
project role IDs. An archived, non-mutating `opencode run --agent sage` probe
called the skill tool for `speckit-specify`; it loaded from this worktree's
`.agents/skills/speckit-specify`. A separate Sage probe of `speckit-plan`
returned `Permission denied: skill`. Both used `openai/gpt-6.1-terra#low` as
an explicit test override, not Sage's configured model; neither ran the skill
workflow. This verifies representative skill permission behavior only.
Subagent handoffs, every configured model, other permission paths, effective
external-directory approvals, Codecks mutation, integration and watchdog
operation remain unverified. Recheck after the separate tooling lands or the
effective global/project configuration changes.

Follow-up on the merged release: OpenCode v2.4.2 initially denied Conductor's
`python3 -B scripts/watchdog.py --help` and Bridge's
`python3 /tmp/opencode/bridge/integrate-pr.py --help`. With these profile
changes, isolated archived `opencode run --auto` probes ran those harmless
`--help` commands; the Bridge file was copied from the fetched release.
An unrelated Conductor `python3 --version` remained denied even with `--auto`.
`opencode debug agents` reported `ask` for both command patterns and for
Conductor's automation create/delete. `--auto` was used only for these
non-mutating probes, not for operation. No automation was created, no
integration or notification executed, and interactive approval behavior and
complete workflows were not tested.

### Persistent-operation repair probe

On OpenCode v2.4.2, isolated archived sessions without `--auto` exercised the
candidate configuration: Conductor created a private assignment, wrote a
separate watchdog state at revision 1, and delegated a read-only title check to
Sentinel, which returned the result. Lambda ran `dotnet build --help` and
committed a staged disposable repository. Lambda's attempted force-push to
`main` was denied; Sentinel had neither shell nor edit tools. The separate
probe assignment was subsequently marked done. These checks do not establish
that every command is allowed or that text patterns isolate the filesystem.

The persistent Bridge copy and manifest lifecycle still require a live Bridge
validation after this configuration is bootstrapped onto the trusted release.
The candidate checkout is not a trusted release checkout, and the existing
release role does not permit the new persistent invocation. A developer
session's attempted invocation from the clean release checkout was denied.
Do not describe this as a verified integration or bypass the trusted bootstrap.

### Routine-default probe

This historical probe predates the narrower project defaults above; its Lambda
observations no longer describe the current policy.

On OpenCode v2.4.2, isolated archived sessions without `--auto` ran Lambda's
`python3 --version` and external `/etc/hostname` read, Conductor's watchdog
`--help`, and Bridge's trusted-copy `--help`. Lambda's force-push help and
trusted-copy help were denied; Sentinel had no shell tool. These harmless probes
do not exercise a real push, integration, scheduled action, or every command
spelling. Automation permissions cannot restrict the payload: Conductor must use
only the documented one-shot action, never an arbitrary or recurring action.

For the narrowed defaults, `python3 tests/test_opencode_permissions.py` checks
representative project rule matches, not OpenCode's shell scanner. On v2.4.2,
`opencode debug agents` showed the later role rules: Sentinel denies shell,
edit and external access, Conductor denies an unrelated shell command and
allows its watchdog pattern, and Bridge allows its review pattern but denies
PR merge. Lambda's unmatched shell and `/etc/*` external rules resolve to ask,
while its status command and `/tmp/opencode/bridge/*` resolve to allow.
This inspection does not test saved approvals, actual tool execution, scanner
coverage or every deployment's configuration.
