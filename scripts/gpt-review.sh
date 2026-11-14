#!/usr/bin/env bash
# Review of a pull request by GPT, through Codex CLI in read-only mode. A
# different model family from the Claude developer, so its mistakes are less
# likely to overlap. Every PR needs it (see the merge gate in AGENTS.md).
#
#   scripts/gpt-review.sh <pr> [--post]
#
# Prints a verdict line and findings. --post publishes them as the reviewer
# GitHub App (ThyviniBotsPullRequestReviewer): a `gpt-review` check run on the
# PR's head commit, which passes only with "VERDICT: approve" and no P0/P1
# finding, and a PR comment. Only a GitHub App can create check runs, so the
# check can't come from anyone else's token.
#
# Needs Codex CLI installed and signed in; CODEX_MODEL=<model> overrides
# Codex's configured model. --post also needs the app's ID (or client ID) and
# private key in $REVIEWER_CONFIG_DIR (default ~/.config/pull-request-reviewer):
# files `app-id` and `private-key.pem`. Codex's own progress output goes to a
# log file, not the terminal.
set -euo pipefail
cd "$(dirname "$0")/.."

pr="${1:?usage: scripts/gpt-review.sh <pr> [--post]}"
post="${2:-}"
command -v codex >/dev/null || { echo "Codex CLI not found; install it and run 'codex login'." >&2; exit 1; }

config="${REVIEWER_CONFIG_DIR:-$HOME/.config/pull-request-reviewer}"
if [ "$post" = "--post" ]; then
  [ -r "$config/app-id" ] && [ -r "$config/private-key.pem" ] || {
    echo "--post needs $config/app-id and $config/private-key.pem (the reviewer GitHub App)." >&2; exit 1; }
fi

work="$(mktemp -d)"
review_tree="$work/tree"
check_id=""
repo="$(git remote get-url origin | sed -E 's#(\.git)?/*$##; s#^.*github\.com[:/]##')"

# --- Acting as the reviewer app -------------------------------------------
b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }
json_field() { python3 -c 'import json, sys; print(json.load(sys.stdin)[sys.argv[1]])' "$1"; }
# A short-lived JWT signed with the app's key, exchanged for an installation
# token for this repository. Each call mints a fresh one (they last an hour).
bot_token() {
  local now header payload signature jwt installation
  now="$(date +%s)"
  header="$(printf '{"alg":"RS256","typ":"JWT"}' | b64url)"
  payload="$(printf '{"iat":%d,"exp":%d,"iss":"%s"}' $((now - 60)) $((now + 540)) "$(tr -d '[:space:]' <"$config/app-id")" | b64url)"
  signature="$(printf '%s.%s' "$header" "$payload" | openssl dgst -sha256 -sign "$config/private-key.pem" -binary | b64url)"
  jwt="$header.$payload.$signature"
  as_app() {
    curl -fsS -X "$1" "https://api.github.com$2" -H "Authorization: Bearer $jwt" \
      -H "Accept: application/vnd.github+json" -H "X-GitHub-Api-Version: 2022-11-28"
  }
  installation="$(as_app GET "/repos/$repo/installation" | json_field id)" || {
    echo "The reviewer app is not installed on $repo, or its app-id/key are wrong." >&2; return 1; }
  as_app POST "/app/installations/$installation/access_tokens" | json_field token
}
as_bot() { GH_TOKEN="$token" gh api "$@"; }
finish_check() { # conclusion title summary
  token="$(bot_token)"
  as_bot -X PATCH "repos/$repo/check-runs/$check_id" -f status=completed -f conclusion="$1" \
    -f "output[title]=$2" -f "output[summary]=$3" >/dev/null
  check_id=""
}

cleanup() {
  # A check left in progress would block the PR without saying why.
  [ -n "$check_id" ] && finish_check failure "Review did not finish" "scripts/gpt-review.sh stopped before a verdict; run it again." || true
  git worktree remove --force "$review_tree" >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT
log="${TMPDIR:-/tmp}/lexscholar-gpt-review-$pr.log"

# Review the PR's exact head commit: its files in a temporary worktree, and
# the diff and posted SHA from that same commit, whatever this checkout holds.
gh api "repos/$repo/pulls/$pr" --jq '"\(.head.sha) \(.base.ref)"' >"$work/refs.txt"
read -r head_sha base_ref <"$work/refs.txt"
gh api "repos/$repo/pulls/$pr" --jq '"Title: \(.title)\nHead: \(.head.sha)\n\n\(.body // "")"' >"$work/pr.txt"
git fetch -q origin "$base_ref"
git fetch -q origin "pull/$pr/head"
[ "$(git rev-parse FETCH_HEAD)" = "$head_sha" ] || { echo "PR #$pr moved while fetching; run again." >&2; exit 1; }
git worktree add -q --detach "$review_tree" "$head_sha"
# The full diff stays in a file GPT reads in sections, so a large PR is never
# reviewed from a truncated copy; the prompt lists every changed file.
git diff "origin/$base_ref...$head_sha" >"$work/diff.patch"
git diff --stat=200 "origin/$base_ref...$head_sha" >"$work/files.txt"

if [ "$post" = "--post" ]; then
  token="$(bot_token)"
  check_id="$(as_bot "repos/$repo/check-runs" -f name=gpt-review -f head_sha="$head_sha" -f status=in_progress --jq .id)"
fi

cat >"$work/prompt.md" <<EOF
You are reviewing LexScholar pull request #$pr. The current directory is a checkout of the PR's head commit. Read AGENTS.md, docs/agents/review-guidelines.md (what to check, F# style, severities), docs/agents/codebase.md and the tasks named in the PR title (in the feature's tasks.md) there; open other files only as needed. The PR description and the list of changed files follow. The full diff is in $work/diff.patch: read all of it, in sections if it is long, and review every changed file in the list before giving a verdict. Do not modify any file.

Review against docs/agents/review-guidelines.md. Reply with at most 20 lines, nothing else:
VERDICT: approve   (or: VERDICT: changes requested)
then one line per finding: [P0|P1|P2] path:line: problem, and why it matters.
Approve when there is no P0 or P1 finding; P2 findings alone don't block. If you could not read the whole diff, reply "VERDICT: changes requested" and say what you could not review. Report only findings you verified in the code. If there are none, write "No findings."

--- PR ---
$(cat "$work/pr.txt")

--- CHANGED FILES ---
$(cat "$work/files.txt")
EOF

model_args=()
[ -n "${CODEX_MODEL:-}" ] && model_args=(-m "$CODEX_MODEL")
# The instructions stay in a file and the diff in another, which the
# read-only sandbox can read; neither goes on the command line.
if ! codex exec --sandbox read-only --cd "$review_tree" "${model_args[@]}" --output-last-message "$work/review.md" \
    "Read $work/prompt.md and follow its instructions exactly. Your final message must be only the review it asks for." >"$log" 2>&1; then
  echo "Codex failed; see $log" >&2
  tail -5 "$log" >&2
  exit 1
fi

cat "$work/review.md"
[ "$post" = "--post" ] || exit 0

# The model Codex used, for the signature: from its log header, else the
# override or its config.
model="$(sed -n -E 's/^model:[[:space:]]*([^[:space:]]+).*/\1/p' "$log" | head -1)"
[ -n "$model" ] || model="${CODEX_MODEL:-$(sed -n -E 's/^model[[:space:]]*=[[:space:]]*"([^"]+)".*/\1/p' "${CODEX_HOME:-$HOME/.codex}/config.toml" 2>/dev/null | head -1)}"
model="${model:-unknown model}"

if grep -Eiq '^[[:space:]]*VERDICT:[[:space:]]*approve' "$work/review.md" && ! grep -Eq '\[P[01]\]' "$work/review.md"; then
  conclusion=success; title="Approved by GPT ($model)"
else
  conclusion=failure; title="Changes requested by GPT ($model)"
fi
finish_check "$conclusion" "$title" "$(printf 'Review of %s by %s through Codex CLI.\n\n%s\n' "${head_sha:0:7}" "$model" "$(cat "$work/review.md")")"
printf '[gpt-review · %s] Review of %s (check gpt-review: %s)\n\n%s\n' \
  "$model" "${head_sha:0:7}" "$conclusion" "$(cat "$work/review.md")" >"$work/comment.md"
token="$(bot_token)"
as_bot "repos/$repo/issues/$pr/comments" -F body=@"$work/comment.md" >/dev/null
echo "(posted to PR #$pr as the reviewer app: check gpt-review $conclusion)"
