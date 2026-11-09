#!/usr/bin/env sh
# Install the Playwright Chromium and Firefox builds used by
# tests/LexScholar.Browser.Tests (cached per user in ~/.cache/ms-playwright).
#
# Uses Microsoft.Playwright.Program.Main from the built test output through
# `dotnet fsi`, which ships with the .NET SDK. Playwright then picks the
# bundled Node driver for the current OS and architecture, so no
# platform-specific path or PowerShell is needed.
#
# Usage: scripts/install-browsers.sh [browser ...]   (default: chromium firefox)
# Set CONFIGURATION to use a build other than Release.
set -eu

root=$(cd "$(dirname "$0")/.." && pwd)
project="$root/tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj"
configuration=${CONFIGURATION:-Release}
[ "$#" -gt 0 ] || set -- chromium firefox

dotnet build "$project" --configuration "$configuration" --nologo --verbosity quiet
target_dir=$(dotnet msbuild "$project" -getProperty:TargetDir -p:Configuration="$configuration")
assembly="${target_dir%/}/Microsoft.Playwright.dll"
[ -f "$assembly" ] || { echo "Microsoft.Playwright.dll not found in $target_dir" >&2; exit 1; }

script_dir=$(mktemp -d)
trap 'rm -rf "$script_dir"' EXIT
cat > "$script_dir/install.fsx" <<EOF
#r @"$assembly"
exit (Microsoft.Playwright.Program.Main(fsi.CommandLineArgs.[1..]))
EOF

dotnet fsi --quiet --exec "$script_dir/install.fsx" install "$@"
