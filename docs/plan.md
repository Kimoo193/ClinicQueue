# plan.md — Clinic Queue SaaS

> **Technical Plan & Architecture** — how we build what `spec.md` describes.
> Web-first MVP · Modular Monolith · Multi-tenant from day one.

---

## 1. Architecture Overview

**Pattern:** Modular Monolith (NOT microservices). A single ASP.NET Core backend split into bounded modules, one PostgreSQL database with row-level tenant isolation, Redis for real-time + caching + background jobs, all containerized with Docker Compose behind Nginx on a single Hostinger VPS.

**Rationale:** At MVP-to-200-clinics scale, microservices add operational complexity with no benefit. Modules keep the code clean; the stateless design allows horizontal scaling later without re-architecture. Any module can be extracted into its own service if the platform outgrows the monolith.

```
                          ┌───────────────────┐
                          │   Super Admin Web  │
                          └─────────┬─────────┘
                                    │
┌──────────────┐          ┌─────────▼─────────┐          ┌──────────────┐
│  Patient Web  │─────────▶│   ASP.NET Core 9  │◀─────────│ Reception Web │
│  (responsive) │  HTTPS   │ Modular Monolith  │  HTTPS   │  + Doctor Web │
└──────────────┘          │       API         │          └──────────────┘
       ▲                  └────┬─────────┬────┘
       │ SignalR / WebPush     │         │
       └───────────────────────┘         │
                          ┌──────────────┼──────────────┐
                    ┌─────▼─────┐  ┌──────▼──────┐  ┌────▼─────┐
                    │PostgreSQL │  │    Redis    │  │  Nginx   │
                    │ 16 (+RLS) │  │ SignalR/    │  │ TLS/proxy│
                    └───────────┘  │ cache/jobs  │  └──────────┘
                                   └─────────────┘
```

---

## 2. Technology Stack

| Layer | Choice | Notes |
|---|---|---|
| Backend | ASP.NET Core 9 | Clean Architecture per module |
| Language | C# | |
| Database | PostgreSQL 16 | Row-Level Security for isolation |
| ORM | EF Core 9 | Global query filters + BaseRepository |
| Real-time | SignalR | Redis backplane from day one |
| Cache/Queue | Redis 7 | Backplane, cache, rate-limit counters |
| Background jobs | Hangfire | Outbox, auto-absent, reminders, backups |
| Web frontend | React 19 + Vite + TypeScript | Web-first; RTL via i18next |
| UI | Tailwind + shadcn/ui | Matches delivered UI kit |
| Data fetching | TanStack Query | |
| Web push | Service Worker + Web Push API (VAPID) | Tab-closed notifications |
| Auth | JWT (HttpOnly cookies) + refresh rotation | No OTP in MVP |
| Deployment | Docker Compose + Nginx | Hostinger VPS |
| CI/CD | GitHub Actions | Build, test, vuln scan, deploy |
| Mobile (Phase 2) | Flutter + FCM | Reuses same backend |

> Note: MVP frontend is **React web** (all three roles). Flutter is Phase 2 for the native mobile app. The earlier Flutter-Web plan is superseded by React for a faster, lighter web dashboard.

---

## 3. Solution Structure (Clean Architecture)

```
ClinicQueue.sln
├── src/
│   ├── ClinicQueue.Domain/            # Entities, value objects, domain events, enums
│   ├── ClinicQueue.Application/       # Use cases (CQRS handlers), DTOs, validators, interfaces
│   ├── ClinicQueue.Infrastructure/    # EF Core, repositories, Redis, Hangfire, WebPush, external
│   └── ClinicQueue.Api/               # Controllers/endpoints, SignalR hubs, middleware, DI
├── tests/
│   ├── ClinicQueue.UnitTests/
│   └── ClinicQueue.IntegrationTests/  # incl. tenant-isolation + queue-concurrency
├── web/                               # React app (patient + reception + doctor + admin)
├── docker/                            # Dockerfiles, compose, nginx conf
└── docs/                              # spec.md, plan.md, tasks.md, CLAUDE.md
```

### Modules (bounded contexts inside the monolith)
```
Identity      → auth, users, roles, clinics, subscriptions, tenancy
Queue         → sessions, appointments, numbering, urgent injection, pause/resume, carry-over
Medical       → patient profiles, visits, prescriptions
Notifications → outbox, dispatcher (SignalR/WebPush), templates
Reporting     → volume, absence, peak hours, revenue, ratings
Platform      → super-admin: clinics, subscriptions, platform analytics
```

---

## 4. Multi-Tenancy Implementation (3 Defense Layers)

**Layer 1 — Application (EF Core + BaseRepository)**
- `ITenantProvider` (Scoped) reads `clinic_id` from JWT claims on every request.
- Tenant filter applied at **execution time** in `BaseRepository<T>` via expression trees — NOT baked into the cached EF model (avoids the model-caching pin bug).

**Layer 2 — Token integrity**
- `clinic_id` comes ONLY from the signed JWT. Never from request body/query/header.

**Layer 3 — Database (PostgreSQL RLS)**
- `ALTER TABLE ... ENABLE ROW LEVEL SECURITY` + policy on `clinic_id = current_setting('app.current_clinic')`.
- Connection sets `app.current_clinic` per request. Final safety net even if app code has a bug.

**Verification:** Automated E2E isolation tests are a release gate (see tasks).

---

## 5. Critical Technical Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Duplicate queue numbers under concurrency | `SELECT ... FOR UPDATE` row lock on the session + `RowVersion` optimistic concurrency on appointments |
| EF Core model-caching pins first tenant | Tenant filter resolved at execution time in BaseRepository, not model build |
| Lost notifications | Outbox pattern: notification persisted in same transaction as the state change; Hangfire worker dispatches with retry |
| Cross-tenant leakage | 3-layer isolation + automated tests |
| Web push unreliable on iOS | SignalR covers tab-open; document iOS 16.4+ PWA requirement; SMS fallback deferred to Phase 2 |
| VPS single point of failure | Health checks + uptime monitor + daily off-site encrypted backups + tested restore |

---

## 6. Urgent Injection Algorithm (Deterministic)

```
State per QueueSession: normalSinceLastUrgent (counter), urgentRatio (default 2)

On confirming arrival of a patient:
  if type == Normal:
      assign next sequential position
      normalSinceLastUrgent += 1
  if type == Urgent:
      if normalSinceLastUrgent >= urgentRatio AND an urgent is waiting:
          insert this urgent immediately after the last-confirmed patient
          normalSinceLastUrgent = 0
      else:
          urgent waits until the ratio is met
  Numbers already assigned to confirmed patients never change.
  Only not-yet-confirmed (absent/at-home) patients are effectively behind.
```
All numbering happens inside a DB transaction with the session row locked.

---

## 7. Notification Pipeline (Outbox)

```
1. State change (booking/position/pause/...) writes an OutboxNotification row
   in the SAME transaction.
2. Hangfire worker polls Pending rows (every ~10s) and dispatches through the
   channel-agnostic INotificationDispatcher:
      - SignalR (if a live connection exists for the user)
      - Web Push (VAPID) if the user granted permission
      - [Phase 2] FCM for mobile
3. Failure → retry ×3 exponential backoff → mark Failed + alert.
```

---

## 8. Deployment (Docker Compose on VPS)

```
services: nginx (TLS, reverse proxy, first-layer rate limit)
          api (ASP.NET Core; secrets via env/Docker secrets; /health)
          web (React static build served by nginx)
          postgres (16-alpine; internal network only; encrypted disk)
          redis (7-alpine; password-protected; internal only)
          hangfire-worker (same image, worker profile)
VPS hardening: UFW (80/443/SSH only), SSH key-only, fail2ban.
Subdomain routing: nginx maps {clinic}.clinicqueue.app → same app, tenant resolved by host.
```

---

## 9. Build Order (Modules → see tasks.md)

```
M1 Foundation      → solution, Clean Arch, tenancy (3 layers), auth, audit, health
M2 Queue Core      → sessions, appointments, numbering, urgent injection, pause/resume,
                     carry-over, auto-absent, SignalR + Redis
M3 Notifications   → Outbox, Hangfire, dispatcher, Web Push, message rules
M4 Medical         → profiles, visits, prescriptions (text), access control
M5 Reports+Ratings → volume, absence, peak, revenue, ratings
M6 Super Admin     → clinics, subscriptions, platform analytics
M7 Web Frontend    → patient → reception → doctor → admin (React, RTL/i18n)
M8 Hardening       → isolation tests, load test, security pass, backup/restore drill
```

---

## 10. Definition of Done (per module)

- Unit + integration tests green (incl. tenant isolation where relevant).
- API documented (OpenAPI) and versioned under `/api/v1/`.
- Validation (FluentValidation) on every input DTO.
- No secrets in code; all config via env.
- Audit logging on sensitive actions.
- Reviewed against the security checklist in CLAUDE.md.
