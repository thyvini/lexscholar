#!/usr/bin/env bash
# Quiet build and test for agents: prints only build errors, failed tests and a
# one-line summary. The full output goes to a log file; read it only when
# something fails.
#
#   scripts/check.sh                  build only
#   scripts/check.sh T012 [T013 ...]  build, then only the tests tagged with those tasks
#   scripts/check.sh --all            build, then the full suite (CI runs it on every PR)
#
# Exits non-zero when the build fails, a test fails, a test is skipped, or no
# test matched (each requested task must match at least one test file). Each
# run writes its own log, so parallel worktrees don't mix results; LOG=<path>
# overrides it.
set -uo pipefail
cd "$(dirname "$0")/.."
log="${LOG:-$(mktemp "${TMPDIR:-/tmp}/lexscholar-check.XXXXXX")}"
: >"$log"

plain() { sed -E 's/\x1B\[[0-9;?]*[A-Za-z]//g' "$log"; }

if ! dotnet build LexScholar.sln -c Release -warnaserror -nologo -v q -tl:off >>"$log" 2>&1; then
  echo "BUILD FAILED"
  plain | grep -E ': (error|warning) [A-Z]+[0-9]+' | sed -E 's/ \[[^]]*\]$//' | sort -u | head -30
  echo "Full log: $log"
  exit 1
fi
[ $# -eq 0 ] && { echo "Build OK."; [ -z "${LOG:-}" ] && rm -f "$log"; exit 0; }

status=0
if [ "$1" = "--all" ]; then
  dotnet test --solution LexScholar.sln -c Release --no-build >>"$log" 2>&1 || status=1
else
  # Run only the test projects whose files carry the requested Task traits.
  filters=()
  projects=()
  unmatched=()
  for task in "$@"; do
    filters+=(--filter-trait "Task=$task")
    found=0
    while IFS= read -r file; do
      project=$(ls "$(dirname "$file")"/*.fsproj 2>/dev/null | head -1)
      [ -n "$project" ] && { projects+=("$project"); found=1; }
    done < <(grep -rlE "\"Task\", \"$task\"" tests --include='*.fs')
    [ "$found" -eq 1 ] || unmatched+=("$task")
  done
  if [ ${#unmatched[@]} -gt 0 ]; then
    echo "No tests are tagged with: ${unmatched[*]}"
    exit 1
  fi
  mapfile -t projects < <(printf '%s\n' "${projects[@]}" | sort -u)
  for project in "${projects[@]}"; do
    dotnet test --project "$project" -c Release "${filters[@]}" >>"$log" 2>&1 || status=1
  done
fi

# Failed tests: the "failed <name>" line and its first few indented lines.
plain | awk '
  /^[[:space:]]*failed [^:]/ { print; shown = 0; inside = 1; next }
  inside && /^[[:space:]]+[^[:space:]]/ { if (shown++ < 6) print; next }
  { inside = 0 }'

read -r total failed skipped < <(plain | awk '
  /^[[:space:]]+total: [0-9]+/   { t += $2 }
  /^[[:space:]]+failed: [0-9]+/  { f += $2 }
  /^[[:space:]]+skipped: [0-9]+/ { s += $2 }
  END { print t + 0, f + 0, s + 0 }')

echo "Tests: $total total, $failed failed, $skipped skipped."
if [ "$total" -eq 0 ]; then
  echo "No tests ran."
  status=1
elif [ "$skipped" -gt 0 ]; then
  echo "Skipped tests are not allowed."
  status=1
fi
if [ "$status" -ne 0 ]; then
  [ "$failed" -eq 0 ] && plain | tail -20
  echo "Full log: $log"
elif [ -z "${LOG:-}" ]; then
  rm -f "$log"
fi
exit "$status"
