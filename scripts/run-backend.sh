#!/usr/bin/env bash
# Terminal 1 — backend (Postgres/Redis via Docker, API on :8080)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT/docker"

if [[ ! -f .env ]]; then
  cp .env.example .env
fi

COMPOSE=(docker compose -f docker-compose.yml -f docker-compose.dev-infra.yml)
POSTGRES_PASSWORD="$(grep -E '^POSTGRES_PASSWORD=' .env | cut -d= -f2- | tr -d '\r')"
# Host port from docker-compose.dev-infra.yml (not 5432 — that is often a system Postgres)
PG_HOST_PORT=5433

echo "Starting Postgres + Redis…"
"${COMPOSE[@]}" up -d postgres redis

echo "Waiting for Postgres…"
until "${COMPOSE[@]}" exec -T postgres pg_isready -U clinicqueue -d clinicqueue >/dev/null 2>&1; do
  sleep 1
done

# Password is set only on first volume init. If .env changed, auth fails until volume reset.
if ! "${COMPOSE[@]}" exec -T -e PGPASSWORD="$POSTGRES_PASSWORD" postgres \
  psql -U clinicqueue -d clinicqueue -c 'SELECT 1' >/dev/null 2>&1; then
  echo "Postgres password does not match docker/.env (volume was created with an older password)."
  echo "Resetting local Postgres/Redis volumes…"
  "${COMPOSE[@]}" down -v
  "${COMPOSE[@]}" up -d postgres redis
  until "${COMPOSE[@]}" exec -T postgres pg_isready -U clinicqueue -d clinicqueue >/dev/null 2>&1; do
    sleep 1
  done
fi

export ConnectionStrings__Default="Host=localhost;Port=${PG_HOST_PORT};Database=clinicqueue;Username=clinicqueue;Password=${POSTGRES_PASSWORD}"
export ConnectionStrings__Redis="localhost:6379"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:8080

cd "$ROOT/src/ClinicQueue.Api"
echo "API listening on http://localhost:8080 (Postgres on localhost:${PG_HOST_PORT})"
dotnet run
