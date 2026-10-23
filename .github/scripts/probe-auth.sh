#!/usr/bin/env bash
# Authenticated-read probe: `diagrid project list` (read-only), used as a positive and a negative control.
#   probe-auth.sh expect-authenticated    exit 0 only if the read SUCCEEDS (the credential works)
#   probe-auth.sh expect-unauthenticated  exit 0 only if the read FAILS with an unauthenticated signature
# Anything else (a usage error, a network error, a timeout) is neither: it exits non-zero, so an unrelated failure can
# never be read as "logged out" (the last round's probe was vacuous because it failed with a usage error either way).
# `timeout`: an unauthenticated CLI may try to start a browser device-code login and hang.
set -euo pipefail
export LC_ALL=C

mode="${1:?usage: probe-auth.sh expect-authenticated|expect-unauthenticated}"
limit="${PROBE_TIMEOUT:-60}"
err="$(mktemp)"
trap 'rm -f "$err"' EXIT

rc=0
timeout "$limit" diagrid project list --output json --no-color=true < /dev/null > /dev/null 2> "$err" || rc=$?

case "$mode" in
  expect-authenticated)
    if [ "$rc" -eq 0 ]; then
      echo "Probe: authenticated read succeeded (credential works)."
      exit 0
    fi
    echo "::error::positive control failed: the authenticated read exited $rc, so a later 'logged out' result would prove nothing."
    exit 1
    ;;
  expect-unauthenticated)
    if [ "$rc" -eq 0 ]; then
      echo "::error::still authenticated: the read succeeded."
      exit 1
    fi
    # A timeout is the EXPECTED shape of "logged out" for this CLI, but only with the re-authentication banner
    # to say so. With no usable credential, authenticateCommand falls through to the browser/device-code OIDC
    # login: it prints "refreshing login session..." to stderr and then blocks waiting for a human, so the read
    # never returns rather than failing. A bare timeout (network stall, hung DNS) still proves nothing.
    if [ "$rc" -eq 124 ]; then
      if grep -aEiq 'refreshing login session|logging in to|device code|authorize' "$err"; then
        echo "Probe: the read blocked on a fresh login attempt (exit $rc) after announcing re-authentication: logged out."
        exit 0
      fi
      echo "::error::the probe timed out after ${limit}s with no sign of a login attempt: not proof of anything. stderr follows."
      cat "$err"
      exit 1
    fi
    # Matched against stderr only; kept in a variable (no early-exit pipelines under pipefail).
    sig=$(grep -aEi 'missing organization id|not logged in|log ?in|unauthenticated|unauthorized|device code|authenticat' "$err" || true)
    if [ -n "$sig" ]; then
      echo "Probe: read failed with an unauthenticated signature (exit $rc): logged out."
      exit 0
    fi
    echo "::error::the read failed (exit $rc) but not with an unauthenticated signature; refusing to read that as 'logged out'. stderr follows."
    cat "$err"
    exit 1
    ;;
  *)
    echo "unknown mode: $mode" >&2
    exit 2
    ;;
esac
