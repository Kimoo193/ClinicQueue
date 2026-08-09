#!/usr/bin/env bash
# Logs in as every ClinicQueue demo account (see docs/demo-accounts.md) and makes one real,
# role-appropriate API call per account, so you can see what each login can actually do
# without opening the browser eleven times yourself.
#
# Prereqs:
#   ./scripts/run-backend.sh   # API up on :8080
#   ./scripts/seed-demo.sh     # creates the accounts below (password Test@1234 for all)
#
# Run:
#   ./scripts/demo-users.sh
#
# This does 12 logins in a few seconds. Production's /auth/login rate limit is 10/minute per IP
# (Middleware/RateLimiting.cs), so a strict run can 429 near the end. If that happens, restart the
# API with the same relaxed flag used for k6 (docs/running.md):
#   CLINICQUEUE_RELAX_RATE_LIMIT=1 ./scripts/run-backend.sh
#
# Point at Docker/nginx instead of the bare API:
#   BASE_URL=https://localhost ./scripts/demo-users.sh
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
PASSWORD="Test@1234"

# Stable seed ids (tools/ClinicQueue.Seeder/DemoSeeder.cs) — safe to hardcode, they never change.
GHADA_ID="11111111-1111-1111-1111-111111111111"
SMILE_ID="22222222-2222-2222-2222-222222222222"

CURL_OPTS=(-sS)
[[ "$BASE_URL" == https://* ]] && CURL_OPTS+=(-k) # docker/up.sh serves a self-signed cert

command -v jq >/dev/null 2>&1 || { echo "jq is required: https://jqlang.org" >&2; exit 1; }

if ! curl "${CURL_OPTS[@]}" -f --connect-timeout 2 "${BASE_URL}/health" >/dev/null 2>&1; then
  echo "API not reachable at ${BASE_URL}." >&2
  echo "Start it first: ./scripts/run-backend.sh   (and seed it: ./scripts/seed-demo.sh)" >&2
  exit 1
fi

JAR_DIR="$(mktemp -d)"
trap 'rm -rf "$JAR_DIR"' EXIT

hr() { printf '%s\n' "────────────────────────────────────────────────────────────────────"; }

# login EMAIL [CLINIC_ID] — authenticates, prints the resulting roles/clinic, returns a cookie
# jar path on stdout (last line) for use with `call`.
login() {
  local email="$1" clinic_id="${2:-}" jar payload body roles clinic status
  jar="$JAR_DIR/$(echo "${email}${clinic_id}" | tr -c 'a-zA-Z0-9' '_').jar"

  if [[ -n "$clinic_id" ]]; then
    payload="$(jq -n --arg e "$email" --arg p "$PASSWORD" --arg c "$clinic_id" \
      '{emailOrPhone:$e,password:$p,clinicId:$c}')"
  else
    payload="$(jq -n --arg e "$email" --arg p "$PASSWORD" '{emailOrPhone:$e,password:$p}')"
  fi

  body="$(curl "${CURL_OPTS[@]}" -c "$jar" -H 'Content-Type: application/json' \
    -w $'\n%{http_code}' -d "$payload" "${BASE_URL}/api/v1/auth/login")"
  status="${body##*$'\n'}"
  body="${body%$'\n'*}"

  # Diagnostics go to stderr — stdout is reserved for the jar path the caller captures via $(...).
  echo "  ▸ login as $email${clinic_id:+ (clinic=$clinic_id)} → HTTP $status" >&2
  if [[ "$status" == "200" ]]; then
    roles="$(jq -r '(.roles // []) | join(", ")' <<<"$body")"
    clinic="$(jq -r '.clinicId // "— (no active clinic)"' <<<"$body")"
    echo "    roles: [${roles:-none}]   clinicId: ${clinic}" >&2
  else
    echo "    $body" >&2
  fi
  printf '%s' "$jar"
}

# call JAR METHOD PATH LABEL — authenticated call using the cookie jar from login, pretty-prints
# the (trimmed) JSON response. jq runs into a plain variable capture (never a truncating pipe) so
# a long response can't SIGPIPE it under `set -o pipefail` and double-print the fallback.
call() {
  local jar="$1" method="$2" path="$3" label="$4" resp status body pretty total
  resp="$(curl "${CURL_OPTS[@]}" -b "$jar" -X "$method" -w $'\n%{http_code}' "${BASE_URL}${path}")"
  status="${resp##*$'\n'}"
  body="${resp%$'\n'*}"
  echo "  ▸ $label → HTTP $status"

  if pretty="$(jq -C . <<<"$body" 2>/dev/null)"; then
    total="$(wc -l <<<"$pretty")"
    sed -n '1,25p' <<<"$pretty" | sed 's/^/      /'
    if (( total > 25 )); then
      echo "      … ($((total - 25)) more lines, truncated)"
    fi
  else
    echo "      $body"
  fi
}

section() { echo; hr; echo "  $1"; hr; }

section "SuperAdmin — platform owner, no clinic membership"
jar="$(login admin@clinicqueue.app)"
call "$jar" GET /api/v1/admin/clinics "list every clinic on the platform"

section "ClinicOwner — Dr. Ghada Kamel Clinic"
jar="$(login ghada@clinic.test)"
call "$jar" GET "/api/v1/clinics/${GHADA_ID}/settings" "read clinic settings (urgent ratio, thresholds, timezone)"

section "Receptionist — Dr. Ghada Kamel Clinic"
jar="$(login reception@clinic.test)"
call "$jar" GET /api/v1/appointments/today "today's live queue (temp/real numbers, status, urgency)"
today_json="$(curl "${CURL_OPTS[@]}" -b "$jar" "${BASE_URL}/api/v1/appointments/today")"
inside_appt_id="$(jq -r '[.[] | select(.status=="Inside")][0].appointmentId // empty' <<<"$today_json")"

section "Doctor — Dr. Ghada Kamel Clinic"
jar="$(login doctor@clinic.test)"
if [[ -n "$inside_appt_id" ]]; then
  call "$jar" GET "/api/v1/appointments/${inside_appt_id}/consultation" "open the consultation screen for the patient currently Inside"
else
  echo "  ▸ no patient is Inside right now — re-run ./scripts/seed-demo.sh to reset today's queue"
fi

section "Patient — patient1@clinic.test (7 ahead → WAIT AT HOME)"
jar="$(login patient1@clinic.test)"
call "$jar" GET /api/v1/patients/me/appointments "my appointments (status, count-ahead, instruction)"

section "Patient — patient2@clinic.test (2 ahead → HEAD TO CLINIC)"
jar="$(login patient2@clinic.test)"
call "$jar" GET /api/v1/patients/me/appointments "my appointments (status, count-ahead, instruction)"

section "Patient — patient3@clinic.test (currently Inside with the doctor)"
jar="$(login patient3@clinic.test)"
call "$jar" GET /api/v1/patients/me/appointments "my appointments (status, count-ahead, instruction)"

section "Patient — patient4@clinic.test (booked, not arrived — deposit on file)"
jar="$(login patient4@clinic.test)"
call "$jar" GET /api/v1/patients/me/appointments "my appointments (status, count-ahead, instruction)"
echo "  ▸ note: this returns [] — the booking was made ON THEIR BEHALF by reception (deposit"
echo "    booking), so it's owned by BookedByUserId=reception, not patient4. See it in the"
echo "    Receptionist section above (temporaryNumber 12, status Booked)."

section "Patient — urgent@clinic.test (Urgent type, confirmed present)"
jar="$(login urgent@clinic.test)"
call "$jar" GET /api/v1/patients/me/appointments "my appointments (status, count-ahead, instruction)"

section "multi@clinic.test — the multi-clinic switcher (Receptionist at Ghada + Patient at Smile)"
jar="$(login multi@clinic.test "$GHADA_ID")"
call "$jar" GET /api/v1/appointments/today "as Receptionist at Ghada — today's queue"
jar="$(login multi@clinic.test "$SMILE_ID")"
call "$jar" GET /api/v1/patients/me/appointments "as Patient at Smile — my appointments"

section "newbie@clinic.test — registered, no clinic membership yet"
jar="$(login newbie@clinic.test)"
echo "  ▸ no clinic-scoped API is available yet — the web app sends this account to /find-clinic."
call "$jar" GET /api/v1/patients/me/clinics "attempt a Patient-only call (expect HTTP 403 — proves role gating works)"

echo
hr
echo "  Done. Full account table + how-to-run: docs/demo-accounts.md"
hr
