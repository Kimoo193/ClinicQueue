# Backup & restore drill (SC-015 / T083)

## Policy

| Tier | Retention | Schedule |
|------|-----------|----------|
| Daily | 7 | Every day 02:00 UTC |
| Weekly | 4 | Sundays (promoted from daily) |
| Monthly | 6 | 1st of month (promoted from daily) |

- Backups are **encrypted at rest** (GPG AES-256 symmetric).
- Encrypted artifacts are synced **off-site** via `CLINICQUEUE_BACKUP_OFFSITE` (rsync target).
- Production restore is never automatic — restore drills use a throwaway database.

## Prerequisites

```bash
# Passphrase file (never commit)
echo 'a-long-random-passphrase' > /secure/backup-passphrase
chmod 600 /secure/backup-passphrase

export POSTGRES_PASSWORD=…
export CLINICQUEUE_BACKUP_PASSPHRASE_FILE=/secure/backup-passphrase
export CLINICQUEUE_BACKUP_DIR=/var/backups/clinicqueue
export CLINICQUEUE_BACKUP_OFFSITE=backup-host:/offsite/clinicqueue
```

## Daily backup

```bash
chmod +x docker/backup.sh docker/restore-drill.sh
./docker/backup.sh
```

Cron example (host or dedicated backup container):

```cron
0 2 * * * CLINICQUEUE_BACKUP_PASSPHRASE_FILE=/secure/backup-passphrase \
  POSTGRES_PASSWORD=… POSTGRES_HOST=127.0.0.1 \
  /opt/clinicqueue/docker/backup.sh >> /var/log/clinicqueue-backup.log 2>&1
```

## Verified restore drill

```bash
export CLINICQUEUE_RESTORE_CONFIRM=YES
export CLINICQUEUE_RESTORE_DB=clinicqueue_restore_drill
./docker/restore-drill.sh /var/backups/clinicqueue/daily/backup-YYYYMMDD.sql.gpg
```

Success criteria: script prints `Restore drill VERIFIED` and `clinics` /
`appointments` / `visits` counts without error.

## Compose helper (optional)

```bash
docker compose -f docker/docker-compose.yml -f docker/docker-compose.backup.yml run --rm backup
```

See `docker-compose.backup.yml` for the one-shot backup service definition.
