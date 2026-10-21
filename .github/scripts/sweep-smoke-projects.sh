#!/usr/bin/env bash
# Deletes leaked smoke projects: name starts with aspire-smoke- AND createdAt is more than 3 hours old.
# Fails loudly: a CLI error, non-JSON output or a failed delete turns the step red. A project with a missing or
# unparseable createdAt is SKIPPED, never deleted (it could be a developer's in-flight run in the same account).
set -euo pipefail

prefix="aspire-smoke-"
max_age_seconds=10800

list="$(mktemp)"
trap 'rm -f "$list"' EXIT

diagrid project list --output json --no-color=true > "$list" < /dev/null
if ! jq -e . "$list" > /dev/null 2>&1; then
  echo "::error::'diagrid project list --output json' did not return JSON; nothing was swept."
  exit 1
fi

# Every prefixed project, wherever it sits in the document.
select_all='[.. | objects
  | select((.metadata? | type) == "object" and ((.metadata.name? // null) | type) == "string")
  | select(.metadata.name | startswith($prefix))]'

# Old enough: createdAt present, parseable and older than the cutoff.
select_old="$select_all | map(select(
  ((.metadata.createdAt? // null) | type) == \"string\"
  and (try (.metadata.createdAt | sub(\"\\\\.[0-9]+\"; \"\") | fromdateiso8601) catch null) != null
  and ((.metadata.createdAt | sub(\"\\\\.[0-9]+\"; \"\") | fromdateiso8601) < (now - \$max)) ))"

scanned=$(jq --arg prefix "$prefix" "$select_all | length" "$list")
mapfile -t old < <(jq -r --arg prefix "$prefix" --argjson max "$max_age_seconds" "$select_old | .[].metadata.name" "$list")
echo "Sweep: scanned $scanned smoke project(s), deleting ${#old[@]} older than $((max_age_seconds / 3600))h; the rest are young or lack a usable createdAt and are left alone."

failures=0
for name in "${old[@]}"; do
  case "$name" in
    "$prefix"*) ;;
    *) echo "::error::refusing to delete '$name'"; failures=1; continue ;;
  esac
  echo "Deleting leaked project $name"
  # </dev/null: the CLI must not read the rest of the loop's input.
  if ! diagrid project delete "$name" --wait --yes --no-color=true < /dev/null; then
    echo "::error::failed to delete $name"
    failures=1
  fi
done

exit "$failures"
