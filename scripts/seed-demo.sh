#!/usr/bin/env bash
# Repeatable, idempotent demo seed (docs/running.md):  ./scripts/seed-demo.sh
#
# Creates two clinics (Dr. Ghada Kamel + Smile Dental), every role, a live queue that reaches
# every UI state, and ~60 days of history — then prints the account table and QR URLs.
#
# Role discipline: the seed runs through the SAME three PostgreSQL roles as production and NEVER
# writes data as the superuser. The owner connection is used only to (a) run EF migrations and
# (b) provision the non-owner roles + grants below. Everything the seeder writes goes through
# clinicqueue_app (RLS enforced) and clinicqueue_bypassrls (pre-auth writes).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT/docker"

if [[ ! -f .env ]]; then
  cp .env.example .env
  echo "Created docker/.env from .env.example — edit passwords if needed, then re-run."
fi

read_env() { grep -E "^$1=" .env | cut -d= -f2- | tr -d '\r'; }
POSTGRES_PASSWORD="$(read_env POSTGRES_PASSWORD)"
APP_DB_PASSWORD="$(read_env APP_DB_PASSWORD)"
BYPASS_DB_PASSWORD="$(read_env BYPASS_DB_PASSWORD)"
DB_NAME=clinicqueue
PG_HOST_PORT=5433 # from docker-compose.dev-infra.yml (avoids a system Postgres on 5432)

COMPOSE=(docker compose -f docker-compose.yml -f docker-compose.dev-infra.yml)

echo "• Ensuring Postgres is up…"
"${COMPOSE[@]}" up -d postgres >/dev/null
until "${COMPOSE[@]}" exec -T postgres pg_isready -U clinicqueue -d "$DB_NAME" >/dev/null 2>&1; do
  sleep 1
done

# Provision the non-owner roles + grants idempotently (owner connection). Needed because an older
# local volume may predate docker/postgres-init, or the roles may lack DML on already-created tables.
# This is SETUP, not seeding — no tenant data is written here. Passwords pass through psql -v
# variables (:'name' → safely single-quoted), never interpolated into the SQL text.
echo "• Provisioning app/bypass roles + grants (owner)…"
"${COMPOSE[@]}" exec -T -e PGPASSWORD="$POSTGRES_PASSWORD" postgres \
  psql -v ON_ERROR_STOP=1 -U clinicqueue -d "$DB_NAME" \
       -v app_pw="$APP_DB_PASSWORD" -v bypass_pw="$BYPASS_DB_PASSWORD" <<'SQL'
SELECT 'CREATE ROLE clinicqueue_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS'
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'clinicqueue_app') \gexec
SELECT 'CREATE ROLE clinicqueue_bypassrls LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE BYPASSRLS'
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'clinicqueue_bypassrls') \gexec

ALTER ROLE clinicqueue_app       LOGIN NOBYPASSRLS PASSWORD :'app_pw';
ALTER ROLE clinicqueue_bypassrls LOGIN BYPASSRLS   PASSWORD :'bypass_pw';

GRANT USAGE ON SCHEMA public TO clinicqueue_app, clinicqueue_bypassrls;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO clinicqueue_app, clinicqueue_bypassrls;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO clinicqueue_app, clinicqueue_bypassrls;
ALTER DEFAULT PRIVILEGES FOR ROLE clinicqueue IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO clinicqueue_app, clinicqueue_bypassrls;
ALTER DEFAULT PRIVILEGES FOR ROLE clinicqueue IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO clinicqueue_app, clinicqueue_bypassrls;
SQL

export ConnectionStrings__Migrations="Host=localhost;Port=${PG_HOST_PORT};Database=${DB_NAME};Username=clinicqueue;Password=${POSTGRES_PASSWORD}"
export ConnectionStrings__Privileged="Host=localhost;Port=${PG_HOST_PORT};Database=${DB_NAME};Username=clinicqueue_bypassrls;Password=${BYPASS_DB_PASSWORD}"
export ConnectionStrings__Default="Host=localhost;Port=${PG_HOST_PORT};Database=${DB_NAME};Username=clinicqueue_app;Password=${APP_DB_PASSWORD}"

cd "$ROOT"
echo "• Running ClinicQueue.Seeder…"
echo
dotnet run --project tools/ClinicQueue.Seeder/ClinicQueue.Seeder.csproj -c Release
