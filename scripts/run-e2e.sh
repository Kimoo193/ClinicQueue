#!/usr/bin/env bash
# Seed demo data, ensure API + Vite are up, run Playwright E2E (S1–S9).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

chmod +x "$ROOT/scripts/seed-demo.sh" "$ROOT/scripts/run-backend.sh" "$ROOT/scripts/run-frontend.sh" 2>/dev/null || true

echo "==> Seeding demo clinic…"
"$ROOT/scripts/seed-demo.sh"

# Start API if not already listening
if ! curl -sf http://127.0.0.1:8080/health >/dev/null 2>&1; then
  echo "==> Starting API (background)…"
  (
    cd "$ROOT"
    export CLINICQUEUE_RELAX_RATE_LIMIT=1
    nohup ./scripts/run-backend.sh > /tmp/clinicqueue-api-e2e.log 2>&1 &
    echo $! > /tmp/clinicqueue-api-e2e.pid
  )
  for i in $(seq 1 60); do
    if curl -sf http://127.0.0.1:8080/health >/dev/null 2>&1; then
      break
    fi
    sleep 2
  done
  if ! curl -sf http://127.0.0.1:8080/health >/dev/null 2>&1; then
    echo "API failed to become healthy. Log: /tmp/clinicqueue-api-e2e.log"
    tail -n 80 /tmp/clinicqueue-api-e2e.log || true
    exit 1
  fi
fi

if ! curl -sf http://127.0.0.1:5173 >/dev/null 2>&1; then
  echo "==> Starting Vite (background)…"
  (
    cd "$ROOT/web"
    nohup npm run dev -- --host 127.0.0.1 --port 5173 > /tmp/clinicqueue-vite-e2e.log 2>&1 &
    echo $! > /tmp/clinicqueue-vite-e2e.pid
  )
  for i in $(seq 1 30); do
    if curl -sf http://127.0.0.1:5173 >/dev/null 2>&1; then
      break
    fi
    sleep 1
  done
fi

echo "==> Running Playwright E2E…"
cd "$ROOT/web"
npm run test:e2e
