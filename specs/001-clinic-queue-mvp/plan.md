# Implementation Plan: Clinic Queue SaaS MVP (Web-First)

**Branch**: `001-clinic-queue-mvp` | **Date**: 2026-07-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-clinic-queue-mvp/spec.md`

**Source of truth**: This plan formalizes the existing `docs/plan.md` (architecture
already decided for this project) into Spec Kit format. It does not introduce new
technology choices; every decision below traces to `docs/plan.md` and is re-checked
against `.specify/memory/constitution.md` v1.0.0. Specifically preserved from
`docs/plan.md`: the Modular Monolith topology (§1), the technology stack (§2), the
three-layer tenancy model (§4), the deterministic urgent-injection algorithm (§6),
the Outbox notification pipeline (§7), and the Docker Compose deployment (§8).

**Reflects clarifications**: This revision incorporates all clarifications from
`spec.md` → Session 2026-07-09, including the second round: carried-over patients
preserve their prior-day relative order (FR-064), at most one active appointment
per patient-of-record per clinic per day (FR-041a), and an expired subscription
lets an already-open QueueSession run to close while blocking only new
bookings/sessions (FR-102). These behaviors are captured in `data-model.md` and the
`contracts/` files.

## Summary

Build a multi-tenant clinic queue SaaS as an ASP.NET Core 10 Modular Monolith (Clean
Architecture: Domain → Application → Infrastructure → Api) with a React 19 web
frontend serving all four roles (Patient, Receptionist, Doctor/ClinicOwner,
SuperAdmin). The MVP launches with one active clinic (Dr. Ghada Kamel, OB/GYN) on a
schema and access-control model that is multi-tenant-ready from day one. The core
technical risks — cross-tenant leakage, duplicate queue numbers under concurrency,
and lost notifications — are addressed structurally: three-layer tenant isolation
(JWT → execution-time repository filter → PostgreSQL RLS), transactional queue
numbering (`SELECT ... FOR UPDATE` + `RowVersion`), and an Outbox notification
pattern dispatched by a Hangfire worker. Six bounded-context modules (Identity,
Queue, Medical, Notifications, Reporting, Platform) share one PostgreSQL database
and one Redis instance, deployed via Docker Compose behind Nginx on a single
Hostinger VPS.

## Technical Context

**Language/Version**: C# (.NET 10, ASP.NET Core 10) for the backend; TypeScript
(strict) for the React 19 web frontend.

**Primary Dependencies**: EF Core 10 (data access via `BaseRepository<T>`), SignalR
with a Redis backplane (real-time queue position updates), Hangfire (Outbox
dispatch, auto-absence sweep, reminders, backup jobs), FluentValidation (every
input DTO), Web Push (VAPID) via Service Worker. Frontend: Vite, TanStack Query,
Tailwind + shadcn/ui, i18next (Arabic RTL default + English).

**Storage**: PostgreSQL 16 as the single system of record, with Row-Level Security
enabled on every clinic-scoped table (`clinic_id = current_setting('app.current_clinic')`).
Redis 7 for the SignalR backplane, output/response caching, and rate-limit counters.

**Testing**: xUnit for unit tests (domain logic, especially the urgent-injection
algorithm and threshold labeling) and Testcontainers-backed PostgreSQL integration
tests for repositories and endpoints. Isolation tests (cross-tenant leakage, tampered
JWT `clinic_id`) and a concurrency test (parallel arrival confirmations never
duplicate a queue number) are release gates, not optional coverage.

**Target Platform**: Linux server (Docker Compose + Nginx on a Hostinger VPS) for
the backend; modern web browsers for the frontend — Chrome/Edge/Firefox/Android for
full Web Push support, iOS 16.4+ as an installed PWA for Web Push, with SignalR
covering all browsers while a tab is open.

**Project Type**: Web application (ASP.NET Core backend + React frontend), single
deployable Modular Monolith, not microservices.

**Performance Goals**: Standard read requests ≤300ms p95 (SC-006); real-time queue
position update reaches the client ≤2s of the triggering change (SC-007); sustain
≥500 concurrent real-time connections per deployment (SC-008).

**Constraints**: TLS 1.2/1.3-only transport (SC-012); login rate-limited to
10/min/IP with sensitive-operation throttling (SC-013); stateless API (all session
state in PostgreSQL/Redis, no in-memory session, per constitution's Security &
Operational Non-Negotiables); migrations never auto-applied on production; UTC
timestamps in the database, converted to clinic timezone at the presentation layer
only; API versioned under `/api/v1/`, all list endpoints paginated (default 20,
max 100); errors as ProblemDetails (RFC 7807).

**Scale/Scope**: MVP launches with 1 active clinic (unlimited-tenant-ready schema);
architecture must support ≥200 clinics on the same deployment without structural
redesign (SC-014); daily encrypted off-site backups with 7-daily/4-weekly/6-monthly
retention, restore verified (SC-015). Feature surface: 10 user stories across 4
roles, 90 functional requirements (FR-001–FR-105), 6 bounded-context modules.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How this plan satisfies it |
|---|---|---|
| I. Tenant Isolation Is Sacred | **PASS** | `ITenantProvider` resolves `clinic_id` from JWT claims only (never body/query/header); `BaseRepository<T>` applies the tenant filter via expression trees at query execution time (not a cached EF global filter); every clinic-scoped table has PostgreSQL RLS enabled. Isolation tests (incl. tampered JWT) are a release gate — see Quality Gates below. |
| II. Transactional Queue Numbering | **PASS** | Real queue-number assignment happens inside a `SELECT ... FOR UPDATE` transaction on the `QueueSession` row; `Appointment.RowVersion` provides optimistic concurrency; the 2:1 urgent-injection interleave (FR-024) is applied inside the same locked transaction, not as a post-hoc reorder. A concurrency test is a release gate. |
| III. Outbox-Only Notifications | **PASS** | Every notification-worthy state change persists an `OutboxNotification` row in the same transaction as the state change; a Hangfire worker polls and dispatches via a channel-agnostic `INotificationDispatcher` (SignalR + Web Push now, FCM slot reserved for Phase 2), retrying failed dispatch 3× with exponential backoff. |
| IV. DTOs at the Boundary; Medical Data Least-Privilege | **PASS** | All endpoints bind to FluentValidation-checked DTOs, never entities; Visit/Prescription writes are restricted to the treating Doctor (per the Q4 clarification), ClinicOwner gets read-only clinical access, Receptionist sees booking-relevant fields only; every medical-file read is audit-logged. |
| V. Clean Architecture & Modular Monolith Boundaries | **PASS** | Existing solution skeleton (`src/ClinicQueue.Domain/Application/Infrastructure/Api`) already matches Domain→Application→Infrastructure→Api; the six modules (Identity, Queue, Medical, Notifications, Reporting, Platform) map directly to this feature's Key Entities and FR groupings. No microservices/message broker/Kubernetes introduced. |
| VI. Web-First MVP Scope Discipline | **PASS** | Scope in this plan matches spec.md's In/Out Scope exactly: no Flutter/FCM, no OTP, no attachments, no WhatsApp channel, no payment gateway. Notification dispatcher is designed channel-agnostic per constitution so FCM can slot in later without redesign. |
| Security & Operational Non-Negotiables | **PASS** | Secrets via env/Docker secrets only; migrations applied as an explicit reviewed step; stateless API; versioned/paginated/ProblemDetails API; UTC storage; Argon2id/bcrypt(≥12) hashing; HttpOnly Secure cookies + CSRF; audit logging ≥2-year retention. |
| Quality Gates & Testing Discipline | **PASS** | Isolation and concurrency tests planned as release gates per module; unit tests planned for the urgent-injection algorithm and threshold labeling; integration tests planned via Testcontainers PostgreSQL. |

No violations identified. Complexity Tracking table is intentionally empty — see
below.

## Project Structure

### Documentation (this feature)

```text
specs/001-clinic-queue-mvp/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
ClinicQueue.slnx
src/
├── ClinicQueue.Domain/            # Entities, value objects, domain events, enums
│   └── Common/                    # (existing) shared base types
├── ClinicQueue.Application/       # CQRS-style use-case handlers, DTOs, validators, interfaces
│   ├── Identity/                  # auth, users, roles, clinics, subscriptions, tenancy
│   ├── Queue/                     # sessions, appointments, numbering, urgent injection,
│   │                              #   pause/resume, carry-over
│   ├── Medical/                   # patient profiles, visits, prescriptions
│   ├── Notifications/             # outbox, dispatcher interface, templates
│   ├── Reporting/                 # volume, absence, peak hours, revenue, ratings
│   └── Platform/                  # super-admin: clinics, subscriptions, platform analytics
├── ClinicQueue.Infrastructure/    # EF Core (BaseRepository, RLS migrations), Redis, Hangfire,
│                                  #   Web Push, external services
└── ClinicQueue.Api/               # Controllers/endpoints (/api/v1/), SignalR hubs, middleware, DI

tests/
├── ClinicQueue.UnitTests/         # domain logic, urgent-injection algorithm, threshold labeling
└── ClinicQueue.IntegrationTests/  # Testcontainers PostgreSQL: repositories, endpoints,
                                   #   tenant-isolation tests, queue-concurrency tests

web/                               # React 19 + Vite + TypeScript (new — not yet created)
├── src/
│   ├── components/
│   ├── pages/                    # patient / reception / doctor / admin surfaces
│   └── services/                 # TanStack Query hooks, API client
└── tests/

docker/                            # Dockerfiles, docker-compose.yml, nginx conf (new — not yet created)
docs/                              # spec.md, plan.md, tasks.md, CLAUDE.md (existing, source of truth)
```

**Structure Decision**: Web application structure (backend + frontend), matching
the Clean Architecture solution already scaffolded at the repository root
(`src/ClinicQueue.{Domain,Application,Infrastructure,Api}`, `tests/ClinicQueue.{Unit,Integration}Tests`,
`ClinicQueue.slnx`). The `web/` and `docker/` directories are planned per
`docs/plan.md` §3/§8 but do not yet exist in the repository — they are created
during implementation, not during planning. Application-layer folders per module
(Identity/Queue/Medical/Notifications/Reporting/Platform) enforce the module
boundaries required by constitution Principle V.

## Complexity Tracking

*No entries — the Constitution Check above reported no violations. The design
follows the existing, already-ratified architecture (Modular Monolith, Clean
Architecture, three-layer tenant isolation) with no deviations requiring
justification.*
