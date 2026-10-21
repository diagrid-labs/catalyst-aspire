#!/usr/bin/env bash
# `dotnet test` reports a skip as success. Read the TRX files and require at least one test that actually passed
# and none that were skipped, so a dropped opt-in variable cannot turn the nightly green having tested nothing.
set -euo pipefail

dir="${1:?usage: assert-tests-ran.sh <trx-directory>}"
mapfile -t files < <(find "$dir" -type f -name '*.trx')
if [ "${#files[@]}" -eq 0 ]; then
  echo "::error::no TRX results under $dir: the tests did not run."
  exit 1
fi

passed=$(cat "${files[@]}" | { grep -o '<UnitTestResult [^>]*outcome="Passed"' || true; } | wc -l)
skipped=$(cat "${files[@]}" | { grep -o '<UnitTestResult [^>]*outcome="NotExecuted"' || true; } | wc -l)
echo "Tests: $passed passed, $skipped skipped."

if [ "$passed" -lt 1 ]; then
  echo "::error::no test passed; the suite ran nothing (skipped counts as not run)."
  exit 1
fi
if [ "$skipped" -gt 0 ]; then
  echo "::error::$skipped test(s) were skipped; the opt-in variable must be set for the nightly."
  exit 1
fi
