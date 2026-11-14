@AGENTS.md

## Claude Code

- Agent team: start it with `claude --settings .claude/team-settings.json --model sonnet`, then run `/feature-team specs/<nnn>-<name>`. The lead coordinates, so a mid-tier model is enough. The only teammate role is `developer` (`.claude/agents/developer.md`, Sonnet), one fresh teammate per slice; every PR gets a GPT review through `/pr-review <pr>` (`scripts/gpt-review.sh`: Codex CLI signed in on this machine, posting as the reviewer GitHub App). Teammates work in their own git worktrees under `.claude/worktrees/` (git-ignored).
- For a single task without the team, a plain session is cheaper: start a fresh one per task and point it at the task ID, rather than continuing a long session.
