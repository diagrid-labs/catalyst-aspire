#!/usr/bin/env bash
# Fail-closed secret scan over captured fixtures. The nightly pushes a branch to a PUBLIC repo, so this must pass
# before anything is committed. It prints file names and key names, never values. A false positive costs a red
# nightly; a false negative leaks a credential.
#
# Two tiers:
#  - STRICT: tests/fixtures/*.json (top level). These hold data read from the account, so they get every check.
#  - STATIC: everything below tests/fixtures/*/ (the embedded OpenAPI schemas and help text). They are printed
#    by the CLI without contacting the account and contain no account data, so applying the account-data heuristics
#    (long identifiers, key-name matches) would only produce permanent false positives, which would leave the
#    nightly red and suppress the drift PR. They still get the diagrid:// check and must be valid, non-empty JSON.
#
# Pipelines are avoided on purpose: under pipefail a reader that exits early (grep -q, or GNU grep with stdout on
# /dev/null) kills its writer with SIGPIPE, and an `if` then reads the non-zero status as "no match" (fail-open).
# Every check captures output into a variable instead.
#
# Residual risk (not covered): a short secret under a non-secret key name (e.g. {"name":"password","value":"x"}),
# short project ids, non-e-mail owner names, and any check beyond diagrid:// on non-JSON files.
set -euo pipefail

dir="${1:-tests/fixtures}"
mock_values='["diagrid://mock-api-token","mock-app-token","redacted"]'
mock_uuid='00000000-0000-0000-0000-000000000001'
failed=0

if [ ! -d "$dir" ]; then
  echo "::error::fixtures directory '$dir' does not exist: nothing was scanned."
  exit 1
fi

# Strict files: top level only. Must exist, or the scan would pass vacuously.
find_output=$(find "$dir" -maxdepth 1 -type f -name '*.json' | sort)
mapfile -t strict_files <<< "$find_output"
if [ -z "$find_output" ] || [ "${#strict_files[@]}" -lt 1 ]; then
  echo "::error::no JSON fixtures directly under '$dir': nothing was scanned."
  exit 1
fi

# 1. No diagrid:// value other than the mock, in ANY captured file (JSON or text, mid-string included).
uris=$(grep -rEoh 'diagrid://[^"[:space:]]*' "$dir" || true)
bad_uris=$(grep -Fxv 'diagrid://mock-api-token' <<< "$uris" || true)
if [ -n "$bad_uris" ]; then
  echo "::error::a captured file contains a diagrid:// value other than the mock token"
  grep -rEl 'diagrid://' "$dir" || true
  failed=1
fi

# STATIC tier: valid, non-empty JSON.
static_files=()
static_output=$(find "$dir" -mindepth 2 -type f -name '*.json' | sort)
if [ -n "$static_output" ]; then
  mapfile -t static_files <<< "$static_output"
  for file in "${static_files[@]}"; do
    if ! jq -e 'type == "object" and length > 0' "$file" > /dev/null 2>&1; then
      echo "::error::$file is not a non-empty JSON object"
      failed=1
    fi
  done
fi

# STRICT tier.
for file in "${strict_files[@]}"; do
  if ! jq -e . "$file" > /dev/null 2>&1; then
    echo "::error::$file is not valid JSON"
    failed=1
    continue
  fi

  # 2. Under any token/key/secret/password/credential-named key, EVERY string in the value (nested objects and
  #    arrays included) must be a known mock.
  bad_keys=$(jq -r --argjson mocks "$mock_values" '
      [.. | objects | to_entries[]
        | select(.key | test("token|key|secret|password|credential|bearer"; "i"))
        | . as $entry
        | $entry.value | [.. | strings | select(. as $v | ($mocks | index($v)) | not)] | select(length > 0)
        | $entry.key] | unique | .[]' "$file")
  if [ -n "$bad_keys" ]; then
    echo "::error::$file: secret-looking key(s) with a non-mock value: $(tr '\n' ' ' <<< "$bad_keys")"
    failed=1
  fi

  strings_in_file=$(jq -r '.. | strings' "$file")

  # 3. No e-mail address other than example.com.
  emails=$(grep -E '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}' <<< "$strings_in_file" || true)
  bad_emails=$(grep -Ev '@example\.com$' <<< "$emails" || true)
  if [ -n "$bad_emails" ]; then
    echo "::error::$file contains an e-mail address"
    failed=1
  fi

  # 4. No long opaque string (a token, id or hash), bar the mock id.
  non_mock=$(grep -Fxv "$mock_uuid" <<< "$strings_in_file" || true)
  opaque=$(grep -E '(^|[^A-Za-z0-9_-])[A-Za-z0-9_-]{32,}($|[^A-Za-z0-9_-])' <<< "$non_mock" || true)
  if [ -n "$opaque" ]; then
    echo "::error::$file contains a long opaque string"
    failed=1
  fi
done

if [ "$failed" -ne 0 ]; then
  echo "Fixture scan FAILED: nothing will be committed or pushed."
  exit 1
fi
echo "Fixture scan passed (${#strict_files[@]} strict JSON file(s), ${#static_files[@]} static)."
