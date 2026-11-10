#!/usr/bin/env bash
# Waits until Greptile has finished reviewing a pull request's current head
# commit, then prints its summary and every inline finding with its severity.
#
#   scripts/wait-for-greptile.sh <pr-number> [timeout-minutes]
#
# Exit codes: 0 when Greptile finished a review, 2 on timeout, 3 when its run
# ended without a review (cancelled, timed out, skipped...), and nonzero when
# the findings can't be fetched. A "failure" conclusion is a finished review
# that found problems, not an error. Rerun after every push:
# Greptile reviews each new head commit again, and only the run on the current
# head counts. (The no-sleeps rule in CLAUDE.md is for tests, not this script.)
set -euo pipefail

pr="${1:?usage: scripts/wait-for-greptile.sh <pr-number> [timeout-minutes]}"
timeout_minutes="${2:-20}"
repo="$(git remote get-url origin | sed -E 's#(\.git)?/*$##; s#^.*github\.com[:/]##')"
head="$(gh api "repos/$repo/pulls/$pr" --jq .head.sha)"
deadline=$(( $(date +%s) + timeout_minutes * 60 ))

while :; do
  state="$(gh api "repos/$repo/commits/$head/check-runs" --jq \
    '[.check_runs[] | select(.app.slug == "greptile-apps")] | sort_by(.started_at) | if length == 0 then "not-started" else (last | .status + " " + (.conclusion // "")) end')"
  case "$state" in
    "completed success" | "completed failure" | "completed neutral") break ;;
    completed*)
      echo "Greptile's run on ${head:0:7} ended without a review ($state). Retrigger it from the PR; this does not pass the gate." >&2
      exit 3 ;;
  esac
  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "Timed out after ${timeout_minutes} min waiting for Greptile on ${head:0:7} (last state: $state)." >&2
    exit 2
  fi
  sleep 30
done

echo "Greptile finished on PR #$pr head ${head:0:7}: $state"
if [ "$(gh api "repos/$repo/pulls/$pr" --jq .head.sha)" != "$head" ]; then
  echo "WARNING: the PR head changed while waiting; rerun this script." >&2
fi

echo
echo "== Summary"
gh api --paginate "repos/$repo/issues/$pr/comments" --jq \
  '.[] | select(.user.login == "greptile-apps[bot]") | .body' \
  | sed -e 's/<[^>]*>//g' | awk 'NF'

echo
echo "== Inline findings (outdated = on code that has since changed)"
gh api --paginate "repos/$repo/pulls/$pr/comments" --jq \
  '.[] | select(.user.login == "greptile-apps[bot]" and .in_reply_to_id == null)
   | (.body | capture("alt=\"(?<sev>P[0-9])\"") // {sev: "P?"}) as $badge
   | "[\($badge.sev)] \(.path):\(.line // .original_line) comment=\(.id) commit=\(.commit_id[0:7]) \(if .line == null then "OUTDATED" else "current" end)\n    \(.body | gsub("<[^>]*>"; "") | gsub("\\s+"; " ") | .[0:600])"'
