#!/usr/bin/env bash
# T001: exercise archive path handling without repeatedly publishing the app.
# The fake dotnet checks the unchanged isolated-publish contract.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd -P)"
fixture="$(mktemp -d "${TMPDIR:-/tmp}/lexscholar-package-test.XXXXXX")"
trap 'rm -rf "$fixture"' EXIT
mkdir -p "$fixture/tools" "$fixture/caller" "$fixture/temporary"
ln -s temporary "$fixture/temp alias"
export TMPDIR="$fixture/temp alias"
export PACKAGE_TEST_CALLS="$fixture/calls"
cat > "$fixture/tools/dotnet" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$PACKAGE_TEST_CALLS"
test "$3" = -c
test "$4" = Release
physical_temp="$(cd "$TMPDIR" && pwd -P)"
case "$2" in
  "$physical_temp"/lexscholar-publish.*/source/src/LexScholar.Server/LexScholar.Server.fsproj) ;;
  *) echo "FAIL: $1 project is not under the physical temporary directory: $2" >&2; exit 41 ;;
esac
if [ "$1" = clean ]; then
  test "$5" = -warnaserror
else
  test "$1" = publish
  test "$5" = -o
  test "$6" = "${2%/source/src/LexScholar.Server/LexScholar.Server.fsproj}/publish"
  test "$7" = -warnaserror
  printf 'published fixture\n' > "$6/marker"
fi
printf 'dotnet stdout must not enter archive path output\n'
SH
# A platform without GNU realpath -m must still be able to package.
cat > "$fixture/tools/realpath" <<'SH'
#!/usr/bin/env bash
echo 'realpath: illegal option -- m' >&2
exit 1
SH
chmod +x "$fixture/tools/dotnet" "$fixture/tools/realpath"
export PATH="$fixture/tools:$PATH"
cd "$fixture/caller"
mkdir physical
ln -s physical linked
for target in "relative.tar.gz" "missing parent/nested/archive.tar.gz" "$fixture/absolute path/archive.tar.gz" "linked/archive.tar.gz"; do
  if ! actual="$("$root/scripts/package-workspace.sh" "$target")"; then
    echo "FAIL: packaging rejected output path: $target" >&2
    exit 1
  fi
  expected="$(cd "$(dirname "$target")" && pwd -P)/$(basename "$target")"
  test "$actual" = "$expected" || { echo "FAIL: expected $expected; got $actual" >&2; exit 1; }
  test "$(tar -xOzf "$actual" ./marker)" = "published fixture"
  for leftover in "$TMPDIR"/*; do
    test ! -e "$leftover" || { echo "FAIL: publish temporary directory retained" >&2; exit 1; }
  done
done
test "$(wc -l < "$PACKAGE_TEST_CALLS" | tr -d ' ')" = 8
printf 'T001 package path tests: 4 passed.\n'
