#!/usr/bin/env bash
# Fail-closed secret scan over captured fixtures. The nightly pushes a branch to a PUBLIC repo, so this must pass
# before anything is committed. It prints file names and key names, never values. A false positive costs a red
# nightly; a false negative leaks a credential.
set -euo pipefail

dir="${1:-tests/fixtures}"
mock_values='["diagrid://mock-api-token","mock-app-token","redacted"]'
failed=0

# 1. No diagrid:// URI other than the mock, anywhere in any captured file (mid-string included).
if grep -rEoh 'diagrid://[^"[:space:]]*' "$dir" | grep -Fxv 'diagrid://mock-api-token' > /dev/null; then
  echo "::error::a captured file contains a diagrid:// value other than the mock token"
  grep -rEl 'diagrid://' "$dir" || true
  failed=1
fi

mapfile -t json_files < <(find "$dir" -type f -name '*.json' | sort)
for file in "${json_files[@]}"; do
  jq -e . "$file" > /dev/null || { echo "::error::$file is not valid JSON"; failed=1; continue; }

  # 2. Any token/key/secret/password/credential-named key holding a string must hold a known mock.
  bad_keys=$(jq -r --argjson mocks "$mock_values" '
      [.. | objects | to_entries[]
        | select((.key | test("token|key|secret|password|credential|bearer"; "i")) and (.value | type == "string"))
        | select(.value as $v | ($mocks | index($v)) | not)
        | .key] | unique | .[]' "$file")
  if [ -n "$bad_keys" ]; then
    echo "::error::$file: secret-looking key(s) with a non-mock value: $(echo "$bad_keys" | tr '\n' ' ')"
    failed=1
  fi

  # Captured into a variable and matched with here-strings: `grep -q` in a pipeline can SIGPIPE the writer, and
  # under pipefail that turns a match into a false "no match" (fail-open).
  strings_in_file=$(jq -r '.. | strings' "$file")

  # 3. No e-mail address other than example.com.
  if grep -E '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}' <<< "$strings_in_file" | grep -Ev '@example\.com$' > /dev/null; then
    echo "::error::$file contains an e-mail address"
    failed=1
  fi

  # 4. No long opaque string (a token, id or hash) anywhere, bar the mock id.
  if grep -Ev '^00000000-0000-0000-0000-000000000001$' <<< "$strings_in_file" | grep -E '[A-Za-z0-9_-]{32,}' > /dev/null; then
    echo "::error::$file contains a long opaque string"
    failed=1
  fi
done

if [ "$failed" -ne 0 ]; then
  echo "Fixture scan FAILED: nothing will be committed or pushed."
  exit 1
fi
echo "Fixture scan passed (${#json_files[@]} JSON files)."
