# tasks.md — Clinic Queue SaaS

> Actionable, ordered task list for implementation with Cursor + Claude Code.
> Each task is small, verifiable, and maps to `spec.md` (FR-xxx) and `plan.md` (M1–M8).
> Mark `[x]` when done. Do not start a module before its predecessor's DoD is met.

---

## M1 — Foundation

### Solution & Tooling
- [ ] T1.1 Create solution with Domain / Application / Infrastructure / Api projects (Clean Architecture).
- [ ] T1.2 Add test projects (UnitTests, IntegrationTests) + Testcontainers for PostgreSQL.
- [ ] T1.3 Configure Serilog structured JSON logging (include `clinic_id` on every log scope).
- [ ] T1.4 Add health checks endpoint `/health` (DB + Redis).
- [ ] T1.5 Set up EF Core + PostgreSQL connection; initial migration pipeline (manual apply, not auto on prod).

### Multi-Tenancy (FR-001, FR-002, FR-005)
- [ ] T1.6 `Clinic` entity + `ITenantProvider` (Scoped) reading `clinic_id` from JWT claims.
- [ ] T1.7 `BaseRepository<T>` applying tenant filter at execution time via expression trees.
- [ ] T1.8 Enable PostgreSQL RLS + per-request `app.current_clinic` setting (layer 3).
- [ ] T1.9 Write first tenant-isolation integration test (clinic A cannot read clinic B).

### Auth & RBAC (FR-010 → FR-016)
- [ ] T1.10 `User` entity + roles enum (Patient, Receptionist, Doctor, ClinicOwner, SuperAdmin).
- [ ] T1.11 Register/login with email or phone + password (Argon2id/bcrypt≥12).
- [ ] T1.12 JWT issuance (15 min) + refresh token (30 days, hashed, rotating).
- [ ] T1.13 Store web tokens in HttpOnly Secure cookies + CSRF token.
- [ ] T1.14 Account lockout after 5 failed attempts (15 min).
- [ ] T1.15 Policy-based authorization (CanManageQueue, PlatformAdmin, etc.).
- [ ] T1.16 Clinic Owner invites/manages receptionist accounts (FR-015).

### Audit
- [ ] T1.17 `AuditLog` entity + interceptor/service; log all sensitive actions.

**DoD M1:** isolation test green · auth flow works · audit logging active · health check passes.

---

## M2 — Queue Core (the heart)

### Sessions & Appointments (FR-020 → FR-023)
- [ ] T2.1 `QueueSession` entity (per clinic/day; Active/Paused/Closed; urgentRatio; normalSinceLastUrgent).
- [ ] T2.2 Auto-open session on first booking/action of the day; block on closed.
- [ ] T2.3 `Appointment` entity (type, method, tempNumber, queueNumber, status, companions, RowVersion, carriedFrom).
- [ ] T2.4 Assign temporary number at booking (FR-021).

### Numbering & Urgent Injection (FR-022, FR-024, FR-030)
- [ ] T2.5 Arrival confirmation assigns real queue number by arrival order, inside a transaction with `SELECT ... FOR UPDATE` on the session (FR-030).
- [ ] T2.6 Implement urgent 2:1 injection algorithm (per plan.md §6); ratio from clinic settings.
- [ ] T2.7 Concurrency test: parallel confirmations never produce duplicate numbers.

### Patient View Logic (FR-025, FR-026)
- [ ] T2.8 Compute "patients ahead" count for a given patient.
- [ ] T2.9 Derive status label: >threshold → "wait home"; ≤threshold → "head to clinic" (threshold from settings).

### Queue Actions (FR-028, FR-029, FR-031)
- [ ] T2.10 Call-next → mark Inside.
- [ ] T2.11 Auto-absent job: called patient unresponsive past window → Absent + advance (Hangfire).
- [ ] T2.12 Instant Entry with mandatory reason (logged to AuditLog).

### Emergency & Carry-over (FR-050 → FR-065)
- [ ] T2.13 Pause session (reason + timestamp) → freeze queue.
- [ ] T2.14 Resume session → continue from stop point.
- [ ] T2.15 Close day → list unserved → carry over or cancel each.
- [ ] T2.16 Carried-over + deposit no-shows placed at front of next day (FR-064, FR-065).

### Real-time (FR-027)
- [ ] T2.17 SignalR hub with per-clinic groups (`clinic-{id}-queue`); Redis backplane.
- [ ] T2.18 Broadcast compact payload `{currentNumber, aheadOfYou, status}` on every queue change.

**DoD M2:** full-day queue simulation passes · concurrency test green · pause/resume/carry-over verified.

---

## M3 — Notifications (Web-First)

- [ ] T3.1 `OutboxNotification` entity; write in same transaction as state change (FR-077).
- [ ] T3.2 Hangfire worker polls Pending; retry ×3 exponential backoff (FR-076).
- [ ] T3.3 `INotificationDispatcher` (channel-agnostic): SignalR + Web Push now; FCM stub for Phase 2.
- [ ] T3.4 Web Push setup: VAPID keys, Service Worker, subscription storage per user (FR-075).
- [ ] T3.5 Message rules & templates: booking confirmed, head-to-clinic, your-turn, pause, resume, carry-over (FR-070 → FR-074), Arabic + English.
- [ ] T3.6 Morning reminder job on booking day (FR-048).

**DoD M3:** notifications fire on all trigger events · survive a failed send (Outbox retry) · Web Push works on Android Chrome + desktop.

---

## M4 — Medical Records (text-only)

- [ ] T4.1 `PatientProfile` (blood type, chronic, allergies, medications) per clinic (FR-080).
- [ ] T4.2 `Visit` (diagnosis, notes) per appointment (FR-081).
- [ ] T4.3 `Prescription` (items: drug/dose/frequency/duration); share with patient (FR-082, FR-083).
- [ ] T4.4 Access control: medical detail restricted to Doctor/ClinicOwner; receptionist booking-only (FR-085).
- [ ] T4.5 Patient views own visits + prescriptions (FR-086).
- [ ] T4.6 Log every medical-file access (FR-087).
- [ ] T4.7 Repeated-absence flag + booking restriction (FR-047).

**DoD M4:** doctor can record visit + prescription · patient sees prescription · access control test green · medical-access audit works.

---

## M5 — Reports & Ratings

- [ ] T5.1 Volume report: daily/weekly/monthly (FR-090).
- [ ] T5.2 Absence-rate report (FR-091).
- [ ] T5.3 Peak days/hours (FR-092).
- [ ] T5.4 Deposit revenue report (FR-093).
- [ ] T5.5 Post-visit rating (1–5 + comment) (FR-094).
- [ ] T5.6 Aggregate clinic rating in search (FR-095).

**DoD M5:** reports return correct numbers verified against seeded data.

---

## M6 — Super Admin

- [ ] T6.1 Clinics list + activate/suspend (FR-100).
- [ ] T6.2 Subscription plans + status + renewal tracking (FR-101).
- [ ] T6.3 Expiry middleware: restrict writes, keep reads (FR-102).
- [ ] T6.4 7-day pre-expiry notification (FR-103).
- [ ] T6.5 Platform analytics (FR-104).
- [ ] T6.6 Clinic data export — patients/appointments/visits (FR-105).

**DoD M6:** expired clinic is read-only · super admin can manage all clinics.

---

## M7 — Web Frontend (React, RTL/i18n)

### Setup
- [ ] T7.1 React 19 + Vite + TS; Tailwind + shadcn/ui; i18next (Arabic RTL default + English).
- [ ] T7.2 Auth flow (cookie-based); TanStack Query; Axios interceptors (refresh + retry).
- [ ] T7.3 Per-clinic dynamic theming from API (logo + colors) (FR-003).
- [ ] T7.4 Subdomain + QR registration landing (FR-007).

### Patient (web, responsive)
- [ ] T7.5 Register/login; clinic search; book (normal/urgent, walk-in/deposit + screenshot upload).
- [ ] T7.6 Live queue widget (count ahead + status) via SignalR + Web Push opt-in.
- [ ] T7.7 My appointments (upcoming/completed/cancelled); cancel; dependents.
- [ ] T7.8 My medical history + prescriptions.

### Reception dashboard
- [ ] T7.9 Queue table (all statuses); confirm arrival; call next; instant entry (reason).
- [ ] T7.10 Manual booking; deposit confirmation; pause/resume; close day + carry-over UI.

### Doctor dashboard
- [ ] T7.11 Today summary + emergency toggle; queue oversight.
- [ ] T7.12 Visit + prescription entry; reports views.

### Super Admin console
- [ ] T7.13 Clinics, subscriptions, platform analytics screens.

**DoD M7:** all four role journeys work end-to-end in the browser, Arabic + English, matching the UI kit.

---

## M8 — Hardening & Launch

- [ ] T8.1 Full E2E tenant-isolation suite (read/write/report/JWT-tamper) — release gate.
- [ ] T8.2 Load test: 500 concurrent SignalR patients; queries < 50ms (EXPLAIN ANALYZE on hot paths).
- [ ] T8.3 Security pass: OWASP API checklist, rate limits, security headers, CORS allowlist, secret scan.
- [ ] T8.4 Rate limiting configured + tested (login, sensitive ops).
- [ ] T8.5 Docker Compose prod stack + Nginx TLS (Let's Encrypt) + subdomain routing.
- [ ] T8.6 Backups: daily encrypted off-site (Hangfire) + tested restore.
- [ ] T8.7 Monitoring: health checks + uptime alert (Uptime Kuma) + Sentry for exceptions.
- [ ] T8.8 CI/CD: build → test → vuln scan → deploy → health check → rollback on fail.
- [ ] T8.9 Privacy Policy + Terms (health data) at registration.
- [ ] T8.10 Onboarding wizard for a new clinic + generate its QR code.

**DoD M8 (MVP launch):** all success criteria in spec.md §7 met · isolation + load + security gates green · Dr. Ghada's clinic live.

---

## Phase 2 (post-MVP, not now)
- [ ] P2.1 Flutter mobile app (patient + doctor) reusing the same API.
- [ ] P2.2 FCM push via the existing dispatcher.
- [ ] P2.3 OTP authentication.
- [ ] P2.4 Medical attachments (Cloudflare R2 + pre-signed URLs).
- [ ] P2.5 WhatsApp channel (Twilio) behind feature flag.
- [ ] P2.6 Online payment gateway.
