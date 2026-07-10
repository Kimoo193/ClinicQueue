#!/usr/bin/env bash
# Maps quickstart.md scenarios 1–7 to automated integration tests (T086).
# Full Docker Compose E2E can be layered on later; this gate proves the same
# behaviors via Testcontainers-backed release tests.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

echo "=== ClinicQueue quickstart validation (T086) ==="
echo "Running automated equivalents of quickstart scenarios 1–7…"

declare -A SCENARIOS=(
  ["1-core-live-queue"]="FullyQualifiedName~BookingTests|FullyQualifiedName~QueueConcurrencyTests"
  ["2-concurrency"]="FullyQualifiedName~QueueConcurrencyTests"
  ["3-pause-resume"]="FullyQualifiedName~PauseResumeTests"
  ["4-medical-least-privilege"]="FullyQualifiedName~MedicalAccessTests"
  ["5-tenant-isolation"]="FullyQualifiedName~TenantIsolationTests"
  ["6-day-close-carry-over"]="FullyQualifiedName~CarryOverTests"
  ["7-booking-subscription-guards"]="FullyQualifiedName~SubscriptionTests|FullyQualifiedName~BookingTests"
)

FAILED=0
for name in "${!SCENARIOS[@]}"; do
  filter="${SCENARIOS[$name]}"
  echo ""
  echo "--- Scenario ${name} ---"
  if dotnet test tests/ClinicQueue.IntegrationTests/ClinicQueue.IntegrationTests.csproj \
      --filter "${filter}" \
      --no-restore \
      -v q; then
    echo "PASS: ${name}"
  else
    echo "FAIL: ${name}" >&2
    FAILED=1
  fi
done

echo ""
echo "--- Phase 13 polish gates (isolation / security / openapi / load subset) ---"
dotnet test tests/ClinicQueue.IntegrationTests/ClinicQueue.IntegrationTests.csproj \
  --filter "FullyQualifiedName~TenantIsolationTests|FullyQualifiedName~SecurityPassTests|FullyQualifiedName~OpenApiDocumentTests|FullyQualifiedName~LoadTests.Status_read" \
  --no-restore -v q || FAILED=1

dotnet test tests/ClinicQueue.UnitTests/ClinicQueue.UnitTests.csproj \
  --filter "FullyQualifiedName~LocaleParityTests" \
  --no-restore -v q || FAILED=1

if [[ "${FAILED}" -ne 0 ]]; then
  echo "Quickstart validation FAILED" >&2
  exit 1
fi

echo "Quickstart validation PASSED (scenarios 1–7 via integration tests + Phase 13 gates)"
