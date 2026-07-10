# Uptime & health monitoring (SC-010 / T081a)

## Endpoints

| Probe | Path | Purpose |
|-------|------|---------|
| Liveness | `GET /health` | Process is up |
| Readiness | `GET /health/ready` | PostgreSQL + Redis reachable |

Target: **≥99.5% uptime** during each clinic's operating hours.

## Enable the monitor sidecar

```bash
cd docker
cp .env.example .env   # fill secrets
# optional alert webhook:
# CLINICQUEUE_ALERT_WEBHOOK=https://hooks.slack.com/...

docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d
```

`health-monitor` polls every 30s and posts to `CLINICQUEUE_ALERT_WEBHOOK` after
3 consecutive failures.

## Compose healthchecks

The `api` service includes a Docker healthcheck against `/health`. Combine with
an external uptime provider (UptimeRobot, Better Stack, etc.) pointed at the
public HTTPS `/health` and `/health/ready` paths for the 99.5% SLO.
