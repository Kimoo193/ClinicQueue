# CLAUDE.md — Clinic Queue SaaS

> Instructions for Claude Code / Cursor when working in this repository.
> Read this before every task. It encodes the non-negotiables.

---

## Project Summary

Multi-tenant SaaS for smart clinic queue management. **Web-first MVP** (patient, receptionist, doctor, super-admin all on web via React). First client: Dr. Ghada Kamel (OB/GYN). Backend is a Modular Monolith in ASP.NET Core **10** (`net10.0`) + PostgreSQL 16 + Redis, deployed via Docker Compose (e.g. single VPS). Native mobile (Flutter) is Phase 2 and reuses the same backend.

Read `specs/001-clinic-queue-mvp/spec.md` (what), `plan.md` (how), `tasks.md` (order) before implementing. Do not invent requirements — if something is ambiguous, ask. Operator docs: `docs/running.md`, `docs/architecture.md`.

---

## Golden Rules (never violate)

1. **Tenant isolation is sacred.** Every data access is scoped by `clinic_id`. Never read `clinic_id` from request body/query/header — only from the signed JWT via `ITenantProvider`. Every new entity that holds clinic data MUST have `ClinicId` and be covered by an isolation test. **Pre-auth / no-tenant writes** (anonymous clinic sign-up, registration-with-clinic, patient join before a clinic claim exists) run under the app role with an EMPTY `app.current_clinic`, so the RLS `WITH CHECK` will REJECT any insert into a clinic-scoped table. These legitimate onboarding writes MUST go through `PrivilegedDbContext` (BYPASSRLS) with each row pinned to an explicit `ClinicId` and a per-row scope guard — never "fix" the resulting 500 by moving the write back onto the app context (that only passes because tests wrongly ran as superuser). See `ClinicEndpoints.JoinAsPatientAsync` / `CreateClinicAsync` and `AuthEndpoints.RegisterAsync`.
2. **No secrets in code.** Connection strings, keys, VAPID keys → environment variables / Docker secrets only. Never commit them.
3. **Tenant filter at execution time, not model-build time.** Apply the filter in `BaseRepository<T>` via expression trees. Do NOT put a captured tenant id into an EF Core global filter that gets cached in the model (it pins the first tenant — known bug).
4. **Queue numbering is transactional.** Any assignment of a real queue number happens inside a DB transaction with `SELECT ... FOR UPDATE` on the session row. Appointments use `RowVersion` for optimistic concurrency. Never assign numbers outside a lock.
5. **Notifications go through the Outbox.** Never send a notification with a direct fire-and-forget call inside a request. Persist an `OutboxNotification` in the same transaction as the state change; the Hangfire worker dispatches it.
6. **DTOs, not entities, at the API boundary.** Never bind request input directly to an EF entity (mass-assignment risk). Never return entities directly. Map through DTOs.
7. **Medical data is least-privilege.** Diagnosis, notes, and prescriptions are visible to the treating Doctor/ClinicOwner only. Receptionists see booking-relevant fields only. Every medical-file read is audit-logged.
8. **Migrations are never auto-applied on production.** Generate migrations; apply them as an explicit, reviewed step.
9. **Background jobs run under BYPASSRLS — the database will NOT protect you.** Every job query and mutation on a tenant table MUST carry an explicit `ClinicId` predicate. Never rely on structural uniqueness (e.g. one `QueueSession` per clinic/day) for isolation. Enumerate clinics (or Distinct `ClinicId`) first, then process per clinic — see `BookingReminderJob`, `AutoAbsenceJob`, `OutboxDispatchJob`. Never add `ExecuteUpdateAsync` / `ExecuteDeleteAsync` on tenant tables without a `ClinicId` filter in the same expression.

---

## Architecture Constraints

- **Clean Architecture:** Domain → Application → Infrastructure → Api. Dependencies point inward. Domain has no framework dependencies.
- **Modular Monolith:** modules = Identity, Queue, Medical, Notifications, Reporting, Platform. Keep cross-module calls through application-layer interfaces, not direct entity reach-through.
- **CQRS-style handlers** in Application for use cases. Keep controllers/endpoints thin.
- **Stateless API:** no in-memory session state. All state in PostgreSQL/Redis (horizontal-scale ready).
- **API versioned** under `/api/v1/`. All list endpoints paginated (default 20, max 100). Errors as ProblemDetails (RFC 7807), no stack traces in production.
- **UTC in the database.** Convert to the clinic's timezone at the presentation layer.

---

## Coding Conventions

- C#: latest stable, nullable reference types on, async/await throughout, `CancellationToken` on I/O.
- Validation: FluentValidation on every input DTO.
- Naming: entities singular (`Appointment`), tables snake_case plural (`appointments`).
- Every public use case has at least one test. Tenant-touching code has an isolation test.
- Keep functions small and named by intent. No dead code, no commented-out blocks.
- Frontend: React function components + hooks, TypeScript strict, TanStack Query for server state, no `any`. RTL-first; all user-facing strings via i18next (ar + en) — no hardcoded copy.

---

## Domain Rules (must be respected in code)

- **Temporary vs real number:** temp number at booking (FR-021); real queue number ONLY on receptionist arrival confirmation, by arrival order (FR-022).
- **Urgent injection (2:1):** after every 2 confirmed-present normal patients, insert 1 present urgent patient (ratio configurable per clinic). Urgent interleaves on the ratio — it does NOT jump to the absolute front. Already-numbered patients keep their order. See plan.md §6 for the exact algorithm.
- **Patient sees a count, never a time.** ">threshold ahead → wait home; ≤threshold → head to clinic" (threshold configurable, default 4).
- **Pause/Resume:** pause freezes the queue and notifies waiting patients; resume continues from the exact stop point.
- **Carry-over:** unserved patients (incl. deposit no-shows) carry to the next operating day, placed at the front, before new bookings.
- **Instant Entry** always requires a mandatory reason, written to the audit log.

---

## Security Checklist (apply to every feature)

- [ ] Input validated (FluentValidation) and bound to a DTO, not an entity.
- [ ] Access scoped by `clinic_id` from JWT; ownership re-checked on by-id fetches (no IDOR).
- [ ] AuthZ policy enforced for the role(s) allowed to call it.
- [ ] Sensitive/state-changing action written to AuditLog.
- [ ] No secret introduced into code; config via env.
- [ ] Rate limiting considered for auth/sensitive endpoints.
- [ ] Errors return ProblemDetails without leaking internals.
- [ ] New clinic-data entity has an isolation test.

---

## Testing Requirements

- Unit tests for domain logic (esp. urgent-injection algorithm, threshold labeling).
- Integration tests (Testcontainers PostgreSQL) for repositories and endpoints.
- **Integration tests MUST connect as the non-owner app role (`clinicqueue_app`, NOBYPASSRLS), exactly as production does. A test that runs as superuser proves nothing about RLS.** The container owner (`clinicqueue`) is for **seeding/migrations only**, never for exercising request-path behaviour. Request-path (WebApplicationFactory) tests use `AppRoleApiFactory`, which wires `Default → clinicqueue_app`, `Privileged → clinicqueue_bypassrls`, `Migrations → clinicqueue`. Only a test that specifically asserts the privileged/bypass path may drive an elevated role, and it must be named to say so (e.g. `RlsEnforcementTests` uses `fixture.BypassConnectionString` deliberately). A previous onboarding-write RLS bug shipped green under 89 superuser-run tests — do not reintroduce that blind spot.
- **Isolation tests** (release gate): clinic A cannot read/write/report clinic B; tampered JWT `clinic_id` rejected.
- **Concurrency test:** parallel arrival confirmations never yield duplicate queue numbers.
- Do not mark a module done until its DoD in tasks.md is met.

---

## What NOT to do

- Do NOT add microservices, message brokers, or Kubernetes — this is a single-VPS Modular Monolith by decision.
- Do NOT introduce OTP, WhatsApp, FCM, R2 attachments, or a payment gateway in the MVP — they are Phase 2 (but keep the notification dispatcher channel-agnostic so FCM slots in later).
- Do NOT store JWTs in localStorage for the web — HttpOnly Secure cookies only.
- Do NOT bypass the Outbox for "just this one" notification.
- Do NOT read tenant id from anywhere except the JWT.
- Do NOT auto-apply migrations on production.

---

## Definition of Done (every task)

Tests green (incl. isolation where relevant) · input validated · access scoped & authorized · audit where sensitive · no secrets in code · API versioned & paginated · reviewed against the security checklist above.

---

## References in this repo
- `specs/001-clinic-queue-mvp/spec.md` — full requirements (FR-xxx), locked decisions, success criteria.
- `specs/001-clinic-queue-mvp/plan.md` — architecture, stack, module boundaries, algorithms, deployment.
- `specs/001-clinic-queue-mvp/tasks.md` — ordered Spec Kit task list.
- `docs/running.md` — how to run locally and with Docker.
