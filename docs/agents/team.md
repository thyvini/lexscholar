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
Integration and watchdog are separate release tooling, documented in
[`integration.md`](integration.md) and [`operations.md`](operations.md).
No `scripts/team-branch.sh` is supplied; use operator-prepared worktrees.
Conductor can request approval for watchdog commands and automation creation or
deletion. Bridge can request approval for the documented trusted-copy command;
the operator must first prepare and verify that copy as described in
`integration.md`. Do not substitute candidate-PR tooling.

These permissions are **not unattended enforcement**: `automation_create`
accepts arbitrary actions, the shell rules match text rather than Python
arguments or script content, and a writable script can change after inspection.
The operator must inspect each requested command/action and approve **once**,
never save a broad approval or use auto-approval for these operations. Approve
only the documented one-shot watchdog action or the intended trusted Bridge
invocation, with its scope and gates. External-directory checks may require
separate approval. Other shell commands remain denied for these roles. If
approval or a trusted copy is unavailable, report the affected workflow blocked.

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
