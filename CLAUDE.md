@AGENTS.md

## Claude Code

- Agent team: start it with `claude --settings .claude/team-settings.json --model sonnet`, then run `/feature-team specs/<nnn>-<name>`. The lead coordinates, so a mid-tier model is enough. Roles are in `.claude/agents/`: `developer` runs on Sonnet, `reviewer` on Opus. Teammates work in their own git worktrees under `.claude/worktrees/` (git-ignored).
- For a single task without the team, a plain session is cheaper: start a fresh one per task and point it at the task ID, rather than continuing a long session.
