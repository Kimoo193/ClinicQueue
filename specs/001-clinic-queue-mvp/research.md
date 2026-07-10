# Phase 0 Research: Clinic Queue SaaS MVP (Web-First)

All decisions below were already made in `docs/plan.md` prior to this Spec Kit
formalization; this document records them in Decision/Rationale/Alternatives form
for traceability and to close out every `NEEDS CLARIFICATION` that would otherwise
appear in Technical Context. No open unknowns remain.

## 1. Tenant isolation enforcement strategy

- **Decision**: Three independent, defense-in-depth layers — (1) `clinic_id`
  resolved only from signed JWT claims via `ITenantProvider`; (2) tenant filter
  applied in `BaseRepository<T>` via expression trees evaluated at query
  **execution time**; (3) PostgreSQL Row-Level Security as the final safety net.
- **Rationale**: A single layer is not trusted alone because each can fail
  independently — a forged request bypasses (1), an application bug bypasses (2),
  and RLS (3) still blocks the query at the database. This mirrors constitution
  Principle I directly.
- **Alternatives considered**: EF Core global query filter baked in at model-build
  time — **rejected**: a captured tenant id in the cached model pins the first
  resolved tenant for all subsequent requests (a known bug class called out
  explicitly in `docs/plan.md` §5 and constitution Principle I). Database-per-tenant
  — **rejected**: operationally heavy for an MVP targeting single-VPS deployment and
  ≥200-clinic scale-out later; row-level isolation in one database is simpler to
  operate and backup.

## 2. Real-time queue position delivery

- **Decision**: SignalR with a Redis backplane for in-app real-time updates (tab
  open); Web Push (VAPID) via Service Worker for tab-closed delivery.
- **Rationale**: Meets SC-007 (≤2s update latency) and SC-008 (≥500 concurrent
  connections) without client polling; the Redis backplane lets SignalR scale
  horizontally if the app layer is later scaled beyond one instance, consistent
  with the constitution's stateless-API requirement.
- **Alternatives considered**: Client polling — **rejected**: wastes bandwidth,
  cannot reliably hit the ≤2s target at scale, and does not help with tab-closed
  delivery. Long-polling only (no Web Push) — **rejected**: fails the tab-closed
  notification requirement (FR-075) entirely.

## 3. Notification dispatch pattern

- **Decision**: Outbox pattern — an `OutboxNotification` row is persisted in the
  same transaction as the triggering state change; a Hangfire worker polls pending
  rows (~every 10s) and dispatches through a channel-agnostic
  `INotificationDispatcher` (SignalR, Web Push now; FCM slot reserved for Phase 2),
  retrying failed dispatch 3× with exponential backoff before marking Failed.
- **Rationale**: Guarantees at-least-once delivery tied to the actual state change
  (constitution Principle III); eliminates the class of bug where the HTTP
  response succeeds but the notification silently never fires.
- **Alternatives considered**: Direct fire-and-forget dispatch inside the request
  — **rejected outright** (explicitly forbidden by constitution Principle III);
  a message broker (RabbitMQ/Azure Service Bus) — **rejected**: unnecessary
  operational complexity for MVP-to-200-clinic scale on a single VPS; Hangfire
  (already used for other background jobs) covers the outbox-worker need without
  adding new infrastructure.

## 4. Queue numbering concurrency control

- **Decision**: `SELECT ... FOR UPDATE` row lock on the owning `QueueSession`
  during real-number assignment, combined with `RowVersion` optimistic concurrency
  on `Appointment`. The 2:1 urgent-injection interleave (FR-024) and Instant Entry
  (ratio-neutral per the Q3 clarification) are both applied inside this same
  locked transaction.
- **Rationale**: Guarantees SC-011 (no duplicate real queue numbers under
  concurrent confirmation) — this is the release-gate concurrency test required by
  constitution Principle II.
- **Alternatives considered**: Database sequence/identity column alone —
  **rejected**: a raw auto-increment doesn't allow the urgent-injection reordering
  logic to be applied atomically with the assignment. Application-level in-memory
  locking (e.g., a `lock` statement or `SemaphoreSlim`) — **rejected**: breaks under
  the constitution's stateless/horizontally-scalable API requirement (a lock held
  in one process instance doesn't protect against a second instance).

## 5. Frontend stack

- **Decision**: React 19 + Vite + TypeScript (strict), Tailwind + shadcn/ui,
  TanStack Query for server state, i18next for Arabic (RTL, default) + English.
- **Rationale**: Matches the delivered UI kit (`docs/plan.md` §2) and provides a
  fast, lightweight single web dashboard serving all four roles per constitution
  Principle VI (web-first delivery for every role).
- **Alternatives considered**: Flutter Web — **superseded**; `docs/plan.md`
  explicitly notes the earlier Flutter-Web plan was replaced by React for a
  faster, lighter web dashboard, reserving Flutter for the Phase 2 native mobile
  app only.

## 6. Notification channel scope for MVP

- **Decision**: SignalR (tab open) + Web Push/VAPID (tab closed, Chrome/Edge/
  Firefox/Android, iOS 16.4+ PWA) only; FCM and WhatsApp are Phase 2.
- **Rationale**: Matches spec.md's Out-of-Scope list and constitution Principle VI;
  the dispatcher interface is designed channel-agnostic specifically so FCM can be
  added later without redesigning the Outbox/dispatch pipeline.
- **Alternatives considered**: Building FCM support now "since it's coming anyway"
  — **rejected**: constitution Principle VI explicitly forbids pulling Phase 2
  scope forward; the channel-agnostic interface already protects that future
  addition from requiring rework.

## 7. Hosting and deployment topology

- **Decision**: Docker Compose (nginx, api, web, postgres, redis, hangfire-worker)
  on a single Hostinger VPS; nginx routes `{clinic}.clinicqueue.app` subdomains to
  the same app, with tenant resolved by host header down to `clinic_id` via the
  Identity module.
- **Rationale**: At MVP-to-200-clinic scale, microservices/Kubernetes add
  operational complexity with no benefit (constitution Principle V); the
  stateless app design keeps the door open to horizontal scaling later without
  re-architecture.
- **Alternatives considered**: Kubernetes / managed container platform —
  **rejected** for this stage per constitution Principle V (explicitly forbidden
  for the MVP; any module may be extracted into its own service only if the
  platform genuinely outgrows the monolith).

## 8. Patient identity model across clinics

- **Decision**: One global patient identity (single email/phone + password login)
  with a separate clinic-scoped membership record per clinic (per the spec's Q1
  clarification); only membership and medical data are clinic-scoped, not the
  login itself.
- **Rationale**: The only reading consistent with FR-006 (unified appointment list
  across clinics) and FR-010 (single email/phone+password login); standard
  multi-tenant SaaS identity pattern (one identity, many tenant memberships).
- **Alternatives considered**: Fully separate per-clinic accounts stitched
  together for display — **rejected**: contradicts the single-login premise and
  adds complexity with no corresponding benefit at this scale.

## 9. Authentication and session storage

- **Decision**: Email/phone + password (Argon2id, or bcrypt cost ≥12); short-lived
  (15 min) access token + rotating refresh token (30 days, stored hashed); web
  tokens in HttpOnly Secure cookies with CSRF protection — never localStorage. No
  OTP in the MVP.
- **Rationale**: Matches spec.md's Locked Decisions #3 and #4 directly; HttpOnly
  cookies eliminate the primary XSS-driven token-theft vector for the web client.
- **Alternatives considered**: OTP-first authentication — **rejected**, explicitly
  deferred to Phase 2 per Locked Decision #4. Token in `localStorage` — **rejected**,
  explicitly forbidden by Locked Decision #3 and constitution's auth-hygiene
  non-negotiable.

## 10. Database and row-level isolation

- **Decision**: PostgreSQL 16 as the single system of record, with Row-Level
  Security enabled per clinic-scoped table (`clinic_id = current_setting('app.current_clinic')`,
  session variable set per request/connection), managed through EF Core 10
  migrations applied as an explicit, reviewed step (never auto-applied on
  production).
- **Rationale**: RLS is the final safety net in the three-layer isolation model
  (see #1); a single database keeps operational overhead (backup, restore,
  monitoring) low at MVP-to-200-clinic scale.
- **Alternatives considered**: Application-layer isolation only (no RLS) —
  **rejected**: leaves no safety net if application code has a tenant-filtering
  bug, which constitution Principle I explicitly treats as unacceptable.
