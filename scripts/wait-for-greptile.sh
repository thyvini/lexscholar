#!/usr/bin/env bash
# Waits until Greptile has finished reviewing a pull request's current head
# commit, then prints its summary and every inline finding with its severity.
#
#   scripts/wait-for-greptile.sh <pr-number> [timeout-minutes]
#
# Exit codes: 0 when Greptile finished, 2 on timeout. Rerun after every push:
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
    '[.check_runs[] | select(.app.slug == "greptile-apps")] | if length == 0 then "not-started" else (.[0].status + " " + (.[0].conclusion // "")) end')"
  case "$state" in completed*) break ;; esac
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
  | sed -e 's/<[^>]*>//g' | grep -v '^[[:space:]]*$' || true

echo
echo "== Inline findings (outdated = on code that has since changed)"
gh api --paginate "repos/$repo/pulls/$pr/comments" --jq \
  '.[] | select(.user.login == "greptile-apps[bot]" and .in_reply_to_id == null)
   | (.body | capture("alt=\"(?<sev>P[0-9])\"") // {sev: "P?"}) as $badge
   | "[\($badge.sev)] \(.path):\(.line // .original_line) comment=\(.id) commit=\(.commit_id[0:7]) \(if .line == null then "OUTDATED" else "current" end)\n    \(.body | gsub("<[^>]*>"; "") | gsub("\\s+"; " ") | .[0:600])"' \
  || true
