#!/usr/bin/env bash
# Retries the live USAC xUnit filter for the nightly workflow only.
# Unreachable USAC exits 0 so the schedule stays green. An assertion or build
# failure exits 1 even when a later probe cannot reach USAC.
set -euo pipefail

summary_file="${GITHUB_STEP_SUMMARY:-}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/tests/ERateLookup.Tests/ERateLookup.Tests.csproj"
form470_probe='https://opendata.usac.org/resource/jt8s-3q52.json?$limit=1'
form471_probe='https://opendata.usac.org/resource/9s6i-myen.json?$limit=1'
# Quoted simple filter from UsacSources.Record. Checked live on 2026-10-19: HTTP 200, 40 rows, all application_number 270000594.
quoted_source_link='https://opendata.usac.org/resource/jt8s-3q52.json?application_number=%27270000594%27'

write_summary() {
  local outcome="$1"
  local attempts="$2"
  local line="$outcome (attempts: $attempts)"
  if [[ -n "$summary_file" ]]; then
    printf '%s\n' "$line" >"$summary_file"
  else
    printf '%s\n' "$line"
  fi
}

# 0 when the log shows a test assertion or a compile/build failure.
log_is_genuine_failure() {
  local log="$1"
  [[ -f "$log" ]] || return 1
  grep -E -q 'Assert\.|Xunit\.Sdk|error CS[0-9]|error NETSDK|Build FAILED' "$log"
}

# 0 when this endpoint answers. Network errors, timeouts, HTTP 408, 429, and 5xx are unreachable.
endpoint_reachable() {
  local url="$1"
  local http_code=""
  local curl_status=0
  set +e
  http_code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 20 --retry 0 "$url")"
  curl_status=$?
  set -e
  if [[ "$curl_status" -ne 0 || -z "$http_code" || "$http_code" == "000" ]]; then
    return 1
  fi
  case "$http_code" in
    408 | 429 | 5*) return 1 ;;
    2* | 3* | 4*) return 0 ;;
    *) return 1 ;;
  esac
}

usac_unreachable() {
  endpoint_reachable "$form470_probe" && endpoint_reachable "$form471_probe" && return 1
  return 0
}

run_live_tests() {
  local log="$1"
  local status=0
  set +e
  dotnet test "$project" -c Release --filter Category=Live >"$log" 2>&1
  status=$?
  set -e
  cat "$log"
  return "$status"
}

# 0 when the quoted source link returns rows for application 270000594.
# 1 when USAC answered but the quoted filter did not return that application.
# 2 when the endpoint is unreachable.
quoted_source_link_status() {
  local body http_code curl_status
  body="$(mktemp)"
  set +e
  http_code="$(curl -sS -o "$body" -w '%{http_code}' --max-time 20 --retry 0 "$quoted_source_link")"
  curl_status=$?
  set -e
  if [[ "$curl_status" -ne 0 || -z "$http_code" || "$http_code" == "000" ]]; then
    rm -f "$body"
    return 2
  fi
  case "$http_code" in
    408 | 429 | 5*)
      rm -f "$body"
      return 2
      ;;
    200) ;;
    *)
      rm -f "$body"
      return 1
      ;;
  esac
  if grep -q -e '"application_number":"270000594"' -e '"application_number": "270000594"' "$body"; then
    rm -f "$body"
    return 0
  fi
  rm -f "$body"
  return 1
}

nightly_live_usac() {
  local log_dir
  log_dir="$(mktemp -d)"
  local attempts=0
  local delay
  for delay in 0 30 60; do
    if [[ "$delay" -gt 0 ]]; then
      sleep "$delay"
    fi
    attempts=$((attempts + 1))
    local log="$log_dir/attempt-$attempts.log"
    if run_live_tests "$log"; then
      local quoted=0
      set +e
      quoted_source_link_status
      quoted=$?
      set -e
      if [[ "$quoted" -eq 0 ]]; then
        if [[ "$attempts" -eq 1 ]]; then
          write_summary "passed" "$attempts"
        else
          write_summary "passed after retry" "$attempts"
        fi
        return 0
      fi
      if [[ "$quoted" -eq 1 ]]; then
        echo "quoted source link did not return rows for application 270000594" >&2
        write_summary "failed" "$attempts"
        return 1
      fi
      echo "quoted source link was unreachable" >&2
    fi
    if log_is_genuine_failure "$log"; then
      write_summary "failed" "$attempts"
      return 1
    fi
  done

  if usac_unreachable; then
    write_summary "skipped due to USAC unreachable" "$attempts"
    return 0
  fi
  write_summary "failed" "$attempts"
  return 1
}

if [[ "${1:-}" == "--self-check" ]]; then
  tmp="$(mktemp)"
  printf 'Assert.True() Failure\n' >"$tmp"
  log_is_genuine_failure "$tmp"
  printf 'System.Net.Http.HttpRequestException\n' >"$tmp"
  if log_is_genuine_failure "$tmp"; then
    echo "transport error was classified as an assertion" >&2
    exit 1
  fi
  printf 'Build FAILED.\n' >"$tmp"
  log_is_genuine_failure "$tmp"
  rm -f "$tmp"
  echo "nightly classifier self-check passed"
  exit 0
fi

cd "$root"
export ERATE_LIVE_USAC=1
nightly_live_usac
