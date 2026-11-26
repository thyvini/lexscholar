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
watchdog ([PR #181](https://github.com/thyvini/lexscholar/pull/181)) are
separate, unmerged deliverables. The integration proposal uses
`scripts/integrate-pr.py`, not `scripts/integrate-pr.sh`; its trusted-release
bootstrap and final invocation must be reconciled with Bridge's permissions
after review. No `scripts/team-branch.sh` is supplied here. Until applicable
tooling is reviewed and available on the release branch, use operator-prepared
worktrees and manual checkpoints; Bridge reports integration blocked. Do not
substitute an unreviewed command or rely on candidate-PR tooling.

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
