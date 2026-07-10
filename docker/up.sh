#!/usr/bin/env bash
# One-command full stack: nginx + api + web + postgres + redis + hangfire.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT/docker"

if [[ ! -f .env ]]; then
  cp .env.example .env
  echo "Created docker/.env from .env.example — edit secrets if needed."
fi

if [[ ! -f certs/fullchain.pem || ! -f certs/privkey.pem ]]; then
  echo "Generating self-signed TLS certs for localhost…"
  openssl req -x509 -nodes -newkey rsa:2048 \
    -keyout certs/privkey.pem -out certs/fullchain.pem \
    -days 365 -subj "/CN=localhost"
fi

docker compose up -d --build

echo ""
echo "ClinicQueue is starting."
echo "  App:    https://localhost  (accept the self-signed cert warning)"
echo "  Health: https://localhost/api/v1/status  (via nginx → api)"
echo "  Logs:   docker compose -f $ROOT/docker/docker-compose.yml logs -f"
echo ""
echo "First-time data: POST /api/v1/clinics then login — see docs/running.md"
