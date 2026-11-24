#!/bin/sh
set -eu

if [ "$#" -ne 1 ]; then
  printf 'Usage: sh tools/check-legal-snapshot.sh <frontend-legal-directory>\n' >&2
  exit 2
fi

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
snapshot="$root/docs/legal/snapshots/2026-11-23"
frontend=$1

(cd "$snapshot" && shasum -a 256 -c SHA256SUMS)
for document in terms.md privacy-policy.md; do
  if ! cmp -s "$snapshot/$document" "$frontend/$document"; then
    printf 'Legal document differs from snapshot: %s\n' "$document" >&2
    exit 1
  fi
done
printf 'Frontend legal documents match the 2026-11-23 snapshot.\n'
