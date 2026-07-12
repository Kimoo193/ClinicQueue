#!/usr/bin/env bash
# Run ClinicQueue k6 load tests against a live API.
#
# Prerequisites:
#   - API listening (default http://127.0.0.1:8080): CLINICQUEUE_RELAX_RATE_LIMIT=1 ./scripts/run-backend.sh
#   - Demo seed: ./scripts/seed-demo.sh  (accounts *@clinic.test / Test@1234)
#   - k6 CLI, or Docker (grafana/k6) as fallback
#
# Usage:
#   ./scripts/run-load-test.sh              # smoke (default)
#   ./scripts/run-load-test.sh smoke|load|stress
#   BASE_URL=http://127.0.0.1:8080 PROFILE=load ./scripts/run-load-test.sh
#   VUS=10 DURATION=30s ./scripts/run-load-test.sh   # single mixed scenario
#
# Extra args after the profile are passed to k6 (e.g. --out json=loadtests/results/out.json).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

PROFILE="${PROFILE:-smoke}"
if [[ $# -gt 0 ]]; then
  case "$1" in
    smoke|load|stress)
      PROFILE="$1"
      shift
      ;;
  esac
fi

export PROFILE
export BASE_URL="${BASE_URL:-http://127.0.0.1:8080}"
export CLINIC_SLUG="${CLINIC_SLUG:-ghada}"
export LOADTEST_PASSWORD="${LOADTEST_PASSWORD:-Test@1234}"
export RECEPTION_EMAIL="${RECEPTION_EMAIL:-reception@clinic.test}"
export PATIENT_EMAILS="${PATIENT_EMAILS:-patient1@clinic.test,patient2@clinic.test,patient3@clinic.test,patient4@clinic.test,urgent@clinic.test}"
# Optional overrides consumed by loadtests/lib/config.js
export VUS="${VUS:-}"
export DURATION="${DURATION:-}"

K6_IMAGE="${K6_IMAGE:-grafana/k6:0.54.0}"
SCRIPT="loadtests/main.js"
RESULTS_DIR="${ROOT}/loadtests/results"
mkdir -p "${RESULTS_DIR}"

echo "==> ClinicQueue load test"
echo "    PROFILE=${PROFILE}"
echo "    BASE_URL=${BASE_URL}"
echo "    CLINIC_SLUG=${CLINIC_SLUG}"
if [[ -n "${VUS}" && -n "${DURATION}" ]]; then
  echo "    VUS=${VUS} DURATION=${DURATION} (mixed override)"
fi
echo

if ! curl -sf --connect-timeout 2 "${BASE_URL}/health" >/dev/null 2>&1; then
  echo "ERROR: API not healthy at ${BASE_URL}/health" >&2
  echo "Start it with:" >&2
  echo "  CLINICQUEUE_RELAX_RATE_LIMIT=1 ./scripts/run-backend.sh" >&2
  echo "Then seed:" >&2
  echo "  ./scripts/seed-demo.sh" >&2
  exit 1
fi

# Warn if rate-limit relaxation may be missing (probe login budget lightly).
# We cannot read the API process env; document the requirement loudly.
echo "NOTE: Ensure the API was started with CLINICQUEUE_RELAX_RATE_LIMIT=1"
echo "      (login/sensitive limits stay tight otherwise; do not change production defaults)."
echo

K6_ENV=(
  -e "BASE_URL=${BASE_URL}"
  -e "PROFILE=${PROFILE}"
  -e "CLINIC_SLUG=${CLINIC_SLUG}"
  -e "LOADTEST_PASSWORD=${LOADTEST_PASSWORD}"
  -e "RECEPTION_EMAIL=${RECEPTION_EMAIL}"
  -e "PATIENT_EMAILS=${PATIENT_EMAILS}"
)
if [[ -n "${VUS}" ]]; then
  K6_ENV+=(-e "VUS=${VUS}")
fi
if [[ -n "${DURATION}" ]]; then
  K6_ENV+=(-e "DURATION=${DURATION}")
fi

run_k6() {
  if command -v k6 >/dev/null 2>&1; then
    echo "==> Using local k6 ($(k6 version 2>/dev/null | head -1))"
    k6 run "${K6_ENV[@]}" "${SCRIPT}" "$@"
    return
  fi

  if command -v docker >/dev/null 2>&1; then
    echo "==> k6 CLI not found; using Docker image ${K6_IMAGE}"
    # Host networking so 127.0.0.1:8080 inside the container reaches the host API.
    # On Linux --network=host works; on Docker Desktop, override BASE_URL to host.docker.internal.
    local docker_base="${BASE_URL}"
    local net_args=(--network=host)
    if [[ "$(uname -s)" == "Darwin" ]]; then
      net_args=()
      if [[ "${BASE_URL}" == *"127.0.0.1"* ]] || [[ "${BASE_URL}" == *"localhost"* ]]; then
        docker_base="${BASE_URL//127.0.0.1/host.docker.internal}"
        docker_base="${docker_base//localhost/host.docker.internal}"
        echo "    Darwin: rewriting BASE_URL → ${docker_base}"
      fi
    fi

    local docker_env=(
      -e "BASE_URL=${docker_base}"
      -e "PROFILE=${PROFILE}"
      -e "CLINIC_SLUG=${CLINIC_SLUG}"
      -e "LOADTEST_PASSWORD=${LOADTEST_PASSWORD}"
      -e "RECEPTION_EMAIL=${RECEPTION_EMAIL}"
      -e "PATIENT_EMAILS=${PATIENT_EMAILS}"
    )
    if [[ -n "${VUS}" ]]; then
      docker_env+=(-e "VUS=${VUS}")
    fi
    if [[ -n "${DURATION}" ]]; then
      docker_env+=(-e "DURATION=${DURATION}")
    fi

    docker run --rm \
      "${net_args[@]}" \
      -v "${ROOT}/loadtests:/scripts:ro" \
      "${docker_env[@]}" \
      "${K6_IMAGE}" \
      run /scripts/main.js "$@"
    return
  fi

  echo "ERROR: neither k6 nor docker found. Install k6 (https://k6.io/docs/get-started/installation/) or Docker." >&2
  exit 1
}

run_k6 "$@"
