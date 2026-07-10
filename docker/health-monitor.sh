#!/usr/bin/env bash
# Health / uptime monitor for ClinicQueue (SC-010 / T081a).
# Polls /health and /health/ready; exits non-zero after consecutive failures
# so a process supervisor or cron can alert (email/webhook).
set -euo pipefail

BASE_URL="${CLINICQUEUE_BASE_URL:-https://localhost}"
LIVENESS_PATH="${CLINICQUEUE_LIVENESS_PATH:-/health}"
READINESS_PATH="${CLINICQUEUE_READINESS_PATH:-/health/ready}"
INTERVAL_SEC="${CLINICQUEUE_HEALTH_INTERVAL_SEC:-30}"
FAIL_THRESHOLD="${CLINICQUEUE_HEALTH_FAIL_THRESHOLD:-3}"
ALERT_WEBHOOK="${CLINICQUEUE_ALERT_WEBHOOK:-}"
CURL_OPTS=(--silent --show-error --fail --max-time 5)
# Allow self-signed certs in local/staging drills
if [[ "${CLINICQUEUE_INSECURE_TLS:-0}" == "1" ]]; then
  CURL_OPTS+=(--insecure)
fi

consecutive_failures=0
target_uptime="99.5"

alert() {
  local message="$1"
  echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] ALERT: ${message}" >&2
  if [[ -n "${ALERT_WEBHOOK}" ]]; then
    curl --silent --show-error --max-time 10 \
      -H 'Content-Type: application/json' \
      -d "{\"text\":\"ClinicQueue health alert: ${message}\"}" \
      "${ALERT_WEBHOOK}" >/dev/null || true
  fi
}

check_once() {
  local url="$1"
  curl "${CURL_OPTS[@]}" "${url}" >/dev/null
}

echo "Monitoring ${BASE_URL} (liveness=${LIVENESS_PATH}, readiness=${READINESS_PATH})"
echo "Target uptime during operating hours: ≥${target_uptime}% (SC-010)"
echo "Fail threshold: ${FAIL_THRESHOLD} consecutive failures; interval ${INTERVAL_SEC}s"

while true; do
  if check_once "${BASE_URL}${LIVENESS_PATH}" \
    && check_once "${BASE_URL}${READINESS_PATH}"; then
    if (( consecutive_failures > 0 )); then
      echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Recovered after ${consecutive_failures} failure(s)"
    fi
    consecutive_failures=0
  else
    consecutive_failures=$((consecutive_failures + 1))
    echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Health check failed (${consecutive_failures}/${FAIL_THRESHOLD})" >&2
    if (( consecutive_failures >= FAIL_THRESHOLD )); then
      alert "liveness/readiness failed ${consecutive_failures} times against ${BASE_URL}"
      consecutive_failures=0
    fi
  fi
  sleep "${INTERVAL_SEC}"
done
