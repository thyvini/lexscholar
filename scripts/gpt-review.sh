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
  local response
  response="$(as_app GET "/repos/$repo/installation")" || {
    echo "Reviewer app: 404 means it isn't installed on $repo (install it from the app's settings); 401 means app-id or private-key.pem is wrong." >&2
    return 1; }
  installation="$(json_field id <<<"$response")"
  response="$(as_app POST "/app/installations/$installation/access_tokens")" || {
    echo "Reviewer app: could not get an installation token for $repo." >&2; return 1; }
  json_field token <<<"$response"
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

# A later round sees the last review on this PR and the changes since the
# commit it reviewed, so it checks those findings and the new changes instead
# of raising new findings, one round at a time, on text already reviewed. If
# that commit is no longer in the PR's history (a force push), the round is a
# full review.
bot_login="thyvinibots-pull-request-reviewer[bot]"
previous_id="$(gh api --paginate "repos/$repo/issues/$pr/comments" \
  --jq ".[] | select(.user.login == \"$bot_login\" and (.body | startswith(\"[gpt-review\"))) | .id" | tail -1)"
previous_sha=""
if [ -n "$previous_id" ]; then
  gh api "repos/$repo/issues/comments/$previous_id" --jq .body >"$work/previous.md"
  previous_short="$(sed -n -E '1s/.*Review of ([0-9a-f]{7,40}).*/\1/p' "$work/previous.md")"
  if grep -Fxq 'COVERAGE: complete' "$work/previous.md" \
      && ! grep -Fxq 'COVERAGE: incomplete' "$work/previous.md" \
      && [ -n "$previous_short" ] && previous_sha="$(git rev-parse -q --verify "$previous_short^{commit}")" \
      && git merge-base --is-ancestor "$previous_sha" "$head_sha"; then
    git diff "$previous_sha" "$head_sha" >"$work/since.patch"
    git diff --stat=200 "$previous_sha" "$head_sha" >"$work/since-files.txt"
  else
    previous_sha=""
  fi
fi

if [ "$post" = "--post" ]; then
  token="$(bot_token)"
  check_id="$(as_bot "repos/$repo/check-runs" -f name=gpt-review -f head_sha="$head_sha" -f status=in_progress --jq .id)"
fi

# The part of the prompt only a later round has.
review_scope="This is a full review. Read all of $work/diff.patch, in sections if it is long, and review every changed file in the list before giving a verdict."
: >"$work/round.md"
if [ -n "$previous_sha" ]; then
  review_scope="Read $work/previous.md and all of $work/since.patch. Review every incremental change and verify the previous findings against the current head. Open other files or $work/diff.patch only as needed for context; do not reread the full PR diff by default."
  cat >"$work/round.md" <<EOF

This is a later round. The last review, of commit ${previous_sha:0:7}, is in $work/previous.md, and the changes since that commit are in $work/since.patch (files below). In this round:
- Check each finding of that review against the current head. Repeat the ones still open, keeping their wording; drop the fixed ones.
- New findings must come from the changes since ${previous_sha:0:7}: a problem those changes introduce or expose, in any file. A problem in text the last review already read is out of scope now, unless it is P0.

--- CHANGED SINCE ${previous_sha:0:7} ---
$(if [ -s "$work/since-files.txt" ]; then cat "$work/since-files.txt"; else echo "Nothing: the head is the commit already reviewed. Only check the last review's findings."; fi)
EOF
fi

cat >"$work/prompt.md" <<EOF
You are reviewing LexScholar pull request #$pr. The current directory is a checkout of the PR's head commit. Read AGENTS.md, docs/agents/review-guidelines.md (what to check, F# style, severities), docs/agents/codebase.md and the tasks named in the PR title (in the feature's tasks.md) there; open other files only as needed. The PR description and the list of changed files follow. $review_scope Do not modify any file.

Review against docs/agents/review-guidelines.md, including its "Rounds" section. Report every finding you verify in this one pass: text you read now and don't flag is not reviewed again in a later round, so hold nothing back for later.
$(cat "$work/round.md")

Reply with at most 40 lines, nothing else:
VERDICT: approve   (or: VERDICT: changes requested)
COVERAGE: complete   (or: COVERAGE: incomplete)
then one line per finding: [P0|P1|P2] path:line: problem, and why it matters.
Approve when there is no P0 or P1 finding; P2 findings alone don't block. COVERAGE is complete only when you read and reviewed the entire required scope for this round (the full diff for a full review; the incremental diff and previous findings for a later round). If you could not finish that scope, reply "VERDICT: changes requested", "COVERAGE: incomplete", and say what you could not review. Report only findings you verified in the code. If there are none, write "No findings."

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

if grep -Fxq 'COVERAGE: complete' "$work/review.md" \
    && ! grep -Fxq 'COVERAGE: incomplete' "$work/review.md" \
    && grep -Eiq '^[[:space:]]*VERDICT:[[:space:]]*approve' "$work/review.md" && ! grep -Eq '\[P[01]\]' "$work/review.md"; then
  conclusion=success; title="Approved by GPT ($model)"
else
  conclusion=failure; title="Changes requested by GPT ($model)"
fi
# Comment first, then complete the check: if the comment fails, the cleanup
# marks the check failed, so the gate never passes without a posted review.
printf '[gpt-review · %s] Review of %s (check gpt-review: %s)\n\n%s\n' \
  "$model" "${head_sha:0:7}" "$conclusion" "$(cat "$work/review.md")" >"$work/comment.md"
token="$(bot_token)"
as_bot "repos/$repo/issues/$pr/comments" -F body=@"$work/comment.md" >/dev/null
finish_check "$conclusion" "$title" "$(printf 'Review of %s by %s through Codex CLI.\n\n%s\n' "${head_sha:0:7}" "$model" "$(cat "$work/review.md")")"
echo "(posted to PR #$pr as the reviewer app: check gpt-review $conclusion)"
