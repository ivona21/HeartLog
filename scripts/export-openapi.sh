#!/usr/bin/env bash
set -euo pipefail

url="${HEARTLOG_OPENAPI_URL:-http://localhost:5048/swagger/v1/swagger.json}"
output="${1:-openapi/heartlog.openapi.json}"
output_dir="$(dirname "$output")"
tmp_file="$(mktemp)"

cleanup() {
  rm -f "$tmp_file"
}
trap cleanup EXIT

mkdir -p "$output_dir"

curl --fail --silent --show-error --location "$url" --output "$tmp_file"
python3 -m json.tool "$tmp_file" "$output"

echo "Exported OpenAPI spec from $url to $output"
