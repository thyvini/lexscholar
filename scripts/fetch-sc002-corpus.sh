#!/usr/bin/env bash
# Downloads the SC-002 import corpus (T044) listed in a manifest into
# <corpus-dir>/pdfs/ and verifies each file's SHA-256 against the manifest.
#
#   scripts/fetch-sc002-corpus.sh specs/001-research-workspace/sc002-corpus.csv corpus/sc002
#
# The manifest is CSV with the header
#   arxiv_id,version,set,url,license,license_url,bytes,pages,sha256
# and no quoted fields. Files are named <arxiv_id><version>.pdf and fetched from
# https://arxiv.org/pdf/<arxiv_id><version>, one request every 3 seconds, at most
# 100,000,000 bytes each. Files already present are not downloaded again.
#
# A row with an empty sha256 (candidate selection) prints the computed hash
# instead of verifying it. The script only downloads and hashes: it never opens,
# parses or runs the PDFs. Exit status is non-zero if any download or hash
# check failed.
set -euo pipefail

if [[ $# -ne 2 ]]; then
    echo "usage: $0 <manifest.csv> <corpus-dir>" >&2
    exit 2
fi
manifest=$1
pdfs="$2/pdfs"
expected_header="arxiv_id,version,set,url,license,license_url,bytes,pages,sha256"
user_agent="lexscholar-corpus-fetch/1.0"

header=$(head -n 1 "$manifest" | tr -d '\r')
if [[ "$header" != "$expected_header" ]]; then
    echo "unexpected manifest header: $header" >&2
    exit 2
fi
mkdir -p "$pdfs"

failures=0
first=1
while IFS=, read -r arxiv_id version _set url _license _license_url _bytes _pages sha256; do
    sha256=${sha256%$'\r'}
    # Names and URLs come only from validated identifiers, never from free text.
    if ! [[ "$arxiv_id" =~ ^[0-9]{4}\.[0-9]{4,5}$ && "$version" =~ ^v[0-9]+$ ]]; then
        echo "FAIL invalid identifier: $arxiv_id$version" >&2
        failures=$((failures + 1)); continue
    fi
    expected_url="https://arxiv.org/pdf/$arxiv_id$version"
    if [[ "$url" != "$expected_url" ]]; then
        echo "FAIL $arxiv_id$version: url $url is not $expected_url" >&2
        failures=$((failures + 1)); continue
    fi
    target="$pdfs/$arxiv_id$version.pdf"
    if [[ ! -f "$target" ]]; then
        if [[ $first -eq 0 ]]; then sleep 3; fi
        first=0
        if ! curl --fail --silent --show-error --location --proto '=https' \
                --user-agent "$user_agent" --max-filesize 100000000 \
                --output "$target.part" "$expected_url"; then
            rm -f "$target.part"
            echo "FAIL $arxiv_id$version: download failed" >&2
            failures=$((failures + 1)); continue
        fi
        mv "$target.part" "$target"
    fi
    actual=$(sha256sum "$target" | cut -d ' ' -f 1)
    if [[ -z "$sha256" ]]; then
        echo "HASH $arxiv_id$version $actual"
    elif [[ "$actual" != "$sha256" ]]; then
        echo "FAIL $arxiv_id$version: sha256 $actual does not match the manifest" >&2
        failures=$((failures + 1))
    else
        echo "OK   $arxiv_id$version"
    fi
done < <(tail -n +2 "$manifest")

if [[ $failures -ne 0 ]]; then
    echo "$failures entries failed" >&2
    exit 1
fi
