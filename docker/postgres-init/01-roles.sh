#!/bin/sh
# Provisions the two non-owner database roles on FIRST cluster init (fresh volume only).
#   clinicqueue_app       — NOSUPERUSER, NOBYPASSRLS: the application connection. Subject to RLS.
#   clinicqueue_bypassrls — NOSUPERUSER, BYPASSRLS  : SuperAdmin/platform + background jobs.
# The owner/superuser role stays $POSTGRES_USER (clinicqueue) and runs EF migrations.
# Passwords come from env (docker/.env) — never hard-code. Avoid single quotes in passwords.
set -e

: "${APP_DB_PASSWORD:?APP_DB_PASSWORD must be set for postgres role provisioning}"
: "${BYPASS_DB_PASSWORD:?BYPASS_DB_PASSWORD must be set for postgres role provisioning}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    DO \$\$
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'clinicqueue_app') THEN
            CREATE ROLE clinicqueue_app LOGIN PASSWORD '${APP_DB_PASSWORD}'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'clinicqueue_bypassrls') THEN
            CREATE ROLE clinicqueue_bypassrls LOGIN PASSWORD '${BYPASS_DB_PASSWORD}'
                NOSUPERUSER NOCREATEDB NOCREATEROLE BYPASSRLS;
        END IF;
    END
    \$\$;

    GRANT USAGE ON SCHEMA public TO clinicqueue_app, clinicqueue_bypassrls;

    -- Tables/sequences created LATER by the owner (EF migrations) are auto-granted DML.
    ALTER DEFAULT PRIVILEGES FOR ROLE ${POSTGRES_USER} IN SCHEMA public
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO clinicqueue_app, clinicqueue_bypassrls;
    ALTER DEFAULT PRIVILEGES FOR ROLE ${POSTGRES_USER} IN SCHEMA public
        GRANT USAGE, SELECT ON SEQUENCES TO clinicqueue_app, clinicqueue_bypassrls;
EOSQL
