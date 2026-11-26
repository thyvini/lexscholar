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
The integration, branch-preparation and watchdog commands referenced by roles
are separate deliverables. If absent or unverified, stop the affected workflow
and report the blocker rather than substituting an unreviewed command.
