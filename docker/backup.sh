#!/usr/bin/env bash
# Encrypted daily PostgreSQL backup with 7/4/6 retention (SC-015 / T083).
# Produces: backup-YYYYMMDD.sql.gpg  (AES-256 via gpg symmetric)
set -euo pipefail

BACKUP_DIR="${CLINICQUEUE_BACKUP_DIR:-./backups}"
RETENTION_DAILY="${CLINICQUEUE_BACKUP_DAILY:-7}"
RETENTION_WEEKLY="${CLINICQUEUE_BACKUP_WEEKLY:-4}"
RETENTION_MONTHLY="${CLINICQUEUE_BACKUP_MONTHLY:-6}"
PASSPHRASE_FILE="${CLINICQUEUE_BACKUP_PASSPHRASE_FILE:-}"
OFFSITE_RSYNC="${CLINICQUEUE_BACKUP_OFFSITE:-}"

POSTGRES_HOST="${POSTGRES_HOST:-postgres}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
POSTGRES_DB="${POSTGRES_DB:-clinicqueue}"
POSTGRES_USER="${POSTGRES_USER:-clinicqueue}"

if [[ -z "${POSTGRES_PASSWORD:-}" ]]; then
  echo "POSTGRES_PASSWORD is required" >&2
  exit 1
fi

if [[ -z "${PASSPHRASE_FILE}" || ! -f "${PASSPHRASE_FILE}" ]]; then
  echo "CLINICQUEUE_BACKUP_PASSPHRASE_FILE must point to a passphrase file" >&2
  exit 1
fi

mkdir -p "${BACKUP_DIR}/daily" "${BACKUP_DIR}/weekly" "${BACKUP_DIR}/monthly"
stamp="$(date -u +%Y%m%d)"
raw="${BACKUP_DIR}/daily/backup-${stamp}.sql"
enc="${raw}.gpg"

echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Dumping ${POSTGRES_DB}…"
PGPASSWORD="${POSTGRES_PASSWORD}" pg_dump \
  -h "${POSTGRES_HOST}" -p "${POSTGRES_PORT}" -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" \
  --format=plain --no-owner --no-acl > "${raw}"

echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Encrypting…"
gpg --batch --yes --symmetric --cipher-algo AES256 \
  --passphrase-file "${PASSPHRASE_FILE}" \
  -o "${enc}" "${raw}"
rm -f "${raw}"

# Promote Sunday dumps to weekly; 1st-of-month to monthly
dow="$(date -u +%u)"   # 7 = Sunday
dom="$(date -u +%d)"
if [[ "${dow}" == "7" ]]; then
  cp -f "${enc}" "${BACKUP_DIR}/weekly/backup-${stamp}.sql.gpg"
fi
if [[ "${dom}" == "01" ]]; then
  cp -f "${enc}" "${BACKUP_DIR}/monthly/backup-${stamp}.sql.gpg"
fi

prune() {
  local dir="$1"
  local keep="$2"
  # shellcheck disable=SC2012
  ls -1t "${dir}"/backup-*.sql.gpg 2>/dev/null | tail -n +"$((keep + 1))" | xargs -r rm -f
}

prune "${BACKUP_DIR}/daily" "${RETENTION_DAILY}"
prune "${BACKUP_DIR}/weekly" "${RETENTION_WEEKLY}"
prune "${BACKUP_DIR}/monthly" "${RETENTION_MONTHLY}"

if [[ -n "${OFFSITE_RSYNC}" ]]; then
  echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Syncing off-site → ${OFFSITE_RSYNC}"
  rsync -az --delete "${BACKUP_DIR}/" "${OFFSITE_RSYNC}/"
fi

echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Backup complete: ${enc}"
