<!--
Sync Impact Report
==================
Version change: [TEMPLATE] → 1.0.0 (initial ratification)
Bump rationale: First concrete constitution for this project — filling an unratified
  template counts as the initial MAJOR ratification (semantic baseline established).

Modified principles: N/A (template placeholders replaced with concrete content; no
  prior named principles existed)

Added sections:
  - I. Tenant Isolation Is Sacred (Three-Layer Defense)
  - II. Transactional Queue Numbering
  - III. Outbox-Only Notifications
  - IV. DTOs at the Boundary; Medical Data Is Least-Privilege
  - V. Clean Architecture & Modular Monolith Boundaries
  - VI. Web-First MVP Scope Discipline
  - Security & Operational Non-Negotiables (Section 2)
  - Quality Gates & Testing Discipline (Section 3)
  - Governance (amendment procedure, versioning policy, compliance review)

Removed sections: All bracketed template placeholders (no prior ratified content existed).

Templates requiring updates:
  - .specify/templates/plan-template.md: ✅ no change needed — "Constitution Check"
    gate already reads gates dynamically from this file ("[Gates determined based on
    constitution file]"); no agent-specific or stale references found.
  - .specify/templates/spec-template.md: ✅ no change needed — scope/requirements
    structure (User Stories, FR-xxx, Success Criteria) is already compatible with
    ClinicQueue's spec.md conventions (FR-xxx numbering, Success Criteria section).
  - .specify/templates/tasks-template.md: ✅ no change needed — phase/user-story
    structure already accommodates foundational tenancy/auth work as blocking
    prerequisites (Phase 2: Foundational) ahead of story-specific tasks.
  - .specify/templates/checklist-template.md: not inspected (no principle-specific
    checklist content to reconcile at this time).

Follow-up TODOs: None. RATIFICATION_DATE and LAST_AMENDED_DATE both set to the date
  of this initial ratification since no earlier adoption date exists in repo history.
-->

# ClinicQueue Constitution

## Core Principles

### I. Tenant Isolation Is Sacred (Three-Layer Defense)

Every data access in this system MUST be scoped by `clinic_id`, enforced through three
independent, defense-in-depth layers:

1. **Token integrity**: `clinic_id` MUST come only from claims in the signed JWT,
   resolved per request via `ITenantProvider` (Scoped). It MUST NEVER be read from
   request body, query string, or header. A tampered-token isolation test is a
   release gate.
2. **Application-layer filtering at execution time**: The tenant filter MUST be
   applied in `BaseRepository<T>` via expression trees evaluated at query execution
   time. It MUST NOT be baked into a cached EF Core global query filter at
   model-build time — a captured tenant id in the cached model pins the first
   resolved tenant for all subsequent requests (a known, previously-observed bug
   class in this codebase's history).
3. **Database-layer enforcement (PostgreSQL RLS)**: Every table holding clinic data
   MUST have `ENABLE ROW LEVEL SECURITY` with a policy on
   `clinic_id = current_setting('app.current_clinic')`, with the session variable set
   per request/connection. RLS is the final safety net when application code has a
   bug, not a redundant formality.

Every new entity that holds clinic-scoped data MUST carry `ClinicId` and MUST be
covered by an automated isolation test verifying clinic A cannot read or write clinic
B's data, including under a tampered JWT. Isolation tests are a release gate, not an
optional nice-to-have.

**Rationale**: This is multi-tenant SaaS from day one; a single cross-tenant leak is
a trust-ending, possibly legally exposing event (medical data is in scope). No single
layer is trusted alone — the three layers exist precisely because each can fail
independently (application bug, forged input, missing session variable).

### II. Transactional Queue Numbering

Any assignment of a real (non-temporary) queue number MUST happen inside a database
transaction that takes `SELECT ... FOR UPDATE` on the owning `QueueSession` row.
Appointments MUST use `RowVersion` for optimistic concurrency. Queue numbers MUST
NEVER be assigned outside a lock, and numbers already assigned to confirmed patients
MUST NEVER change once assigned (only not-yet-confirmed patients move relative to
newly confirmed ones). The urgent-injection interleave (default 2 normal : 1 urgent,
configurable per clinic) MUST be applied inside this same locked transaction, not as
a post-hoc reordering step.

An automated concurrency test — parallel arrival confirmations must never yield
duplicate queue numbers — is a release gate for the Queue module.

**Rationale**: The queue number is the core promise to the patient ("you are #N").
Duplicate or shifting numbers under concurrent receptionist actions breaks that
promise and the product's central differentiator (live, trustworthy, count-based
queueing).

### III. Outbox-Only Notifications

No code path may dispatch a notification via a direct, fire-and-forget call inside a
request. Every notification-worthy state change (booking confirmation, position
update, pause/resume, carry-over, "your turn") MUST persist an `OutboxNotification`
row in the same database transaction as the state change. A Hangfire worker MUST poll
and dispatch pending rows through a channel-agnostic `INotificationDispatcher`
(SignalR and Web Push in the MVP; FCM slots in later without redesign). Failed
dispatch MUST retry up to 3 times with exponential backoff before being marked Failed
and alerted on.

**Rationale**: A queue system that silently drops a "head to clinic" or "your turn"
notification actively harms patients (missed calls, wasted trips). The Outbox
pattern guarantees at-least-once delivery tied to the actual state change, eliminating
the class of bugs where the HTTP response succeeds but the notification is lost.

### IV. DTOs at the Boundary; Medical Data Is Least-Privilege

Request input MUST bind to DTOs, never directly to EF Core entities (mass-assignment
risk). Endpoints MUST NEVER return entities directly — all responses map through
DTOs. Every input DTO MUST be validated with FluentValidation.

Medical data (diagnosis, clinical notes, prescriptions) is visible ONLY to the
treating Doctor/ClinicOwner. Receptionists MUST see booking-relevant fields only
(no diagnosis, notes, or prescription content). Every read of a medical file MUST be
audit-logged (who, what, when) — this is not optional for "just viewing."

**Rationale**: DTO boundaries prevent over-posting and accidental exposure of
internal/sensitive entity fields. Medical data carries a higher duty of care than
booking data; role-scoped visibility plus audit logging is the least-privilege
control that keeps that duty enforceable and reviewable after the fact.

### V. Clean Architecture & Modular Monolith Boundaries

The backend is a single deployable Modular Monolith (ASP.NET Core), organized as
Clean Architecture layers with dependencies pointing inward only:
`Domain → Application → Infrastructure → Api`. Domain MUST have no framework
dependencies. Application holds CQRS-style use-case handlers, DTOs, validators, and
interfaces; Infrastructure implements those interfaces (EF Core, Redis, Hangfire,
Web Push, external services); Api exposes thin controllers/endpoints, SignalR hubs,
and middleware.

The monolith is organized into six bounded-context modules: **Identity** (auth,
users, roles, clinics, subscriptions, tenancy), **Queue** (sessions, appointments,
numbering, urgent injection, pause/resume, carry-over), **Medical** (patient
profiles, visits, prescriptions), **Notifications** (outbox, dispatcher, templates),
**Reporting** (volume, absence, peak hours, revenue, ratings), and **Platform**
(super-admin: clinics, subscriptions, platform analytics). Cross-module calls MUST
go through application-layer interfaces; direct reach-through into another module's
entities is prohibited. Microservices, message brokers, or Kubernetes MUST NOT be
introduced — the single-VPS Modular Monolith is a deliberate decision, not a
placeholder for a "real" architecture later; any module may be extracted into its
own service only if the platform genuinely outgrows the monolith.

**Rationale**: Clean layering keeps Domain logic (especially the queue algorithm)
testable and framework-independent. Module boundaries prevent the monolith from
decaying into an unstructured ball of mud while deliberately avoiding the
operational cost of microservices at MVP-to-200-clinics scale.

### VI. Web-First MVP Scope Discipline

The MVP ships **web-first**: patient, receptionist, doctor, and super-admin all use
the React web application. The following are explicitly Phase 2 and MUST NOT be
implemented in the MVP: native mobile app (Flutter) and FCM push, OTP authentication,
medical file attachments (lab/imaging via R2), WhatsApp notification channel, online
payment gateway (MVP uses manual deposit confirmation only), insurance, telemedicine,
pharmacy/lab integration, and Egyptian Tax Authority e-Receipt integration.

Phase 2 exclusion does not license sloppy seams: the notification dispatcher MUST
remain channel-agnostic so FCM can slot in later, and the schema MUST remain
multi-tenant-ready even though the MVP serves a single clinic. Scope discipline means
declining to build Phase 2 features now, not declining to design for their eventual
arrival.

**Rationale**: A single-clinic, web-first MVP with multi-tenant-ready architecture
lets the product reach a real client (Dr. Ghada Kamel) fast without foreclosing the
Phase 2 roadmap. Pulling Phase 2 scope forward delays the MVP and risks over-building
for requirements that are not yet validated.

## Security & Operational Non-Negotiables

- **No secrets in code.** Connection strings, JWT signing keys, VAPID keys, and all
  other secrets MUST live in environment variables or Docker secrets only, and MUST
  NEVER be committed to the repository.
- **Migrations are never auto-applied on production.** EF Core migrations MUST be
  generated and then applied as an explicit, reviewed step — never automatically on
  application startup in production.
- **Stateless API.** No in-memory session state. All state lives in PostgreSQL or
  Redis so the API can scale horizontally without re-architecture.
- **Versioned, paginated, structured API.** All endpoints are versioned under
  `/api/v1/`. All list endpoints MUST be paginated (default 20, max 100). Errors MUST
  return ProblemDetails (RFC 7807) and MUST NOT leak stack traces or internals in
  production.
- **UTC in the database.** All persisted timestamps are UTC; conversion to the
  clinic's configured timezone happens at the presentation layer only.
- **Auth hygiene.** Passwords MUST be hashed with Argon2id or bcrypt (cost ≥ 12).
  Web tokens MUST be stored in HttpOnly Secure cookies with CSRF protection — never
  in localStorage. Sensitive/auth endpoints MUST have rate limiting (e.g., login
  10/min/IP; lockout after 5 failed attempts for 15 minutes).
- **Audit logging.** Every sensitive or state-changing action (medical file access,
  Instant Entry, pause/resume, carry-over decisions) MUST be written to the audit
  log with actor, action, and timestamp, retained for at least 2 years.

## Quality Gates & Testing Discipline

- Every public use case MUST have at least one test; tenant-touching code MUST have
  an isolation test.
- Unit tests are required for domain logic, especially the urgent-injection
  algorithm and count-ahead/threshold labeling.
- Integration tests (Testcontainers PostgreSQL) are required for repositories and
  endpoints.
- **Isolation tests** (clinic A cannot read/write/report clinic B; tampered JWT
  `clinic_id` rejected) and the **concurrency test** (parallel arrival confirmations
  never yield duplicate queue numbers) are release gates — a module is not done
  without them passing.
- A module is not marked done until its Definition of Done is met: tests green
  (including isolation/concurrency where relevant), input validated via DTO,
  access scoped and authorized, audit logging present where sensitive, no secrets
  in code, API versioned and paginated, and reviewed against the security checklist
  in `docs/CLAUDE.md`.
- Frontend: React function components with hooks, TypeScript strict (no `any`),
  TanStack Query for server state, RTL-first with all user-facing strings routed
  through i18next (Arabic + English) — no hardcoded copy.

## Governance

This constitution is the highest-precedence source of engineering rules for
ClinicQueue. Where `docs/CLAUDE.md`, `docs/spec.md`, `docs/plan.md`, or any other
project document appears to conflict with this constitution, this constitution
governs; the conflicting document MUST be updated to match, not the other way around.

**Amendment procedure**: Amendments are proposed as a change to this file with an
explicit rationale, reviewed against the consistency propagation checklist (plan,
spec, and tasks templates; command files; runtime guidance docs), and merged only
after that propagation is complete or explicitly deferred with a recorded TODO.

**Versioning policy**: This constitution follows semantic versioning:

- **MAJOR** — backward-incompatible governance changes or removal/redefinition of an
  existing principle.
- **MINOR** — a new principle or materially expanded section is added.
- **PATCH** — clarifications, wording, or non-semantic refinements.

**Compliance review**: Every feature plan MUST pass the Constitution Check gate
(see `.specify/templates/plan-template.md`) before Phase 0 research and again after
Phase 1 design. Any violation MUST be justified in that plan's Complexity Tracking
table or the plan MUST be revised to comply. Pull requests touching tenant-scoped
data, queue numbering, notifications, medical records, or module boundaries MUST be
reviewed against the relevant principle(s) above in addition to normal code review.

**Version**: 1.0.0 | **Ratified**: 2026-07-09 | **Last Amended**: 2026-07-09
