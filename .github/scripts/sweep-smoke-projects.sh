#!/usr/bin/env bash
# Deletes leaked smoke projects: name starts with aspire-smoke- AND createdAt is more than 3 hours old.
# Fails loudly: a CLI error, non-JSON output or a failed delete turns the step red. A project with a missing or
# unparseable createdAt is SKIPPED, never deleted (it could be a developer's in-flight run in the same account) --
# but the skipped ones are reported as a warning, because a timestamp format this cannot parse makes the whole
# sweep silently inert, which is exactly how the fromdateiso8601-only version shipped broken.
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

# CLI 1.109.0 emits createdAt as "2026-10-20 05:17:48": space-separated UTC, NOT ISO 8601 (verified against a live
# account, 26/26 projects). fromdateiso8601 rejects that, so parsing with it alone skipped every project and the
# sweep deleted nothing. Accept both spellings; anything else yields null and is skipped. strptime+mktime read the
# broken-down time as UTC, matching the CLI. status.updatedAt carries a ".000" fraction, hence the sub().
jq_defs='
def parse_ts:
  sub("\\.[0-9]+"; "")
  | (try fromdateiso8601 catch null)
    // (try (strptime("%Y-%m-%d %H:%M:%S") | mktime) catch null);

def smoke_projects:
  [.. | objects
   | select((.metadata? | type) == "object" and ((.metadata.name? // null) | type) == "string")
   | select(.metadata.name | startswith($prefix))];

def created_epoch:
  (.metadata.createdAt? // null) | if type == "string" then parse_ts else null end;
'

# tr -d '\r': a Windows jq writes CRLF, and `mapfile -t` strips only the LF, leaving a CR inside each name.
# Harmless on the ubuntu runner this normally executes on; it keeps a local Windows run honest.
scanned=$(jq --arg prefix "$prefix" "$jq_defs smoke_projects | length" "$list" | tr -d '\r')
# mapfile cannot see a failure inside its process substitution, so a broken jq program would read as "nothing to
# sweep". Make the count prove jq ran instead.
if ! [[ "$scanned" =~ ^[0-9]+$ ]]; then
  echo "::error::could not count smoke projects (jq returned '$scanned'); nothing was swept."
  exit 1
fi

mapfile -t old < <(jq -r --arg prefix "$prefix" --argjson max "$max_age_seconds" \
  "$jq_defs smoke_projects | map(select(created_epoch != null and created_epoch < (now - \$max))) | .[].metadata.name" "$list" | tr -d '\r')
mapfile -t unparseable < <(jq -r --arg prefix "$prefix" \
  "$jq_defs smoke_projects | map(select(created_epoch == null)) | .[].metadata.name" "$list" | tr -d '\r')

echo "Sweep: scanned $scanned smoke project(s), deleting ${#old[@]} older than $((max_age_seconds / 3600))h; the rest are young or lack a usable createdAt and are left alone."

if [ "${#unparseable[@]}" -gt 0 ]; then
  echo "::warning::${#unparseable[@]} of $scanned smoke project(s) have a missing or unparseable createdAt and were NOT deleted: ${unparseable[*]}. If that is all of them, the CLI's timestamp format has changed and this sweep is inert."
fi

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
