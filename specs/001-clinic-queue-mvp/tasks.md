---
description: "Task list for Clinic Queue SaaS MVP (Web-First)"
---

# Tasks: Clinic Queue SaaS MVP (Web-First)

**Input**: Design documents from `/specs/001-clinic-queue-mvp/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Test tasks below are limited to areas the constitution mandates as
release gates (tenant-isolation, queue-numbering concurrency, urgent-injection
algorithm) plus primary-flow integration tests per story. They are NOT exhaustive
TDD — they are the quality gates required by `.specify/memory/constitution.md`.

**Organization**: Tasks are grouped by user story (from spec.md) to enable
independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1–US10 map to the user stories in spec.md
- File paths follow the plan.md structure: `src/ClinicQueue.{Domain,Application,Infrastructure,Api}/`, `tests/ClinicQueue.{Unit,Integration}Tests/`, `web/`, `docker/`

## Module boundaries (constitution Principle V)

`Identity` · `Queue` · `Medical` · `Notifications` · `Reporting` · `Platform` —
cross-module calls go through Application-layer interfaces only.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Dependencies, project wiring, and scaffolding for the existing Clean
Architecture solution (`src/ClinicQueue.*`, `tests/ClinicQueue.*` already exist).

- [x] T001 Add backend NuGet packages and wire Clean Architecture project references (Domain ← Application ← Infrastructure ← Api): EF Core 10 + Npgsql, SignalR, StackExchange.Redis, Hangfire + Hangfire.PostgreSql, FluentValidation, BCrypt, Asp.Versioning — update the four `.csproj` files under `src/`
- [x] T002 [P] Replace test placeholders and configure xUnit + Testcontainers (PostgreSQL) in `tests/ClinicQueue.IntegrationTests/` (remove `UnitTest1.cs`) and `tests/ClinicQueue.UnitTests/` (remove `UnitTest1.cs`)
- [x] T003 [P] Scaffold React 19 + Vite + TypeScript (strict) app in `web/` with Tailwind + shadcn/ui, TanStack Query, and i18next configured for Arabic (RTL, default) + English
- [x] T004 [P] Create `docker/` with Dockerfiles (api, web), `docker/docker-compose.yml` (nginx, api, web, postgres, redis, hangfire-worker), and `docker/nginx.conf` with TLS 1.2/1.3 and per-clinic subdomain routing
- [x] T005 [P] Configure formatting/linting: `.editorconfig` + `dotnet format` for backend; ESLint + Prettier with `no-any` strict rules in `web/`
- [x] T006 Configure environment/Docker-secrets binding (no secrets in code) and base `Program.cs` host builder in `src/ClinicQueue.Api/Program.cs` and `src/ClinicQueue.Api/appsettings.json`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Three-layer tenancy, identity/auth, Outbox, audit, and cross-cutting
API concerns. **No user story can begin until this phase is complete.**

**⚠️ CRITICAL**: These implement constitution Principles I, III, IV and the
Security & Operational Non-Negotiables.

### Tenant isolation — three layers (constitution Principle I)

- [X] T007 Create Domain base types (`BaseEntity`, `IClinicScoped` with `ClinicId`, `RowVersion` concurrency token) in `src/ClinicQueue.Domain/Common/`
- [X] T008 Define `ITenantProvider` (resolves `clinic_id` from JWT claims only — never body/query/header) as a Domain-layer port in `src/ClinicQueue.Domain/Common/ITenantProvider.cs` and implement the HTTP/JWT adapter in `src/ClinicQueue.Infrastructure/Tenancy/JwtTenantProvider.cs`
- [X] T009 Create `AppDbContext` and `BaseRepository<T>` applying the tenant filter via **execution-time expression trees** (NOT a cached EF global query filter) in `src/ClinicQueue.Infrastructure/Persistence/` (constitution Principle I, FR-005)
- [X] T010 Add PostgreSQL RLS: migration enabling `ROW LEVEL SECURITY` + policy `clinic_id = current_setting('app.current_clinic')` on every clinic-scoped table, plus a connection interceptor setting `app.current_clinic` per request in `src/ClinicQueue.Infrastructure/Persistence/RlsConnectionInterceptor.cs`
- [X] T011 Configure EF Core migrations with an explicit (never auto-applied on production) apply step; generate the initial migration in `src/ClinicQueue.Infrastructure/Migrations/`
- [X] T011a Configure UTC timestamp persistence (Npgsql `timestamptz`; all entity timestamps stored and read as UTC — no local kinds) in `src/ClinicQueue.Infrastructure/Persistence/AppDbContext.cs` (constitution: UTC in the database)

### Identity, auth, and multi-tenant onboarding (Identity module)

- [X] T012 [P] Create `User` (global identity), `ClinicMembership`, `Clinic`, `Subscription`, `Dependent` entities in `src/ClinicQueue.Domain/Identity/`
- [X] T013 [P] Implement password hashing (Argon2id or bcrypt cost ≥12) in `src/ClinicQueue.Infrastructure/Identity/PasswordHasher.cs`
- [X] T014 Implement JWT issuance + refresh-token rotation (hashed, 15-min access / 30-day refresh) writing HttpOnly Secure cookies with CSRF protection in `src/ClinicQueue.Infrastructure/Identity/TokenService.cs`
- [X] T015 Implement auth endpoints (register, login, refresh, logout) with account lockout (5 failures / 15 min) and FluentValidation DTOs in `src/ClinicQueue.Api/Endpoints/Identity/AuthEndpoints.cs` (FR-010–FR-013, FR-016)
- [X] T016 [P] Define RBAC authorization policies (Patient, Receptionist, Doctor, ClinicOwner, SuperAdmin) in `src/ClinicQueue.Api/Authorization/` (FR-014)
- [X] T017 Implement clinic self-registration, per-clinic settings (urgent ratio, thresholds, timezone, repeated-absence count), branding, and QR-code endpoints in `src/ClinicQueue.Api/Endpoints/Identity/ClinicEndpoints.cs` (FR-001–FR-004, FR-007)

### Cross-cutting infrastructure

- [X] T018 Add rate limiting (login 10/min/IP; sensitive-op throttling) in `src/ClinicQueue.Api/Middleware/RateLimiting.cs` (SC-013)
- [X] T019 Add ProblemDetails (RFC 7807) global exception handler, `/api/v1/` API versioning, and pagination defaults (20, max 100) in `src/ClinicQueue.Api/` (constitution Security & Operational Non-Negotiables)
- [X] T020 [P] Create `AuditLog` entity + append-only audit-writing service, with a ≥2-year retention policy configured, in `src/ClinicQueue.Domain/Common/AuditLog.cs` and `src/ClinicQueue.Infrastructure/Auditing/` (FR-054, FR-087, constitution audit retention)
- [X] T021 Implement Outbox infrastructure: `OutboxNotification` entity, channel-agnostic `INotificationDispatcher`, Hangfire polling worker with 3× exponential-backoff retry, and server-side SignalR + Web Push (VAPID) channel implementations in `src/ClinicQueue.Infrastructure/Notifications/` (constitution Principle III, FR-075, FR-076, FR-077, SC-009)
- [X] T022 Configure Redis, the SignalR Redis backplane, and the Hangfire server in `src/ClinicQueue.Api/Program.cs` and `src/ClinicQueue.Infrastructure/`

### Foundational test harness (release gate — constitution Principle I)

- [X] T023 [P] Build the tenant-isolation integration-test harness (clinic A cannot read/write clinic B; tampered-JWT `clinic_id` rejected) in `tests/ClinicQueue.IntegrationTests/Isolation/TenantIsolationTests.cs` — extended per entity in later phases (SC-005)
- [X] T023a [P] Add `/health` liveness + readiness endpoints (readiness checks PostgreSQL and Redis connectivity) in `src/ClinicQueue.Api/Endpoints/HealthEndpoints.cs` (SC-010, docs/plan.md §8)

**Checkpoint**: Foundation ready — user story implementation can begin.

---

## Phase 3: User Story 1 — Patient: Book & Track a Live Queue Position (P1) 🎯 MVP

**Goal**: A patient books an appointment, receives a temporary number, and watches
their count-ahead update live with a "wait home / head to clinic" instruction.

**Independent Test**: Book a Normal appointment against an open session; observe the
temporary number and booking-confirmation notification; as arrivals ahead are
confirmed, the count-ahead and instruction update live within 2s. *(Live-update
scenarios are fully exercised once US2's confirm-arrival exists — both are P1 and
together form the core loop.)*

### Tests for User Story 1

- [X] T024 [P] [US1] Unit test for count-ahead threshold labeling (wait-home when > threshold, head-to-clinic when ≤) in `tests/ClinicQueue.UnitTests/Queue/ThresholdLabelingTests.cs` (FR-025, FR-026)
- [X] T025 [P] [US1] Integration test: booking assigns a temporary number and persists a `BookingConfirmation` outbox row in the same transaction in `tests/ClinicQueue.IntegrationTests/Queue/BookingTests.cs` (FR-021, FR-070)

### Implementation for User Story 1

- [X] T026 [P] [US1] Create `QueueSession` (with `NormalSinceLastUrgent`, pause state, `RowVersion`) and `Appointment` (type, method, temp/real number, status, companions, instant-entry, `RowVersion`) entities in `src/ClinicQueue.Domain/Queue/`
- [X] T027 [US1] Implement QueueSession auto-open per operating day and block-booking-on-closed-session in `src/ClinicQueue.Application/Queue/Sessions/` (FR-020)
- [X] T028 [US1] Implement the patient booking use case: assign temporary number, record companions, default type Normal, optional "possible urgent" flag, reject duplicate active same-day booking per patient-of-record in `src/ClinicQueue.Application/Queue/Booking/CreateAppointmentHandler.cs` (FR-021, FR-032, FR-041, FR-041a)
- [X] T029 [US1] Implement count-ahead computation + threshold labeling service in `src/ClinicQueue.Application/Queue/Positioning/CountAheadService.cs` (FR-025, FR-026)
- [X] T030 [US1] Create the `queue` SignalR hub pushing per-patient count-ahead + instruction to group `clinic:{clinicId}` in `src/ClinicQueue.Api/Hubs/QueueHub.cs` (FR-027)
- [X] T031 [US1] Wire booking-confirmation and head-to-clinic outbox triggers (notification rules) in `src/ClinicQueue.Application/Queue/Notifications/` (FR-070, FR-071)
- [X] T032 [US1] Implement `POST /api/v1/appointments` and `GET /api/v1/patients/me/appointments` endpoints with FluentValidation DTOs in `src/ClinicQueue.Api/Endpoints/Queue/BookingEndpoints.cs` (FR-021, FR-045)
- [X] T033 [P] [US1] Build the patient booking + live-tracking web UI (book form, temporary number, live count-ahead, wait-home/head-to-clinic banner) with i18next strings in `web/src/features/patient-queue/`
- [X] T033a [P] [US1] Register the Web Push service worker and the browser push-subscription flow (VAPID) for tab-closed notifications in `web/src/serviceWorker/push.ts` (FR-075)
- [X] T033b [P] [US1] Create a shared UTC→clinic-timezone display formatter, reused by all web surfaces, in `web/src/lib/datetime.ts` (constitution: convert UTC to clinic timezone at the presentation layer)

**Checkpoint**: US1 booking + live tracking demoable (numbering completed in US2).

---

## Phase 4: User Story 2 — Receptionist: Run the Daily Smart Queue (P1)

**Goal**: Confirm arrivals (assigning real numbers by arrival order), call next,
interleave urgent on the 2:1 ratio, handle instant entry and on-behalf booking —
never producing a duplicate or out-of-order number.

**Independent Test**: Confirm a mix of Normal and Urgent walk-ins; verify call order
matches the configured ratio and no duplicate real numbers occur even under
simultaneous confirmations.

### Tests for User Story 2

- [X] T034 [P] [US2] Unit test for the deterministic 2:1 urgent-injection algorithm, including ratio-neutral Instant Entry, per docs/plan.md §6 in `tests/ClinicQueue.UnitTests/Queue/UrgentInjectionTests.cs` (FR-024, FR-031)
- [X] T035 [P] [US2] Concurrency integration test: parallel `confirm-arrival` on the same session never yields duplicate real numbers in `tests/ClinicQueue.IntegrationTests/Queue/QueueConcurrencyTests.cs` (release gate — SC-011, FR-030)

### Implementation for User Story 2

- [X] T036 [US2] Implement transactional real-number assignment using `SELECT ... FOR UPDATE` on the `QueueSession` row + `Appointment.RowVersion` optimistic concurrency in `src/ClinicQueue.Application/Queue/Numbering/AssignRealNumberHandler.cs` (constitution Principle II, FR-022, FR-030)
- [X] T037 [US2] Implement the deterministic urgent-injection algorithm (2:1 configurable, interleave not front-jump, ratio-neutral Instant Entry) inside the locked transaction in `src/ClinicQueue.Application/Queue/Numbering/UrgentInjectionService.cs` (FR-024, FR-031)
- [X] T038 [US2] Implement `POST /api/v1/appointments/{id}/confirm-arrival` in `src/ClinicQueue.Api/Endpoints/Queue/ArrivalEndpoints.cs` (FR-022)
- [X] T039 [US2] Implement `POST /api/v1/appointments/{id}/call-next` (status → Inside) with a "your turn now" outbox trigger in `src/ClinicQueue.Api/Endpoints/Queue/ArrivalEndpoints.cs` (FR-028, FR-072)
- [X] T040 [US2] Implement Instant Entry endpoint (mandatory reason, audit-logged, ratio-neutral) in `src/ClinicQueue.Api/Endpoints/Queue/ArrivalEndpoints.cs` (FR-031)
- [X] T041 [P] [US2] Implement on-behalf booking and set-appointment-type-Urgent (Receptionist/Doctor only) endpoints in `src/ClinicQueue.Api/Endpoints/Queue/BookingEndpoints.cs` (FR-043, FR-023, FR-041)
- [X] T042 [US2] Implement the auto-absence Hangfire job (called patient no-response past the configured window → Absent, queue advances) in `src/ClinicQueue.Infrastructure/Jobs/AutoAbsenceJob.cs` (FR-029)
- [X] T043 [P] [US2] Build the reception queue-console web UI (confirm arrival, call next, instant entry, on-behalf booking, urgent toggle) in `web/src/features/reception-console/`

**Checkpoint**: Core live-queue loop (US1 + US2) fully functional — MVP-viable.

---

## Phase 5: User Story 3 — Doctor/Owner: Emergency Control & Medical Records (P1)

**Goal**: Pause/resume the queue without losing positions; record visits and
prescriptions visible only to authorized clinical roles, every read audited.

**Independent Test**: Pause with reason (all notified, positions held), resume
(continues from stop point); record a visit + prescription and confirm a
receptionist cannot view clinical content while ClinicOwner has read-only access.

### Tests for User Story 3

- [X] T044 [P] [US3] Integration test: pause notifies all pending/confirmed patients and preserves positions; resume continues from the stop point in `tests/ClinicQueue.IntegrationTests/Queue/PauseResumeTests.cs` (FR-050–FR-053)
- [X] T045 [P] [US3] Integration test: medical least-privilege — Doctor writes, ClinicOwner read-only, Receptionist 403 — and every read writes an AuditLog entry in `tests/ClinicQueue.IntegrationTests/Medical/MedicalAccessTests.cs` (FR-085, FR-087)

### Implementation for User Story 3

- [X] T046 [US3] Implement pause/resume use cases + endpoints with audit logging and pause/resume outbox triggers in `src/ClinicQueue.Api/Endpoints/Queue/EmergencyEndpoints.cs` (FR-050–FR-054, FR-073)
- [X] T047 [P] [US3] Create `PatientProfile`, `Visit`, `Prescription` entities in `src/ClinicQueue.Domain/Medical/`
- [X] T048 [US3] Implement Visit create/edit (treating Doctor only) and e-prescription issue (treating Doctor only) use cases in `src/ClinicQueue.Application/Medical/` (FR-081, FR-082)
- [X] T049 [US3] Implement the medical access-control policy (Doctor write; ClinicOwner read-only; Receptionist none) + medical-file read audit logging in `src/ClinicQueue.Api/Endpoints/Medical/MedicalEndpoints.cs` (FR-085, FR-087)
- [X] T050 [P] [US3] Build the doctor web UI (emergency pause/resume, visit + prescription entry, read-only clinical view for ClinicOwner) in `web/src/features/doctor-console/`

**Checkpoint**: All three P1 stories complete — full clinical MVP.

---

## Phase 6: User Story 4 — Patient: Manage Bookings Across Clinics (P2)

**Goal**: Search clinics, cancel, book for dependents, and see one unified
cross-clinic appointment history.

**Independent Test**: Search by specialty, book, cancel, book for a dependent, and
confirm all appear correctly categorized in a single history spanning clinics.

- [X] T051 [P] [US4] Integration test: unified appointment list spans two clinics for one global patient identity in `tests/ClinicQueue.IntegrationTests/Queue/UnifiedHistoryTests.cs` (FR-006)
- [X] T052 [P] [US4] Implement clinic search (by specialty, name, location) endpoint in `src/ClinicQueue.Api/Endpoints/Queue/ClinicSearchEndpoints.cs` (FR-040)
- [X] T053 [US4] Implement cancel-appointment and history (Upcoming/Completed/Cancelled) + unified cross-clinic list use cases and endpoints in `src/ClinicQueue.Application/Queue/Booking/` (FR-044, FR-045, FR-006)
- [X] T054 [US4] Implement booking on behalf of a registered Dependent in `src/ClinicQueue.Application/Queue/Booking/DependentBookingHandler.cs` (FR-046)
- [X] T055 [P] [US4] Build the patient booking-management web UI (search, history tabs, cancel, dependent booking) in `web/src/features/patient-bookings/`

**Checkpoint**: Patients can self-manage across clinics.

---

## Phase 7: User Story 5 — Receptionist: Deposit Bookings & Day Close / Carry-Over (P2)

**Goal**: Confirm deposit bookings; close the day and carry unserved patients to the
front of the next day (preserving their prior-day order), including auto carry-over
for deposit no-shows.

**Independent Test**: Run a session with ≥2 unserved patients in a known order;
close the day; carry them over; confirm they lead the next day's queue in the same
relative order, ahead of new bookings.

- [X] T056 [P] [US5] Integration test: carry-over places patients at the front preserving prior-day order; deposit no-show auto-carries-over at close in `tests/ClinicQueue.IntegrationTests/Queue/CarryOverTests.cs` (FR-064, FR-065)
- [X] T057 [US5] Implement the deposit booking method (screenshot upload or pay-at-clinic) with receptionist manual confirmation in `src/ClinicQueue.Application/Queue/Booking/DepositBookingHandler.cs` (FR-042)
- [X] T058 [US5] Implement close-day + unserved-list and carry-over use cases (front placement, preserve prior-day relative order) + endpoints with carry-over outbox triggers in `src/ClinicQueue.Api/Endpoints/Queue/DayCloseEndpoints.cs` (FR-060–FR-064, FR-074)
- [X] T059 [US5] Implement the deposit no-show auto-carry-over Hangfire job in `src/ClinicQueue.Infrastructure/Jobs/DepositCarryOverJob.cs` (FR-065)
- [X] T060 [P] [US5] Build the reception day-close / carry-over web UI (deposit confirm, unserved list, carry-over/cancel actions) in `web/src/features/reception-dayclose/`

**Checkpoint**: Full daily operational cycle works end to end.

---

## Phase 8: User Story 6 — Doctor/Owner: Reports, Ratings & Receptionist Management (P2)

**Goal**: Volume/absence/peak/revenue reports, post-visit ratings + aggregate
rating, and receptionist account management.

**Independent Test**: After several days of activity with no-shows and ratings,
confirm reports are accurate and the owner can invite a receptionist.

- [X] T061 [P] [US6] Integration test: absence-rate and volume report calculations over a period in `tests/ClinicQueue.IntegrationTests/Reporting/ReportsTests.cs` (FR-090, FR-091)
- [X] T062 [P] [US6] Implement Reporting module use cases (volume, absence rate, peak hours, deposit revenue) in `src/ClinicQueue.Application/Reporting/`
- [X] T063 [US6] Implement report endpoints in `src/ClinicQueue.Api/Endpoints/Reporting/ReportEndpoints.cs` (FR-090–FR-093)
- [X] T064 [P] [US6] Implement post-visit rating submission + aggregate clinic rating (surfaced in clinic search) in `src/ClinicQueue.Api/Endpoints/Reporting/RatingEndpoints.cs` (FR-094, FR-095)
- [X] T065 [US6] Implement ClinicOwner invite/manage receptionist accounts in `src/ClinicQueue.Api/Endpoints/Identity/MembershipEndpoints.cs` (FR-015)
- [X] T066 [P] [US6] Build the owner reports + staff-management web UI in `web/src/features/owner-reports/`

**Checkpoint**: Clinic business insights and staffing available.

---

## Phase 9: User Story 7 — Super Admin: Manage Clinics & Subscriptions (P2)

**Goal**: View/activate/suspend clinics; manage subscriptions with expiry
write-restriction (grace for an already-open session) and a 7-day renewal notice.

**Independent Test**: Suspend a clinic (staff ops blocked), reactivate (ops resume);
expire a subscription mid-open-session and confirm the session runs to close while
new bookings/sessions are blocked.

- [X] T067 [P] [US7] Integration test: suspended clinic blocks staff ops; expired subscription blocks new bookings/sessions but lets an already-open session finish in `tests/ClinicQueue.IntegrationTests/Platform/SubscriptionTests.cs` (FR-100, FR-102)
- [X] T068 [P] [US7] Implement Platform module clinic lifecycle (list, activate, suspend) in `src/ClinicQueue.Application/Platform/Clinics/` and `src/ClinicQueue.Api/Endpoints/Platform/ClinicAdminEndpoints.cs` (FR-100)
- [X] T069 [US7] Implement subscription plan management + expiry write-restriction (open-session grace) + 7-day renewal-notice Hangfire job in `src/ClinicQueue.Application/Platform/Subscriptions/` and `src/ClinicQueue.Infrastructure/Jobs/RenewalNoticeJob.cs` (FR-101, FR-102, FR-103)
- [X] T070 [P] [US7] Build the super-admin console web UI (clinics list, activate/suspend, subscription management) in `web/src/features/admin-console/`

**Checkpoint**: Platform operator can run the business.

---

## Phase 10: User Story 8 — Patient: View Medical History & Prescriptions (P3)

**Goal**: Patients view their per-clinic visit history, prescriptions, and health
profile.

**Independent Test**: A doctor records a visit + prescription; the patient views both
under the correct clinic and maintains their profile.

- [X] T071 [P] [US8] Implement PatientProfile read/update endpoint (self) in `src/ClinicQueue.Api/Endpoints/Medical/ProfileEndpoints.cs` (FR-080)
- [X] T072 [US8] Implement patient per-clinic visit/prescription history endpoint in `src/ClinicQueue.Api/Endpoints/Medical/MedicalEndpoints.cs` (FR-083, FR-086)
- [X] T073 [P] [US8] Build the patient medical-history + profile web UI in `web/src/features/patient-medical/`

**Checkpoint**: Patient continuity-of-care surface complete.

---

## Phase 11: User Story 9 — Receptionist: Handle Repeated Absences (P3)

**Goal**: Flag patients past the configured repeated-absence threshold and restrict
their booking; send morning-of reminders.

**Independent Test**: Mark a patient absent to the threshold without cancellation;
confirm the flag appears and booking can be restricted.

- [X] T074 [P] [US9] Implement repeated-absence flag (per-clinic threshold, default 2) + booking-restriction action in `src/ClinicQueue.Application/Queue/Booking/AbsencePolicyService.cs` (FR-047)
- [X] T075 [US9] Implement the morning-of booking-day reminder Hangfire job in `src/ClinicQueue.Infrastructure/Jobs/BookingReminderJob.cs` (FR-048)
- [X] T076 [P] [US9] Surface the repeated-absence flag + restrict action in the reception web UI in `web/src/features/reception-console/`

**Checkpoint**: No-show safeguard active.

---

## Phase 12: User Story 10 — Super Admin: Platform Analytics & Data Export (P3)

**Goal**: Platform-wide usage/revenue analytics and clinic self-service export.

**Independent Test**: Generate platform analytics after multi-clinic activity;
trigger a clinic export and confirm it contains only that clinic's data.

- [X] T077 [P] [US10] Implement platform-wide usage + revenue analytics in `src/ClinicQueue.Api/Endpoints/Platform/AnalyticsEndpoints.cs` (FR-104)
- [X] T078 [US10] Implement clinic self-service export (patients, appointments, visits — clinic-scoped only) in `src/ClinicQueue.Application/Platform/Export/ClinicExportHandler.cs` (FR-105)
- [X] T079 [P] [US10] Add analytics + export UI to the super-admin console in `web/src/features/admin-console/`

**Checkpoint**: All 10 user stories complete.

---

## Phase 13: Polish & Cross-Cutting Concerns

**Purpose**: Release-gate hardening and validation across all stories.

- [X] T080 Extend the tenant-isolation test sweep (T023) to every clinic-scoped entity — release gate (SC-005, constitution Principle I)
- [X] T081 [P] Load test: ≥500 concurrent SignalR connections, read p95 ≤300ms, position update ≤2s in `tests/ClinicQueue.IntegrationTests/Performance/` (SC-006, SC-007, SC-008)
- [X] T081a [P] Configure uptime + health monitoring and alerting against the `/health` endpoints (target ≥99.5% during operating hours) in `docker/` (SC-010)
- [X] T082 [P] Security pass: verify rate limiting, security headers, CORS, TLS-only, and no secrets in code (SC-012, SC-013)
- [X] T083 [P] Backup/restore drill: daily encrypted off-site backups (7/4/6 retention) with a verified restore, documented in `docker/` (SC-015)
- [X] T084 [P] Generate OpenAPI documentation for all `/api/v1/` endpoints in `src/ClinicQueue.Api/`
- [X] T085 [P] Accessibility + RTL/i18n QA pass across all web surfaces (no hardcoded strings, ar/en complete) in `web/`
- [X] T086 Run the `quickstart.md` end-to-end validation scenarios (all 7) against the Docker Compose stack

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies — start immediately.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all user stories**.
- **User Stories (Phases 3–12)**: all depend on Foundational.
  - US1 (P1) and US2 (P1) together form the core live-queue loop; US1's live-update
    scenarios are validated once US2's `confirm-arrival` (T036–T038) exists.
  - US3–US10 depend only on Foundational and are otherwise independent of each other.
- **Polish (Phase 13)**: depends on all targeted stories being complete.

### Story-level notes

- US4, US5, US9 build on the `Appointment`/`QueueSession` entities created in US1
  (T026) — those entities are the earliest-story shared model.
- US8 reuses the Medical entities created in US3 (T047).
- US6 and US10 reuse Reporting/Platform scaffolding; US7 owns Subscription behavior
  that US6 revenue reporting reads.

### Parallel opportunities

- All `[P]` Setup tasks (T002–T005) run together.
- Foundational `[P]` tasks (T012, T013, T016, T020, T023) run in parallel where they
  touch different files.
- Within each story, `[P]`-marked entity/UI tasks run alongside backend handlers.
- Once Foundational completes, P2/P3 stories (US4–US10) can be staffed in parallel.

---

## Implementation Strategy

### MVP first

1. Complete Phase 1 (Setup) + Phase 2 (Foundational).
2. Complete Phase 3 (US1) + Phase 4 (US2) — the core live-queue loop.
3. Add Phase 5 (US3) for the full clinical MVP (emergency + records).
4. **STOP and VALIDATE**: run quickstart Scenarios 1–5; confirm the isolation
   (T023) and concurrency (T035) release gates pass.

### Incremental delivery

Add P2 stories (US4→US7) then P3 stories (US8→US10), each independently testable
and deployable, before the Phase 13 hardening pass.

---

## Notes

- `[P]` = different files, no dependency on an incomplete task.
- `[Story]` label maps each task to a spec.md user story for traceability.
- Every clinic-scoped entity created in any phase MUST carry `ClinicId`, get RLS
  coverage (extend T010), and be added to the isolation sweep (T080).
- **Blanket rule (constitution Principle IV)**: every endpoint task binds input to a
  DTO validated with FluentValidation and returns a DTO (never an entity) — this
  applies to all `Endpoints/*` tasks even where not restated per task.
- **Blanket rule (constitution Security & Operational)**: all persisted timestamps
  are UTC (T011a); all displayed timestamps are converted to the clinic timezone via
  the shared formatter (T033b).
- Commit after each task or logical group; do not mark a module done until its
  release-gate tests pass (constitution Quality Gates).
