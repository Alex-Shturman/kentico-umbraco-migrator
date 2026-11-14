#!/usr/bin/env bash
# Checks the old URLs of a migrated site: requests each one without following redirects and compares the status and the
# final path with the expectation.
# Usage: scripts/check-urls.sh <base-url> <file>
# File lines: "<old url> <expected status> <expected final path>"; blank lines and lines starting with # are ignored.
# The final path is the Location path for a redirect and the request path itself for any other status.
# Prints OK or FAIL per line and a summary; exits 1 when any line fails, 2 on a usage or file error.
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <base-url> <file>" >&2
  exit 2
fi
base="${1%/}"
file="$2"
if [ ! -f "$file" ]; then
  echo "File not found: $file" >&2
  exit 2
fi

ok=0
fail=0
while IFS= read -r line || [ -n "$line" ]; do
  line="${line%$'\r'}"
  case "$line" in
    '' | '#'*) continue ;;
  esac
  read -r url expected_status expected_path extra <<<"$line"
  if [ -z "${expected_path:-}" ] || [ -n "${extra:-}" ]; then
    echo "FAIL $line (expected three fields: url status path)"
    fail=$((fail + 1))
    continue
  fi

  # -k: the local host uses the development certificate; --path-as-is keeps the URL exactly as written in the file.
  result=$(curl -sk --path-as-is -o /dev/null -w '%{http_code} %{redirect_url}' "$base$url" || true)
  status="${result%% *}"
  location="${result#* }"
  if [ -n "$location" ]; then
    # redirect_url is absolute: drop the scheme and host, keep the path (and query, if any)
    final="/${location#*://*/}"
  else
    final="$url"
  fi

  if [ "$status" = "$expected_status" ] && [ "$final" = "$expected_path" ]; then
    echo "OK   $url -> $status $final"
    ok=$((ok + 1))
  else
    echo "FAIL $url: expected $expected_status $expected_path, got $status $final"
    fail=$((fail + 1))
  fi
done <"$file"

echo "$ok OK, $fail FAIL"
[ "$fail" -eq 0 ]
