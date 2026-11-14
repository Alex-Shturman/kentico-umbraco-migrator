#!/usr/bin/env bash
# Repository guard (Constitution I): no client material, no em dashes in tracked content.
set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

fail=0
em_dash=$(printf '\342\200\224')
# core.quotepath=off keeps non-ASCII path names readable and matchable.
tracked() { git -c core.quotepath=off ls-files; }

# 1. Forbidden paths and file types (case-insensitive).
bad_paths=$(tracked | grep -iE '^(sources|_work|runs)/|\.(bak|mdf|ldf)$' || true)
if [ -n "$bad_paths" ]; then
  echo "FAIL: forbidden tracked paths:"
  echo "$bad_paths"
  fail=1
fi

# 2. U+2014 in tracked file names.
bad_names=$(tracked | grep -F "$em_dash" || true)
if [ -n "$bad_names" ]; then
  echo "FAIL: em dash in tracked file names:"
  echo "$bad_names"
  fail=1
fi

# 3. U+2014 in tracked content (index, not working tree), except vendored Spec Kit files.
bad_content=$(git -c core.quotepath=off grep -n -I -F "$em_dash" -- . ':!.specify' ':!.claude/skills/speckit-*' || true)
if [ -n "$bad_content" ]; then
  echo "FAIL: em dash in tracked content:"
  echo "$bad_content" | head -20
  fail=1
fi

if [ "$fail" -ne 0 ]; then
  exit 1
fi
echo "OK: no client material, no em dashes"
