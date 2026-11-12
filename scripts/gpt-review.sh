#!/usr/bin/env bash
# Second-opinion review of a pull request by GPT, through Codex CLI in
# read-only mode. A different model family from the Claude developer, so its
# mistakes are less likely to overlap. Used for risky slices (see AGENTS.md).
#
#   scripts/gpt-review.sh <pr> [--post]
#
# Prints a verdict line and findings. --post also adds them to the PR as a
# comment. Needs Codex CLI installed and signed in; CODEX_MODEL=<model>
# overrides Codex's configured model. Codex's own progress output goes to a
# log file, not the terminal.
set -euo pipefail
cd "$(dirname "$0")/.."

pr="${1:?usage: scripts/gpt-review.sh <pr> [--post]}"
post="${2:-}"
command -v codex >/dev/null || { echo "Codex CLI not found; install it and run 'codex login'." >&2; exit 1; }

work="$(mktemp -d)"
review_tree="$work/tree"
cleanup() { git worktree remove --force "$review_tree" >/dev/null 2>&1 || true; rm -rf "$work"; }
trap cleanup EXIT
log="${TMPDIR:-/tmp}/lexscholar-gpt-review-$pr.log"

# Review the PR's exact head commit: its files in a temporary worktree, and
# the diff and posted SHA from that same commit, whatever this checkout holds.
repo="$(git remote get-url origin | sed -E 's#(\.git)?/*$##; s#^.*github\.com[:/]##')"
gh api "repos/$repo/pulls/$pr" --jq '"\(.head.sha) \(.base.ref)"' >"$work/refs.txt"
read -r head_sha base_ref <"$work/refs.txt"
gh api "repos/$repo/pulls/$pr" --jq '"Title: \(.title)\nHead: \(.head.sha)\n\n\(.body // "")"' >"$work/pr.txt"
git fetch -q origin "$base_ref"
git fetch -q origin "pull/$pr/head"
[ "$(git rev-parse FETCH_HEAD)" = "$head_sha" ] || { echo "PR #$pr moved while fetching; run again." >&2; exit 1; }
git worktree add -q --detach "$review_tree" "$head_sha"
git diff "origin/$base_ref...$head_sha" >"$work/diff.patch"
max=200000
if [ "$(wc -c <"$work/diff.patch")" -gt "$max" ]; then
  head -c "$max" "$work/diff.patch" >"$work/diff.trimmed" && mv "$work/diff.trimmed" "$work/diff.patch"
  echo $'\n[diff truncated at 200 KB]' >>"$work/diff.patch"
fi

cat >"$work/prompt.md" <<EOF
You are reviewing LexScholar pull request #$pr. The current directory is a checkout of the PR's head commit. Read AGENTS.md, docs/agents/codebase.md and the tasks named in the PR title (in the feature's tasks.md) there; open other files only as needed. The PR description and diff follow. Do not modify any file.

Check, in this order:
1. Correctness, especially partial failures: what happens if the process stops between two steps, or an operation is retried after such a stop. Startup must never be able to block the app permanently.
2. Scope: does it do what its tasks ask and nothing more (constitution Principle V)?
3. Tests: they check behavior, a RED failed on assertions, and tests for existing behavior have a sensitivity check.
4. Conventions in AGENTS.md: functional-first F#, dependency direction, host security untouched, no sleeps in tests.

Reply with at most 20 lines, nothing else:
VERDICT: approve   (or: VERDICT: changes requested)
then one line per finding: [P0|P1|P2] path:line: problem, and why it matters.
P0 = data loss or security; P1 = must fix before merging; P2 = worth fixing. Report only findings you verified in the code. If there are none, write "No findings."

--- PR ---
$(cat "$work/pr.txt")

--- DIFF ---
$(cat "$work/diff.patch")
EOF

model_args=()
[ -n "${CODEX_MODEL:-}" ] && model_args=(-m "$CODEX_MODEL")
# The instructions and diff stay in a file: a long diff would exceed the
# command-line argument limit. The read-only sandbox can still read it.
if ! codex exec --sandbox read-only --cd "$review_tree" "${model_args[@]}" --output-last-message "$work/review.md" \
    "Read $work/prompt.md and follow its instructions exactly. Your final message must be only the review it asks for." >"$log" 2>&1; then
  echo "Codex failed; see $log" >&2
  tail -5 "$log" >&2
  exit 1
fi

cat "$work/review.md"
if [ "$post" = "--post" ]; then
  printf 'GPT review (Codex CLI) of %s:\n\n%s\n' "${head_sha:0:7}" "$(cat "$work/review.md")" | gh pr comment "$pr" --body-file - >/dev/null
  echo "(posted to PR #$pr)"
fi
