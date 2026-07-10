# spec.md — Clinic Queue SaaS

> **Specification Document** — Multi-Tenant Clinic Queue & Management Platform
> Version 1.0 · Web-First MVP · Target client: Dr. Ghada Kamel (OB/GYN)

---

## 1. Product Vision

A multi-tenant SaaS platform that replaces manual, phone-based clinic queue management with a real-time, count-based digital queue. The MVP launches **web-first** (patient, receptionist, and doctor all use the web), serving a single clinic (Dr. Ghada Kamel) while the schema and architecture are fully multi-tenant from day one. A native mobile app is Phase 2.

**Core differentiator:** No competitor in the Egyptian market offers a true live, count-based queue with emergency pause/resume and automatic day carry-over. That is our wedge.

---

## 2. Scope

### In Scope (MVP — Phase 1, Web)
- Multi-tenant foundation (single active clinic, unlimited-ready)
- Clinic self-registration + per-clinic subdomain + QR-code patient onboarding
- Email/phone + password authentication (no OTP in MVP)
- Smart queue: temporary → real number on arrival; normal/urgent with 2:1 injection
- Count-based patient view ("N patients ahead" + "wait home / head to clinic")
- Emergency pause/resume with notifications
- End-of-day close with carry-over to next day
- Instant Entry with mandatory logged reason
- Repeated-absence warning
- Notifications via SignalR (in-app real-time) + Web Push API (tab closed)
- Simplified patient medical file (text only — no attachments in MVP)
- E-prescription (text)
- Reports: daily/weekly/monthly volume, absence rate
- Super Admin console: clinics, subscriptions
- Arabic (RTL) + English (i18n from day one)

### Out of Scope (Phase 2+)
- Native mobile app (Flutter) + FCM push
- OTP authentication
- Medical file attachments (lab/imaging via R2)
- WhatsApp notification channel
- Online payment gateway (MVP: manual deposit confirmation)
- Insurance, telemedicine, pharmacy/lab integration
- Egyptian Tax Authority e-Receipt

---

## 3. Users & Roles

| Role | Access | Primary Interface (MVP) |
|---|---|---|
| **Patient** | Book, track own queue position, view own medical history/prescriptions. Can belong to multiple clinics. | Web (responsive) |
| **Receptionist** | Manage clinic queue: confirm arrivals, call next, instant entry, close day. Booking-relevant data only. | Web dashboard |
| **Doctor / Clinic Owner** | Full clinic admin: queue oversight, medical records, emergency controls, reports, manage receptionists. | Web dashboard |
| **Super Admin** | Platform operator: manage all clinics, subscriptions, platform analytics. | Web admin console |

---

## 4. Key Decisions (Locked)

1. **Urgent injection logic:** Urgent patients advance ahead of *present* normal patients on a **2 normal : 1 urgent** rhythm. After every 2 confirmed-present normal patients, 1 present urgent patient is inserted. Urgent does NOT jump straight to the front; it interleaves on that ratio. Ratio is configurable per clinic (default 2:1).
2. **Deposit booking:** Patient uploads a transfer screenshot OR pays at the clinic; receptionist confirms the booking manually. No-show after deposit → appointment auto carries over to next day (no refund).
3. **Languages:** Arabic (RTL, default) + English from day one.
4. **MVP scope:** Single clinic, multi-tenant-ready schema.
5. **Hosting:** Hostinger VPS (to be purchased), single platform domain, per-clinic subdomain (e.g., `ghada.clinicqueue.app`). Each clinic gets a **QR code** that opens its registration page so patients self-register into that clinic.
6. **Auth:** Email/phone + password (bcrypt/Argon2). No OTP in MVP.
7. **Medical file:** Text only in MVP; attachments deferred.
8. **Timezone:** Per-clinic timezone setting (ready for Gulf expansion).
9. **Delivery:** Web-first (all three roles on web). Mobile app is Phase 2, reusing the same backend.

---

## 5. Functional Requirements

### 5.1 Multi-Tenancy & Onboarding
- **FR-001** Clinic self-registration: name, specialty, contact, admin account.
- **FR-002** Unique tenant ID isolates all clinic data.
- **FR-003** Per-clinic branding (logo, primary/secondary color) on patient-facing screens.
- **FR-004** Per-clinic operational settings: urgent ratio, "head to clinic" threshold, auto-absence timeout, working days/hours, timezone.
- **FR-005** No user may access another clinic's data — enforced at app AND database layers.
- **FR-006** A patient may belong to multiple clinics with a unified appointment list.
- **FR-007** Each clinic exposes a QR code that links to its subdomain registration page.

### 5.2 Authentication & Authorization
- **FR-010** Patients register/login with email or phone + password.
- **FR-011** Passwords hashed with Argon2id (or bcrypt cost ≥ 12).
- **FR-012** Account lockout after 5 failed attempts (15 min).
- **FR-013** Short-lived access token (15 min) + rotating refresh token (30 days), stored hashed.
- **FR-014** RBAC: Patient, Receptionist, Doctor, ClinicOwner, SuperAdmin.
- **FR-015** Clinic Owner can invite/manage receptionist accounts.
- **FR-016** Web tokens stored in HttpOnly Secure cookies + CSRF protection (never localStorage).

### 5.3 Smart Queue (Core)
- **FR-020** Auto-open a QueueSession per operating day; block booking on a closed session.
- **FR-021** Assign a **temporary number** at booking time.
- **FR-022** Assign the **real queue number** ONLY on receptionist arrival confirmation, by arrival order.
- **FR-023** Support Normal and Urgent appointment types.
- **FR-024** Insert a present Urgent patient after every 2 confirmed-present Normal patients (configurable ratio). Urgent interleaves on the ratio; it does not jump to the absolute front. Already-numbered patients keep their relative order.
- **FR-025** Show each patient the **count ahead**, never an estimated time.
- **FR-026** Instruct "wait at home" when patients-ahead > threshold (default 4); "head to clinic" when ≤ threshold.
- **FR-027** Update queue position in real time via SignalR (no manual refresh).
- **FR-028** Receptionist calls the next patient → status "Inside".
- **FR-029** Auto-absent after configurable window (default 15 min) if a called patient does not respond; queue advances.
- **FR-030** Prevent duplicate queue numbers under concurrent confirmation (row lock + RowVersion).
- **FR-031** Instant Entry inserts a patient immediately with a **mandatory logged reason**.
- **FR-032** Record companions count at booking.

### 5.4 Booking
- **FR-040** Patient searches clinics by specialty, name, or location.
- **FR-041** Patient books for a given day, choosing Normal/Urgent where allowed.
- **FR-042** Two methods: Walk-in and Phone/Deposit (screenshot upload or pay-at-clinic; manual confirm).
- **FR-043** Receptionist can create a booking on a patient's behalf.
- **FR-044** Patient can cancel an upcoming appointment.
- **FR-045** Patient views history: Upcoming / Completed / Cancelled.
- **FR-046** Booking on behalf of a registered Dependent under a guardian account.
- **FR-047** Flag a patient absent twice without prior cancellation; receptionist may restrict booking.
- **FR-048** Morning reminder on the booking day requesting confirmation.

### 5.5 Emergency Management
- **FR-050** Receptionist/doctor pauses the active session (reason + timestamp).
- **FR-051** All pending/confirmed patients notified of pause; positions preserved.
- **FR-052** Doctor resumes; queue continues exactly where it stopped.
- **FR-053** All waiting patients notified on resume.
- **FR-054** Every pause/resume logged (user, time, reason).

### 5.6 End-of-Day & Carry-over
- **FR-060** Receptionist closes the day's session.
- **FR-061** System lists unserved patients at closure.
- **FR-062** Receptionist carries over each unserved appointment to next day, or cancels.
- **FR-063** Affected patients notified of carry-over + next-day position.
- **FR-064** Carried-over patients placed at the front of next day's queue, before new bookings.
- **FR-065** Deposit-paid no-shows auto carry over to the next operating day.

### 5.7 Notifications (Web-First)
- **FR-070** Booking confirmation notification (with temporary number).
- **FR-071** "Head to clinic" notification when position crosses threshold.
- **FR-072** "Your turn now" notification when called.
- **FR-073** Pause/resume notifications (FR-051, FR-053).
- **FR-074** Carry-over notification (FR-063).
- **FR-075** Delivery channels (MVP): SignalR in-app real-time (tab open) + Web Push API via Service Worker (tab closed, Chrome/Edge/Firefox/Android; iOS 16.4+ PWA).
- **FR-076** Retry failed delivery up to 3× with exponential backoff.
- **FR-077** No queue-position notification silently lost (Outbox pattern). Channel-agnostic dispatcher (SignalR / WebPush now; FCM in Phase 2).

### 5.8 Medical Records (text-only MVP)
- **FR-080** Per-clinic PatientProfile: blood type, chronic conditions, allergies, current medications.
- **FR-081** Doctor records a Visit per appointment: diagnosis, notes.
- **FR-082** Doctor issues an e-prescription (drug, dose, frequency, duration).
- **FR-083** Prescription shared with patient (viewable on web).
- **FR-084** Attachments DEFERRED to Phase 2.
- **FR-085** Diagnosis/notes/prescriptions restricted to treating Doctor/ClinicOwner; receptionist sees booking info only.
- **FR-086** Patient views own visit history + prescriptions per clinic.
- **FR-087** Every medical-file access logged (who, what, when).

### 5.9 Reports & Ratings
- **FR-090** Daily/weekly/monthly patient volume (clinic owner).
- **FR-091** No-show (absence) rate over a period.
- **FR-092** Peak days/hours from historical data.
- **FR-093** Revenue from deposit bookings over a period.
- **FR-094** Post-visit patient rating (1–5 + optional comment).
- **FR-095** Aggregate clinic rating shown in clinic search.

### 5.10 Super Admin
- **FR-100** View/activate/suspend any clinic.
- **FR-101** Manage subscription plans; track status + renewal date.
- **FR-102** Expired subscription → restrict writes, preserve read access.
- **FR-103** Notify clinic owner 7 days before expiry.
- **FR-104** Platform-wide usage & revenue analytics.
- **FR-105** Clinic self-service data export (patients, appointments, visits).

---

## 6. Non-Functional Requirements

| ID | Requirement | Target |
|---|---|---|
| NFR-001 | Standard read API latency | ≤ 300ms (p95) |
| NFR-002 | Real-time position update latency | ≤ 2s |
| NFR-003 | Concurrent real-time connections | ≥ 500 per deployment |
| NFR-010 | Transport encryption | TLS 1.2/1.3 only |
| NFR-011 | Password hashing | Argon2id / bcrypt(≥12) |
| NFR-012 | Cross-tenant data leakage | Zero — automated isolation tests |
| NFR-013 | Rate limiting | Login 10/min/IP; sensitive ops throttled |
| NFR-014 | Audit log retention | ≥ 2 years |
| NFR-020 | Clinics on current architecture | ≥ 200 |
| NFR-021 | App layer | Stateless (horizontal-scale ready) |
| NFR-030 | Uptime (operating hours) | ≥ 99.5% monthly |
| NFR-031 | Backups | Daily, encrypted, off-site (7/4/6) |

---

## 7. Success Criteria (MVP Done)

- Dr. Ghada's clinic runs a full day on the system with zero manual queue tracking.
- A patient books via QR, watches position update live, receives "head to clinic", arrives, is called — end to end.
- Receptionist handles walk-ins, deposits, instant entry, and closes the day with carry-over.
- Doctor pauses for an emergency; all waiting patients are notified and positions hold on resume.
- All tenant-isolation tests pass; no cross-clinic leakage.
- Web Push works on Android Chrome + desktop; SignalR live-updates in all supported browsers.
