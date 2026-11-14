#!/usr/bin/env bash
# Repository guard (Constitution I): no client material, no em dashes in tracked files.
set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

fail=0

# 1. Forbidden paths and file types.
bad_paths=$(git ls-files | grep -E '^(Sources|sources|_work|runs)/|\.(bak|mdf)$' || true)
if [ -n "$bad_paths" ]; then
  echo "FAIL: forbidden tracked paths:"
  echo "$bad_paths"
  fail=1
fi

# 2. U+2014 in tracked text files, except vendored Spec Kit files.
em_dash=$(printf '\342\200\224')
while IFS= read -r f; do
  case "$f" in
    .specify/*|.claude/skills/speckit-*) continue ;;
  esac
  [ -f "$f" ] || continue
  # Skip binary files.
  if ! grep -Iq . "$f" 2>/dev/null; then continue; fi
  if grep -n -F "$em_dash" "$f" >/dev/null 2>&1; then
    echo "FAIL: em dash in $f:"
    grep -n -F "$em_dash" "$f" | head -3
    fail=1
  fi
done < <(git ls-files)

if [ "$fail" -ne 0 ]; then
  exit 1
fi
echo "OK: no client material, no em dashes"
