# Architecture

Source of truth for stack versions: project files (`*.csproj` → `net10.0`, `docker-compose.yml` images, `web/package.json`). Spec narrative lives in `specs/001-clinic-queue-mvp/plan.md` and contributor rules in `docs/CLAUDE.md`.

## Shape

Modular monolith, Clean Architecture dependency direction:

```text
ClinicQueue.Api
    → ClinicQueue.Infrastructure
        → ClinicQueue.Application
            → ClinicQueue.Domain
```

Bounded contexts (folders under Domain/Application, not separate deployables): **Identity**, **Queue**, **Medical**, **Notifications**, **Reporting**, **Platform**.

Frontend: single React SPA under `web/` with feature folders (`patient-queue`, `patient-bookings`, `patient-medical`, `reception-console`, `reception-dayclose`, `doctor-console`, `owner-reports`, `admin-console`).

## Runtime topology (Docker)

```text
Browser
  → Nginx (TLS, static SPA, /api and /hubs reverse proxy)
      → web (static)
      → api (ASP.NET Core, Kestrel :8080 inside network)
  → hangfire-worker (same API image, Hangfire__ServerOnly)
Postgres 16  ·  Redis 7 (SignalR backplane + Hangfire as configured)
```

## Tenancy

Intended three-layer model (constitution / plan):

1. **JWT `clinic_id` claim** via `ITenantProvider` — do not trust body/query/header for tenant id.
2. **Execution-time filters** — `BaseRepository<T>` exists for clinic-scoped entities; many handlers currently query `IAppDbContext` with explicit `ClinicId` predicates (verify when adding new paths).
3. **PostgreSQL RLS** — migrations `ENABLE ROW LEVEL SECURITY` on clinic-scoped tables. Default compose uses table-owner role `clinicqueue`; owner bypasses RLS unless `FORCE ROW LEVEL SECURITY` and a non-owner app role are introduced. Treat app-layer filters + isolation tests as the active control until that ops change lands.

Isolation release gate: `tests/.../Isolation/TenantIsolationTests.cs`.

## Queue numbering

Real numbers are assigned on arrival confirmation (and related staff flows), not at booking. Implementation uses session row locking (`SELECT … FOR UPDATE`) and appointment `RowVersion` (see Application Queue handlers / constitution Principle II). Concurrency gate: `QueueConcurrencyTests`.

Urgent interleave defaults to clinic setting **2:1** (configurable). Patients see **count-ahead** and instruction `waitHome` / `headToClinic` vs a threshold (default 4) — not a time estimate.

## Notifications

State changes enqueue `OutboxNotification` rows in the same unit of work. Hangfire jobs dispatch channels (SignalR, Web Push when VAPID + subscriptions exist). Hub path: `/hubs/queue` (`Program.cs` → `MapHub<QueueHub>`).

## Medical least-privilege

Visit/prescription APIs re-resolve Doctor/ClinicOwner membership and audit reads. Receptionist must not receive diagnosis/notes content (see Medical endpoints + access tests).

## AuthN / AuthZ (web)

- Cookies: `cq_access`, `cq_refresh`, `cq_csrf` (`TokenService`)
- CSRF: double-submit `X-CSRF-Token` (`CsrfMiddleware`) on mutating methods
- Policies: role-based (`Policies.*` in Infrastructure Authorization); medical paths additionally check membership

## Further reading

- `specs/001-clinic-queue-mvp/plan.md` — algorithms and deployment decisions
- `.specify/memory/constitution.md` — governance principles
- `docker/BACKUP.md`, `docker/MONITORING.md` — ops overlays
