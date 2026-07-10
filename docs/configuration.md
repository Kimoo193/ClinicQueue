# Configuration

Keys below are read by the running API or Compose. Verify against `appsettings.json`, `appsettings.Development.json`, and `docker/docker-compose.yml` / `.env.example`.

## ASP.NET configuration

| Key | Purpose | Notes |
|-----|---------|--------|
| `ConnectionStrings:Default` | PostgreSQL | Required for API; empty in base `appsettings.json` |
| `ConnectionStrings:Redis` | Redis | If set, SignalR uses Redis backplane; if empty/missing, in-process SignalR |
| `Jwt:Issuer` | Token issuer | Default `ClinicQueue` |
| `Jwt:Audience` | Token audience | Default `ClinicQueue` |
| `Jwt:AccessTokenMinutes` | Access token lifetime | Default `15` |
| `Jwt:RefreshTokenDays` | Refresh lifetime | Default `30` |
| `Jwt:SigningKey` | HMAC signing key | Must be ≥32 characters at startup (fail-fast); empty in base appsettings |
| `Vapid:Subject` | Web Push subject | Default `mailto:ops@clinicqueue.app` |
| `Vapid:PublicKey` / `Vapid:PrivateKey` | Web Push | Empty until configured; send path logs if missing |
| `Hangfire:Enabled` | Register Hangfire | `false` in Development appsettings; `true` in Compose |
| `Hangfire:ServerOnly` | Worker-only process | Compose `hangfire-worker` sets `true` |
| `Pagination:DefaultPageSize` | List default | `20` |
| `Pagination:MaxPageSize` | List max | `100` |
| `Audit:RetentionDays` | Audit retention policy config | `730` (2 years) — ops must enforce storage |
| `Database:ApplyMigrationsOnStartup` | Dev auto-migrate | `false` by default; only applied when Development **and** true |
| `Cors:AllowedOrigins` | Browser origins | Empty array → deny cross-origin; Compose can set index `0` from env |
| `OpenApi:EnableUi` | Force OpenAPI UI | Production maps OpenAPI/Scalar only if Dev **or** this is `true` |

Environment variable form uses `__` nesting, e.g. `ConnectionStrings__Default`, `Jwt__SigningKey` (as in Compose).

## Docker Compose env (`docker/.env.example`)

| Variable | Used for |
|----------|----------|
| `POSTGRES_PASSWORD` | Postgres + API connection string |
| `REDIS_PASSWORD` | Redis `requirepass` + API Redis connection |
| `JWT_SIGNING_KEY` | `Jwt__SigningKey` |
| `VAPID_PUBLIC_KEY` / `VAPID_PRIVATE_KEY` | Web Push |
| `CORS_ALLOWED_ORIGIN` | Optional single origin (`Cors__AllowedOrigins__0`) |
| `CLINICQUEUE_ALERT_WEBHOOK` | Optional health-monitor alerts |
| `CLINICQUEUE_BACKUP_PASSPHRASE_FILE` / `CLINICQUEUE_BACKUP_OFFSITE` | Backup overlay |

Copy to `docker/.env` (gitignored patterns cover secrets). Never commit real passwords or signing keys.

## Development defaults

`appsettings.Development.json` currently includes local Postgres credentials and a fixed JWT signing key for convenience. Treat as **machine-local only**; prefer user-secrets or env overrides for shared machines.

## Frontend

- Vite proxy targets are hardcoded in `web/vite.config.ts` (`localhost:8080`).
- UI clinic id may be stored in browser localStorage by the shell (not an auth boundary — JWT `clinic_id` is).
- Optional `web/.env` is unused for API base URL when using the Vite proxy (same-origin `/api`).
