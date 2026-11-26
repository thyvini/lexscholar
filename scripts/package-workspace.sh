#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "Usage: scripts/package-workspace.sh <archive.tar.gz>" >&2
  exit 2
fi
root="$(cd "$(dirname "$0")/.." && pwd)"
archive="$(realpath -m "$1")"
mkdir -p "$(dirname "$archive")"
publish="$(mktemp -d "${TMPDIR:-/tmp}/lexscholar-publish.XXXXXX")"
trap 'rm -rf "$publish"' EXIT
dotnet publish "$root/src/LexScholar.Server/LexScholar.Server.fsproj" -c Release -o "$publish" -warnaserror >&2
tar -C "$publish" -czf "$archive" .
printf '%s\n' "$archive"
