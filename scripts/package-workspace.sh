#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "Usage: scripts/package-workspace.sh <archive.tar.gz>" >&2
  exit 2
fi
root="$(cd "$(dirname "$0")/.." && pwd)"
case "$1" in
  /*) archive="$1" ;;
  *) archive="$PWD/$1" ;;
esac
mkdir -p "$(dirname "$archive")"
archive="$(cd "$(dirname "$archive")" && pwd -P)/$(basename "$archive")"
build="$(mktemp -d "${TMPDIR:-/tmp}/lexscholar-publish.XXXXXX")"
trap 'rm -rf "$build"' EXIT
mkdir -p "$build/source" "$build/publish"
# Keep the candidate independent of checkout outputs and of other running tests.
tar -C "$root" --exclude=bin --exclude=obj -cf - \
  global.json Directory.Build.props Directory.Packages.props src | tar -C "$build/source" -xf -
dotnet clean "$build/source/src/LexScholar.Server/LexScholar.Server.fsproj" -c Release -warnaserror >&2
dotnet publish "$build/source/src/LexScholar.Server/LexScholar.Server.fsproj" -c Release -o "$build/publish" -warnaserror >&2
tar -C "$build/publish" -czf "$archive" .
printf '%s\n' "$archive"
