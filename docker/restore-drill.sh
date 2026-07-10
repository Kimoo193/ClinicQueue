#!/usr/bin/env bash
# Restore drill: decrypt a backup and restore into a throwaway database (SC-015 / T083).
# Never restores over production without explicit CLINICQUEUE_RESTORE_CONFIRM=YES.
set -euo pipefail

BACKUP_FILE="${1:-}"
PASSPHRASE_FILE="${CLINICQUEUE_BACKUP_PASSPHRASE_FILE:-}"
RESTORE_DB="${CLINICQUEUE_RESTORE_DB:-clinicqueue_restore_drill}"

POSTGRES_HOST="${POSTGRES_HOST:-postgres}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
POSTGRES_USER="${POSTGRES_USER:-clinicqueue}"

if [[ -z "${BACKUP_FILE}" || ! -f "${BACKUP_FILE}" ]]; then
  echo "Usage: $0 <backup-YYYYMMDD.sql.gpg>" >&2
  exit 1
fi
if [[ -z "${PASSPHRASE_FILE}" || ! -f "${PASSPHRASE_FILE}" ]]; then
  echo "CLINICQUEUE_BACKUP_PASSPHRASE_FILE must point to a passphrase file" >&2
  exit 1
fi
if [[ -z "${POSTGRES_PASSWORD:-}" ]]; then
  echo "POSTGRES_PASSWORD is required" >&2
  exit 1
fi
if [[ "${CLINICQUEUE_RESTORE_CONFIRM:-}" != "YES" ]]; then
  echo "Refusing restore: set CLINICQUEUE_RESTORE_CONFIRM=YES to proceed into '${RESTORE_DB}'" >&2
  exit 1
fi
# RESTORE_DB is interpolated into raw SQL below (DROP/CREATE DATABASE); restrict it to a
# plain identifier so a malformed or malicious value can't inject additional statements.
if [[ ! "${RESTORE_DB}" =~ ^[a-zA-Z_][a-zA-Z0-9_]*$ ]]; then
  echo "CLINICQUEUE_RESTORE_DB must be a plain identifier, got '${RESTORE_DB}'" >&2
  exit 1
fi

tmp="$(mktemp)"
trap 'rm -f "${tmp}"' EXIT

echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Decrypting ${BACKUP_FILE}…"
gpg --batch --yes --decrypt --passphrase-file "${PASSPHRASE_FILE}" \
  -o "${tmp}" "${BACKUP_FILE}"

echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Recreating database ${RESTORE_DB}…"
PGPASSWORD="${POSTGRES_PASSWORD}" psql \
  -h "${POSTGRES_HOST}" -p "${POSTGRES_PORT}" -U "${POSTGRES_USER}" -d postgres \
  -v ON_ERROR_STOP=1 \
  -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${RESTORE_DB}' AND pid <> pg_backend_pid();" \
  -c "DROP DATABASE IF EXISTS ${RESTORE_DB};" \
  -c "CREATE DATABASE ${RESTORE_DB};"

echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Restoring…"
PGPASSWORD="${POSTGRES_PASSWORD}" psql \
  -h "${POSTGRES_HOST}" -p "${POSTGRES_PORT}" -U "${POSTGRES_USER}" -d "${RESTORE_DB}" \
  -v ON_ERROR_STOP=1 -f "${tmp}" >/dev/null

# Minimal verification: core tables exist and are queryable
PGPASSWORD="${POSTGRES_PASSWORD}" psql \
  -h "${POSTGRES_HOST}" -p "${POSTGRES_PORT}" -U "${POSTGRES_USER}" -d "${RESTORE_DB}" \
  -v ON_ERROR_STOP=1 \
  -c "SELECT COUNT(*) AS clinics FROM clinics;" \
  -c "SELECT COUNT(*) AS appointments FROM appointments;" \
  -c "SELECT COUNT(*) AS visits FROM visits;"

echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Restore drill VERIFIED against ${RESTORE_DB}"
