# Feature Specification: Clinic Queue SaaS MVP (Web-First)

**Feature Branch**: `001-clinic-queue-mvp`

**Created**: 2026-07-09

**Status**: Draft

**Input**: User description: "Formalize docs/spec.md into Spec Kit format. Do NOT
reinvent requirements. Feature: Clinic Queue SaaS MVP (web-first, single clinic
launch, multi-tenant-ready). Include user stories per role (Patient, Receptionist,
Doctor/Owner, Super Admin), numbered acceptance criteria (Given/When/Then), preserve
all FR-xxx references from docs/spec.md, explicit In/Out scope from spec.md §2, and
locked decisions (2:1 urgent ratio, count-not-time, HttpOnly cookies, no OTP in MVP)."

**Source of truth**: This specification formalizes the existing `docs/spec.md`
(v1.0) into Spec Kit format for a single clinic (Dr. Ghada Kamel, OB/GYN) launch. It
does not introduce new requirements beyond what `docs/spec.md` and `docs/plan.md`
already describe; all `FR-xxx` identifiers are preserved verbatim for traceability.

## Clarifications

### Session 2026-07-09

- Q: When the same person registers at Clinic A and later also at Clinic B, what is the patient account model? → A: One global patient identity (single email/phone + password) with per-clinic membership records; login is platform-wide, medical/appointment data stays clinic-scoped.
- Q: Who is authorized to set an appointment's type to Urgent? → A: Only Receptionist/Doctor can mark an appointment Urgent; a patient booking online can only request Normal, optionally flagging "possible urgent" for staff review.
- Q: Does an Instant Entry insertion affect the 2:1 urgent-ratio counter? → A: No — Instant Entry is ratio-neutral; it inserts immediately without incrementing or resetting the normal/urgent counter, which continues exactly as if the Instant Entry never happened.
- Q: Can a ClinicOwner who isn't the treating Doctor write Visit diagnosis/notes/prescriptions? → A: No — ClinicOwner has read access to all clinical content for oversight, but only the Doctor role can create/edit a Visit or issue a prescription.
- Q: Is the repeated-absence count ("twice") that triggers a flag/booking-restriction fixed or per-clinic configurable? → A: Clinic-configurable setting (default 2), added to the same per-clinic settings group as FR-004.
- Q: When multiple patients are carried over to the next day, what determines their order among themselves at the front of the queue? → A: Preserve their relative order from the previous day (the order they held when the day closed).
- Q: May the same patient-of-record hold more than one active appointment at the same clinic for the same operating day? → A: No — at most one active (booked/present) appointment per patient-of-record, per clinic, per operating day; a dependent is a distinct patient-of-record.
- Q: When a subscription expires while a QueueSession is already open, does the write-restriction halt the active session? → A: No — grace period: an already-open QueueSession runs to normal close (arrivals, calls, numbering still work); only NEW bookings and NEW session creation are blocked until renewal.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Patient: Book and Track a Live Queue Position (Priority: P1)

A patient discovers a clinic, books an appointment for a given day, and — on the
day — watches their position in the queue update in real time so they know when to
head to the clinic without waiting there or calling to ask.

**Why this priority**: This is the product's core differentiator (count-based live
queue) and the primary reason a patient uses the platform at all. Without it there
is no viable product.

**Independent Test**: Can be fully tested by having a patient search for the clinic,
book a normal appointment for the current operating day, and observe their assigned
temporary number, then their real number and count-ahead update as the receptionist
confirms arrivals ahead of them — delivering the "know when to leave home" value
end to end.

**Acceptance Scenarios**:

1. **Given** a clinic has an open QueueSession for today, **When** a patient books an
   appointment, **Then** the system assigns a temporary number and shows a booking
   confirmation (FR-021, FR-070).
2. **Given** a patient's appointment has a temporary number, **When** the
   receptionist confirms the patient's arrival, **Then** the system assigns the real
   queue number by arrival order (FR-022).
3. **Given** a patient is waiting with a real queue number, **When** other patients
   ahead are confirmed present or become absent, **Then** the patient's "count ahead"
   updates in real time without a manual page refresh (FR-025, FR-027).
4. **Given** a patient's count-ahead is greater than the clinic's configured
   threshold (default 4), **When** the patient views their status, **Then** the
   system instructs "wait at home"; **when** the count-ahead is at or below the
   threshold, **then** it instructs "head to clinic" and the patient receives a
   "head to clinic" notification (FR-026, FR-071).
5. **Given** a patient has been called ("Inside" status), **When** they do not
   respond within the configured auto-absence window (default 15 minutes), **Then**
   the system marks them absent and the queue advances (FR-029).

---

### User Story 2 - Receptionist: Run the Daily Smart Queue (Priority: P1)

A receptionist manages the live queue for the day: confirming arrivals, calling the
next patient, inserting urgent cases on the configured ratio, and handling walk-ins
and instant entries — without ever producing a duplicate or out-of-order queue
number.

**Why this priority**: The receptionist is the operational engine of the queue; if
this workflow is unreliable, the live-queue promise to patients breaks immediately.

**Independent Test**: Can be fully tested by confirming a mix of normal and urgent
walk-in patients against a running QueueSession and verifying the resulting call
order matches the configured urgent ratio, with no duplicate numbers even when two
confirmations are attempted at the same moment.

**Acceptance Scenarios**:

1. **Given** a QueueSession is open, **When** the receptionist confirms a normal
   patient's arrival, **Then** that patient receives the next sequential real number
   and the clinic's normal-patient counter increments (FR-022, FR-023).
2. **Given** two confirmed-present normal patients have been recorded since the last
   urgent insertion (default ratio 2:1, configurable per clinic), **When** the
   receptionist confirms a waiting urgent patient's arrival, **Then** that urgent
   patient is inserted immediately after the last-confirmed patient rather than
   appended to the back of the line or jumped to the absolute front (FR-024).
3. **Given** two receptionist actions attempt to confirm arrivals for the same
   QueueSession at the same moment, **When** both requests are processed, **Then**
   the system guarantees no two patients receive the same queue number (FR-030).
4. **Given** an urgent situation requiring immediate attention, **When** the
   receptionist performs an Instant Entry, **Then** the system requires a mandatory
   reason before inserting the patient, logs the action, and leaves the 2:1
   urgent-ratio counter unchanged (FR-031).
5. **Given** a patient calls ahead or walks in without a prior booking, **When** the
   receptionist creates a booking on the patient's behalf, **Then** the appointment
   is created exactly as if the patient had booked themselves (FR-043).
6. **Given** the receptionist is viewing the queue, **When** they call the next
   patient, **Then** that patient's status changes to "Inside" and they receive a
   "your turn now" notification (FR-028, FR-072).

---

### User Story 3 - Doctor/Clinic Owner: Emergency Control and Medical Record Keeping (Priority: P1)

A doctor or clinic owner can pause the entire queue during an emergency without
losing anyone's place, resume exactly where it stopped, and record a visit,
diagnosis, and e-prescription for the patient currently being seen — visible only to
authorized clinical roles.

**Why this priority**: Emergency handling and accurate medical recordkeeping are
non-negotiable clinical safety and trust requirements; a queue product for a doctor's
office must not compromise either.

**Independent Test**: Can be fully tested by pausing an active session with a reason,
confirming all waiting patients are notified and positions are preserved, resuming
the session, and separately recording a visit and prescription for a seen patient and
confirming a receptionist account cannot view its clinical contents.

**Acceptance Scenarios**:

1. **Given** an active QueueSession, **When** the doctor or receptionist pauses it
   with a reason, **Then** all pending/confirmed patients are notified and their
   positions are preserved unchanged (FR-050, FR-051, FR-054).
2. **Given** a paused session, **When** the doctor resumes it, **Then** the queue
   continues from the exact point it stopped and all waiting patients are notified
   (FR-052, FR-053, FR-054).
3. **Given** a patient's appointment is in progress, **When** the treating doctor
   records a Visit, **Then** the system stores diagnosis and notes tied to that
   appointment; a ClinicOwner who is not the treating doctor can view but not edit
   it (FR-081).
4. **Given** a recorded Visit, **When** the treating doctor issues an
   e-prescription (drug, dose, frequency, duration), **Then** the prescription is
   stored and made viewable to the patient on the web and, read-only, to the
   ClinicOwner (FR-082, FR-083).
5. **Given** a Visit contains diagnosis, notes, or a prescription, **When** a
   receptionist account views the same appointment, **Then** only booking-relevant
   fields are visible — no diagnosis, notes, or prescription content (FR-085).
6. **Given** any read of a patient's medical file, **When** that read occurs
   (by any authorized role), **Then** the system logs who accessed it and when
   (FR-087).

---

### User Story 4 - Patient: Manage Bookings Across Clinics (Priority: P2)

A patient searches for clinics, books or cancels appointments, books on behalf of a
registered dependent, and sees a single history of upcoming, completed, and cancelled
visits across every clinic they belong to.

**Why this priority**: Multi-clinic booking management is important for retention
and convenience but is not required for a single patient to experience the core
live-queue value in User Story 1.

**Independent Test**: Can be fully tested by having a patient search by specialty,
book an appointment, cancel it, book again on behalf of a dependent, and confirm all
three appear correctly categorized in the patient's history.

**Acceptance Scenarios**:

1. **Given** a patient wants to find care, **When** they search by specialty, name,
   or location, **Then** matching clinics are returned (FR-040).
2. **Given** a patient is self-service booking online, **When** they submit the
   booking, **Then** the appointment is created as Normal by default; the patient
   may optionally flag it as "possible urgent" for staff review, but only a
   Receptionist or Doctor can actually set the type to Urgent (FR-041).
3. **Given** a patient wants to pay ahead, **When** they choose the Phone/Deposit
   method, **Then** they may upload a transfer screenshot or choose to pay at the
   clinic, and the booking is confirmed only after receptionist manual confirmation
   (FR-042).
4. **Given** an upcoming appointment, **When** the patient cancels it, **Then** it
   moves to the Cancelled category in their history (FR-044, FR-045).
5. **Given** a guardian account has a registered dependent, **When** the guardian
   books on the dependent's behalf, **Then** the appointment is attributed to the
   dependent under the guardian's account (FR-046).
6. **Given** a patient belongs to multiple clinics, **When** they view their
   appointments, **Then** they see one unified list spanning all clinics they belong
   to (FR-006).

---

### User Story 5 - Receptionist: Handle Deposit Bookings and Day Closure with Carry-Over (Priority: P2)

A receptionist confirms deposit-based bookings, and at the end of the operating day
closes the session, reviews unserved patients, and carries them over to the front of
the next day's queue (or cancels them) — including automatic carry-over for
deposit-paid no-shows.

**Why this priority**: Day-closure and carry-over are essential for daily operations
but occur after the core live-queue flow (User Story 2) and can be validated
independently once a session has run.

**Independent Test**: Can be fully tested by running a session with at least one
unserved patient, closing the day, carrying that patient over, and confirming they
appear at the front of the next day's queue ahead of new bookings.

**Acceptance Scenarios**:

1. **Given** a patient booked via deposit with an uploaded screenshot or an intent to
   pay at the clinic, **When** the receptionist reviews the booking, **Then** they
   can manually confirm it (FR-042).
2. **Given** the operating day is ending, **When** the receptionist closes the day's
   session, **Then** the system lists all unserved patients at closure (FR-060,
   FR-061).
3. **Given** a list of unserved patients at closure, **When** the receptionist
   chooses to carry one or more over, **Then** those appointments move to the front
   of the next operating day's queue, ahead of new bookings, preserving their
   relative order from the day that just closed, and each patient is notified of
   their next-day position (FR-062, FR-063, FR-064).
4. **Given** a deposit-paid appointment where the patient did not show up, **When**
   the day closes, **Then** the system automatically carries that appointment over to
   the next operating day without requiring manual action (FR-065).

---

### User Story 6 - Doctor/Clinic Owner: Reports, Ratings, and Receptionist Management (Priority: P2)

A doctor/clinic owner reviews volume, absence-rate, peak-hours, and revenue reports,
sees aggregate patient ratings, and manages receptionist accounts for their clinic.

**Why this priority**: Reporting and staff management support running the business
but are not required for a single day's queue to function correctly.

**Independent Test**: Can be fully tested by running several days of queue activity
with some no-shows and post-visit ratings, then confirming the owner's reports
reflect accurate volume, absence rate, and aggregate rating, and that the owner can
invite a new receptionist account.

**Acceptance Scenarios**:

1. **Given** a period of clinic activity, **When** the clinic owner requests a
   volume report, **Then** daily/weekly/monthly patient volume is shown (FR-090).
2. **Given** a period of clinic activity, **When** the clinic owner requests an
   absence report, **Then** the no-show rate over that period is shown (FR-091).
3. **Given** historical booking data, **When** the clinic owner views peak analysis,
   **Then** peak days/hours are shown (FR-092).
4. **Given** deposit bookings over a period, **When** the clinic owner requests a
   revenue report, **Then** deposit revenue for that period is shown (FR-093).
5. **Given** a completed visit, **When** the patient submits a post-visit rating (1–5
   plus optional comment), **Then** it contributes to the clinic's aggregate rating
   shown in clinic search (FR-094, FR-095).
6. **Given** the clinic owner wants to add staff, **When** they invite a
   receptionist, **Then** a new receptionist account scoped to that clinic is created
   (FR-015).

---

### User Story 7 - Super Admin: Manage Clinics and Subscriptions (Priority: P2)

A platform super admin views, activates, or suspends any clinic, manages
subscription plans and their status, and is notified in advance of renewals, so the
platform business itself is operable at launch.

**Why this priority**: Required for the platform operator to run the business
(billing, clinic lifecycle) but not required for a single already-active clinic's
day-to-day queue operation.

**Independent Test**: Can be fully tested by suspending a clinic and confirming its
staff can no longer perform clinic operations, then reactivating it and confirming
normal operation resumes.

**Acceptance Scenarios**:

1. **Given** the platform has registered clinics, **When** the super admin views the
   clinic list, **Then** they can activate or suspend any clinic (FR-100).
2. **Given** a clinic's subscription, **When** the super admin manages its plan,
   **Then** status and renewal date are tracked (FR-101).
3. **Given** a clinic's subscription has expired, **When** any write operation is
   attempted for that clinic, **Then** it is restricted while read access is
   preserved (FR-102).
4. **Given** a subscription is 7 days from expiry, **When** that threshold is
   reached, **Then** the clinic owner is notified (FR-103).

---

### User Story 8 - Patient: View Medical History and Prescriptions (Priority: P3)

A patient views their own visit history and prescriptions for each clinic they
belong to, and maintains a per-clinic health profile (blood type, chronic
conditions, allergies, current medications).

**Why this priority**: Valuable for patient trust and continuity of care, but
secondary to the live-queue and booking flows that define the MVP wedge.

**Independent Test**: Can be fully tested by having a doctor record a visit and
prescription for a patient, then confirming the patient can view both under the
correct clinic in their own account.

**Acceptance Scenarios**:

1. **Given** a per-clinic PatientProfile exists, **When** the patient or treating
   doctor views it, **Then** blood type, chronic conditions, allergies, and current
   medications are shown (FR-080).
2. **Given** a patient has visit and prescription history at a clinic, **When** they
   view their history for that clinic, **Then** all past visits and prescriptions
   for that clinic are listed (FR-086).

---

### User Story 9 - Receptionist: Handle Repeated Absences (Priority: P3)

A receptionist sees when a patient has been absent multiple times without prior
cancellation and can restrict that patient's future booking ability.

**Why this priority**: A secondary safeguard against no-show abuse; useful but not
required for the MVP's primary flows to deliver value.

**Independent Test**: Can be fully tested by marking the same patient absent twice
without a cancellation on record and confirming the receptionist sees a flag and can
apply a booking restriction.

**Acceptance Scenarios**:

1. **Given** a patient has been marked absent at or beyond the clinic's configured
   repeated-absence threshold (default 2) without a prior cancellation, **When**
   the receptionist views that patient's record, **Then** a repeated-absence flag
   is shown and the receptionist may restrict future booking (FR-047).
2. **Given** a booking day arrives, **When** the morning of that day begins, **Then**
   the patient receives a reminder requesting confirmation (FR-048).

---

### User Story 10 - Super Admin: Platform Analytics and Data Export (Priority: P3)

A platform super admin reviews platform-wide usage and revenue analytics, and a
clinic can self-service export its own data.

**Why this priority**: Business-intelligence and data-portability features that
support the platform's growth but are not required for launch-day clinic operation.

**Independent Test**: Can be fully tested by generating platform analytics after
several clinics have activity, and by triggering a self-service export for one
clinic and confirming it contains that clinic's patients, appointments, and visits.

**Acceptance Scenarios**:

1. **Given** platform-wide clinic activity, **When** the super admin views platform
   analytics, **Then** usage and revenue across all clinics is shown (FR-104).
2. **Given** a clinic wants a copy of its data, **When** it triggers a self-service
   export, **Then** the export contains that clinic's patients, appointments, and
   visits only (FR-105).

---

### Edge Cases

- What happens when two receptionist actions confirm arrivals at the exact same
  instant? The system MUST serialize the assignment so no duplicate queue number is
  produced (FR-030; see Locked Decisions).
- What happens when a patient's JWT is tampered with to claim a different clinic?
  The request MUST be rejected and treated as a potential cross-tenant access
  attempt (FR-005; NFR-012).
- What happens when a deposit-paid patient never shows up and the day closes without
  receptionist action? The appointment MUST still auto-carry-over to the next
  operating day (FR-065).
- What happens when an urgent patient arrives but the 2:1 ratio has not yet been met
  by confirmed normal patients? The urgent patient MUST wait until the ratio is met
  rather than jumping the queue (FR-024; Locked Decisions).
- What happens when a clinic's subscription expires mid-operating-day? The
  already-open QueueSession runs to normal close (arrivals, calls, and numbering
  keep working); only new bookings and new session creation are blocked until
  renewal, and read access is preserved throughout (FR-102).
- What happens when a patient closes the browser tab? Web Push notifications
  (service worker) MUST still be able to reach them for supported browsers; the
  system does not silently lose the notification either way (FR-075, FR-077).
- What happens when a notification fails to deliver on first attempt? The system
  MUST retry up to 3 times with exponential backoff before marking it Failed
  (FR-076).
- What happens when a called patient never responds? Auto-absence triggers after the
  configured window and the queue advances without operator intervention (FR-029).
- What happens when a patient tries to book a second appointment at the same clinic
  for a day they already have an active appointment? The system MUST reject the
  duplicate booking — at most one active appointment per patient-of-record per
  clinic per day (FR-041a).

## Scope

### In Scope (MVP — Phase 1, Web)

- Multi-tenant foundation (single active clinic at launch, unlimited-tenant-ready
  schema and architecture)
- Clinic self-registration, per-clinic subdomain, and QR-code patient onboarding
- Email/phone + password authentication (no OTP)
- Smart queue: temporary number at booking → real number on arrival confirmation;
  normal/urgent appointment types with 2:1 injection ratio (configurable)
- Count-based patient view ("N patients ahead" plus "wait home" / "head to clinic")
- Emergency pause/resume with notifications
- End-of-day close with carry-over to the next operating day
- Instant Entry with a mandatory logged reason
- Repeated-absence warning and booking restriction
- Notifications via real-time in-app delivery (tab open) and Web Push (tab closed)
- Simplified patient medical file (text only — no attachments)
- E-prescription (text only)
- Reports: daily/weekly/monthly volume, absence rate, peak hours, deposit revenue
- Post-visit patient ratings and aggregate clinic rating
- Super Admin console: clinic and subscription management
- Arabic (RTL, default) and English, from day one

### Out of Scope (Phase 2+ — explicitly excluded from this feature)

- Native mobile app and mobile push notifications
- OTP-based authentication
- Medical file attachments (lab results, imaging)
- A chat-app notification channel as an alternative delivery method
- Online payment gateway (MVP uses manual deposit confirmation only)
- Insurance integration, telemedicine, and pharmacy/lab integration
- Tax-authority e-receipt integration

## Locked Decisions

These decisions were made during specification and MUST NOT be re-litigated or
silently changed by downstream planning or implementation work:

1. **Urgent injection ratio is 2:1, not front-of-line.** Urgent patients advance
   ahead of *present* normal patients on a rhythm of 2 confirmed-present normal
   patients to 1 present urgent patient. Urgent patients do not jump to the absolute
   front of the queue; they interleave on the ratio, which is configurable per
   clinic (default 2:1).
2. **Patients see a count, never a time estimate.** The patient-facing signal is
   always "N patients ahead" plus a binary "wait at home" / "head to clinic"
   instruction driven by a configurable threshold (default 4) — never a predicted
   wait time.
3. **Web session tokens are HttpOnly Secure cookies, never localStorage.** Combined
   with CSRF protection, this is the only accepted token storage mechanism for the
   web client.
4. **No OTP authentication in the MVP.** Authentication is email/phone plus password
   only (Argon2id or bcrypt cost ≥ 12); OTP is deferred to Phase 2.
5. **Real queue numbers are assigned only on arrival confirmation**, by arrival
   order — never at booking time (temporary numbers only at booking).
6. **Deposit bookings are manually confirmed**; there is no automated online payment
   gateway in the MVP.
7. **Delivery is web-first for every role** (patient, receptionist, doctor/owner,
   super admin); native mobile reuses the same backend in Phase 2.

## Requirements *(mandatory)*

### Functional Requirements

#### Multi-Tenancy & Onboarding
- **FR-001**: System MUST support clinic self-registration capturing name,
  specialty, contact information, and an admin account.
- **FR-002**: System MUST assign a unique tenant identifier that isolates all data
  belonging to a clinic.
- **FR-003**: System MUST support per-clinic branding (logo, primary/secondary
  color) on patient-facing screens.
- **FR-004**: System MUST support per-clinic operational settings: urgent ratio,
  "head to clinic" threshold, auto-absence timeout, working days/hours, timezone,
  and repeated-absence count threshold (default 2).
- **FR-005**: System MUST prevent any user from accessing another clinic's data,
  enforced at both the application and database layers.
- **FR-006**: System MUST allow a patient to belong to multiple clinics with a
  single unified appointment list, backed by one global patient identity
  (one email/phone + password login) with a separate clinic-scoped membership
  record per clinic; only membership and medical data are clinic-scoped, not the
  login itself.
- **FR-007**: System MUST expose a QR code per clinic linking to that clinic's
  subdomain registration page.

#### Authentication & Authorization
- **FR-010**: System MUST allow patients to register and log in with email or phone
  plus password.
- **FR-011**: System MUST hash passwords with Argon2id (or bcrypt cost ≥ 12).
- **FR-012**: System MUST lock an account for 15 minutes after 5 consecutive failed
  login attempts.
- **FR-013**: System MUST issue a short-lived access token (15 minutes) plus a
  rotating refresh token (30 days) stored hashed.
- **FR-014**: System MUST support role-based access control for Patient,
  Receptionist, Doctor, ClinicOwner, and SuperAdmin.
- **FR-015**: System MUST allow a Clinic Owner to invite and manage receptionist
  accounts.
- **FR-016**: System MUST store web session tokens in HttpOnly Secure cookies with
  CSRF protection, and MUST NOT use localStorage for token storage.

#### Smart Queue (Core)
- **FR-020**: System MUST auto-open a QueueSession per operating day and MUST block
  new bookings against a closed session.
- **FR-021**: System MUST assign a temporary number at booking time.
- **FR-022**: System MUST assign the real queue number only on receptionist arrival
  confirmation, by arrival order.
- **FR-023**: System MUST support Normal and Urgent appointment types.
- **FR-024**: System MUST insert a present Urgent patient after every 2
  confirmed-present Normal patients (ratio configurable per clinic). Urgent patients
  MUST interleave on the ratio and MUST NOT jump to the absolute front; already
  numbered patients MUST keep their relative order.
- **FR-025**: System MUST show each patient the count of patients ahead of them, and
  MUST NOT show an estimated wait time.
- **FR-026**: System MUST instruct "wait at home" when patients-ahead exceeds the
  configured threshold (default 4), and "head to clinic" when at or below it.
- **FR-027**: System MUST update queue position in real time without requiring a
  manual refresh.
- **FR-028**: System MUST allow a receptionist to call the next patient, changing
  their status to "Inside".
- **FR-029**: System MUST auto-mark a called patient absent after a configurable
  window (default 15 minutes) of no response, and MUST advance the queue.
- **FR-030**: System MUST prevent duplicate queue numbers under concurrent
  confirmation.
- **FR-031**: System MUST support Instant Entry, inserting a patient immediately
  only with a mandatory logged reason. Instant Entry MUST be ratio-neutral: it
  MUST NOT increment or reset the Normal/Urgent injection counter (FR-024), which
  MUST continue exactly as if the Instant Entry had not occurred.
- **FR-032**: System MUST record the number of companions at booking time.

#### Booking
- **FR-040**: System MUST allow a patient to search clinics by specialty, name, or
  location.
- **FR-041**: System MUST allow a patient to book for a given day as a Normal
  appointment by default; a patient MUST NOT be able to self-set the type to
  Urgent. A patient MAY optionally flag a booking as "possible urgent" for staff
  review, but only a Receptionist or Doctor MAY actually set an appointment's type
  to Urgent.
- **FR-041a**: System MUST allow at most one active (booked or present) appointment
  per patient-of-record, per clinic, per operating day, and MUST reject a duplicate
  same-day booking attempt. A registered Dependent is a distinct patient-of-record,
  so a guardian MAY hold one active appointment for themselves plus one per
  dependent on the same day.
- **FR-042**: System MUST support two booking methods: Walk-in, and
  Phone/Deposit (screenshot upload or pay-at-clinic, with manual receptionist
  confirmation).
- **FR-043**: System MUST allow a receptionist to create a booking on a patient's
  behalf.
- **FR-044**: System MUST allow a patient to cancel an upcoming appointment.
- **FR-045**: System MUST allow a patient to view their history as Upcoming /
  Completed / Cancelled.
- **FR-046**: System MUST allow booking on behalf of a registered Dependent under a
  guardian account.
- **FR-047**: System MUST flag a patient absent at or beyond the clinic's configured
  repeated-absence threshold (default 2) without prior cancellation, and allow a
  receptionist to restrict that patient's future booking.
- **FR-048**: System MUST send a morning reminder on the booking day requesting
  confirmation.

#### Emergency Management
- **FR-050**: System MUST allow a receptionist/doctor to pause the active session
  with a reason and timestamp.
- **FR-051**: System MUST notify all pending/confirmed patients of a pause while
  preserving their positions.
- **FR-052**: System MUST allow a doctor to resume a paused session, continuing
  exactly where it stopped.
- **FR-053**: System MUST notify all waiting patients on resume.
- **FR-054**: System MUST log every pause/resume event (user, time, reason).

#### End-of-Day & Carry-over
- **FR-060**: System MUST allow a receptionist to close the day's session.
- **FR-061**: System MUST list all unserved patients at closure.
- **FR-062**: System MUST allow a receptionist to carry over each unserved
  appointment to the next day, or cancel it.
- **FR-063**: System MUST notify affected patients of carry-over and their next-day
  position.
- **FR-064**: System MUST place carried-over patients at the front of the next
  day's queue, before new bookings. When multiple patients are carried over, the
  system MUST preserve their relative order from the previous day (the order they
  held when that day closed).
- **FR-065**: System MUST auto-carry-over deposit-paid no-shows to the next
  operating day.

#### Notifications (Web-First)
- **FR-070**: System MUST send a booking confirmation notification including the
  temporary number.
- **FR-071**: System MUST send a "head to clinic" notification when a patient's
  position crosses the configured threshold.
- **FR-072**: System MUST send a "your turn now" notification when a patient is
  called.
- **FR-073**: System MUST send pause/resume notifications (see FR-051, FR-053).
- **FR-074**: System MUST send a carry-over notification (see FR-063).
- **FR-075**: System MUST deliver notifications via real-time in-app delivery (tab
  open) and Web Push (tab closed) for supported browsers/platforms.
- **FR-076**: System MUST retry a failed notification delivery up to 3 times with
  exponential backoff.
- **FR-077**: System MUST guarantee no queue-position notification is silently lost,
  and MUST keep the notification dispatcher channel-agnostic so additional channels
  can be added without redesign.

#### Medical Records (text-only MVP)
- **FR-080**: System MUST maintain a per-clinic PatientProfile: blood type, chronic
  conditions, allergies, current medications.
- **FR-081**: System MUST allow only the treating Doctor to create or edit a Visit
  per appointment: diagnosis, notes. A ClinicOwner who is not the treating Doctor
  MUST have read-only access to the Visit for oversight.
- **FR-082**: System MUST allow only the treating Doctor to issue an e-prescription
  (drug, dose, frequency, duration). A ClinicOwner who is not the treating Doctor
  MUST have read-only access to the prescription.
- **FR-083**: System MUST make a prescription viewable to the patient on the web.
- **FR-084**: System MUST NOT support attachments in the MVP (deferred to Phase 2).
- **FR-085**: System MUST restrict read access to diagnosis/notes/prescriptions to
  the treating Doctor and the ClinicOwner; a receptionist MUST see
  booking-relevant information only.
- **FR-086**: System MUST allow a patient to view their own visit history and
  prescriptions per clinic.
- **FR-087**: System MUST log every medical-file access (who, what, when).

#### Reports & Ratings
- **FR-090**: System MUST report daily/weekly/monthly patient volume to the clinic
  owner.
- **FR-091**: System MUST report the no-show (absence) rate over a period.
- **FR-092**: System MUST report peak days/hours from historical data.
- **FR-093**: System MUST report revenue from deposit bookings over a period.
- **FR-094**: System MUST allow a post-visit patient rating (1–5 plus optional
  comment).
- **FR-095**: System MUST show an aggregate clinic rating in clinic search.

#### Super Admin
- **FR-100**: System MUST allow the super admin to view, activate, or suspend any
  clinic.
- **FR-101**: System MUST allow the super admin to manage subscription plans and
  track status plus renewal date.
- **FR-102**: System MUST restrict writes but preserve read access when a
  subscription expires. An already-open QueueSession MUST be allowed to run to its
  normal close — arrival confirmation, calling patients, and queue numbering MUST
  continue to work for that in-progress session — while NEW bookings and NEW
  QueueSession creation MUST be blocked until the subscription is renewed.
- **FR-103**: System MUST notify a clinic owner 7 days before subscription expiry.
- **FR-104**: System MUST provide platform-wide usage and revenue analytics to the
  super admin.
- **FR-105**: System MUST support clinic self-service data export (patients,
  appointments, visits).

### Key Entities

- **Clinic**: A tenant of the platform; owns branding, operational settings
  (urgent ratio, threshold, timezone), staff accounts, and all clinic-scoped data.
- **User**: A single global identity (one email/phone + password login) with one or
  more roles (Patient, Receptionist, Doctor, ClinicOwner, SuperAdmin). A Patient
  identity is platform-wide and links to a separate clinic-scoped membership record
  per clinic it belongs to; only the membership/medical data is clinic-scoped, not
  the login itself.
- **ClinicMembership**: The clinic-scoped link between a User and a Clinic carrying
  the role(s) that apply within that clinic; it is the tenant-scoping boundary for a
  user's clinic-specific data and may be Active or Restricted (booking restriction).
- **Dependent**: A person managed under a guardian's account (a distinct
  patient-of-record) so a guardian can book appointments on their behalf.
- **QueueSession**: One operating day's queue for a clinic; holds state such as the
  running normal/urgent counters and pause state.
- **Appointment**: A patient's booked or walk-in visit for a specific day; carries
  type (Normal/Urgent), method (Walk-in/Phone-Deposit), temporary number, real
  number (once assigned), and status (booked, present, inside, absent, completed,
  cancelled, carried-over).
- **PatientProfile**: Per-clinic medical summary for a patient (blood type, chronic
  conditions, allergies, current medications).
- **Visit**: A record of a completed appointment's clinical outcome — diagnosis and
  notes — tied to the treating doctor.
- **Prescription**: An e-prescription (drug, dose, frequency, duration) issued as
  part of a Visit and shared with the patient.
- **Rating**: A post-visit patient rating (1–5 plus optional comment) tied to an
  appointment, aggregated into the clinic's overall rating shown in clinic search.
- **OutboxNotification**: A durable record of a notification to be delivered,
  created in the same transaction as the triggering state change.
- **Subscription**: A clinic's platform plan, status, and renewal date, managed by
  the super admin.
- **AuditLog**: An immutable record of sensitive or state-changing actions (medical
  file access, Instant Entry, pause/resume, carry-over decisions) with actor,
  action, and timestamp.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A clinic can run a full operating day with zero manual (pen-and-paper
  or verbal) queue tracking.
- **SC-002**: A patient who books via QR code can watch their position update live,
  receive a "head to clinic" signal, arrive, and be called — completing the full
  journey without contacting the clinic by phone.
- **SC-003**: A receptionist can handle a mix of walk-ins, deposit bookings, and
  instant entries, and close the day with carry-over, in the same session without
  needing a secondary manual log.
- **SC-004**: When a doctor pauses the queue for an emergency, 100% of waiting
  patients are notified and every position holds unchanged on resume.
- **SC-005**: 100% of automated tenant-isolation tests pass — no clinic can read,
  write, or report on another clinic's data, including under a tampered access
  token.
- **SC-006**: Standard read requests complete in under 300ms at the 95th
  percentile.
- **SC-007**: A patient's queue-position update reaches their device within 2
  seconds of the triggering change, for both an open browser tab and a closed tab
  with notification permission granted.
- **SC-008**: The platform sustains at least 500 concurrent real-time connections
  per deployment without degradation.
- **SC-009**: Zero queue-position or status notifications are silently lost — every
  triggering event either delivers or is retried and eventually marked Failed with
  an alert, never disappears unnoticed.
- **SC-010**: The platform maintains at least 99.5% uptime during each clinic's
  configured operating hours, measured monthly.
- **SC-011**: No two patients are ever assigned the same real queue number within
  the same operating day, verified under concurrent-confirmation load testing.
- **SC-012**: All traffic between clients and the platform is encrypted in transit
  (TLS 1.2/1.3 only) — zero requests are servable over an unencrypted channel.
- **SC-013**: Login attempts are rate-limited to 10 per minute per IP address, and
  other sensitive operations are throttled to prevent abuse.
- **SC-014**: The architecture supports growth to at least 200 clinics on the same
  deployment without requiring a structural redesign.
- **SC-015**: Platform data is backed up daily, encrypted, and stored off-site, with
  a defined retention scheme (7 daily / 4 weekly / 6 monthly), and restore has been
  verified to work.

## Assumptions

- The MVP serves a single active clinic (Dr. Ghada Kamel, OB/GYN) at launch, with
  the data model and access-control architecture fully multi-tenant-ready for
  additional clinics without schema change.
- Arabic (RTL, default) and English are both required from day one; no other
  languages are in scope for the MVP.
- "Head to clinic" threshold (default 4) and auto-absence timeout (default 15
  minutes) are clinic-configurable settings, not hardcoded constants.
- Deposit payment verification (screenshot review or pay-at-clinic) is a manual
  receptionist judgment call in the MVP; there is no automated payment gateway or
  fraud detection.
- A guardian/dependent relationship is pre-registered before a guardian can book on
  a dependent's behalf; dependent self-registration is out of scope for this
  feature.
- "Supported" browsers/platforms for Web Push (tab-closed notifications) are
  Chrome/Edge/Firefox/Android and iOS 16.4+ as an installed PWA; devices/browsers
  outside this set only receive in-app (tab-open) notifications.
- Audit log entries are retained for a minimum of 2 years to satisfy the platform's
  compliance posture.
