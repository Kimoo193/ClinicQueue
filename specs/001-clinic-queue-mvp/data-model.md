# Phase 1 Data Model: Clinic Queue SaaS MVP (Web-First)

Entities extracted from `spec.md` Key Entities, Functional Requirements, and the
Clarifications session. Field lists are conceptual (attributes + types in
business terms), not a migration script — implementation detail belongs to the
Infrastructure layer during `/speckit-tasks` / implementation.

## Clinic

Tenant of the platform.

- `Id` (tenant identifier, referenced as `clinic_id` everywhere)
- `Name`, `Specialty`, `ContactInfo`
- `Branding`: logo, primary color, secondary color (FR-003)
- `Settings`: urgent ratio (default 2), head-to-clinic threshold (default 4),
  auto-absence timeout (default 15 min), repeated-absence count threshold
  (default 2), working days/hours, timezone (FR-004)
- `SubdomainSlug`, `QrCodeUrl` (FR-007)
- `Status` (Active / Suspended) — managed by SuperAdmin (FR-100)

**Relationships**: has many `ClinicMembership`, `QueueSession`, `Subscription`
(one active), `AuditLog` entries.

## User

A single global identity — **not** clinic-scoped (per Clarification Q1).

- `Id`, `Email`/`Phone` (login identifier, unique platform-wide), `PasswordHash`
  (Argon2id or bcrypt ≥12 — FR-011)
- `FailedLoginAttempts`, `LockedUntil` (FR-012 — lockout after 5 failures / 15 min)
- Global platform roles where applicable: `SuperAdmin` flag (platform-wide, not
  per clinic)

**Relationships**: has many `ClinicMembership` (one per clinic the user has a
role in); a `SuperAdmin` user typically has zero clinic memberships.

**Validation rules**: login identifier unique across the platform (not per
clinic); password never stored or logged in plaintext.

## ClinicMembership

Clinic-scoped link between a `User` and a `Clinic`, carrying the role(s) that
apply within that clinic. This is the tenant-scoping boundary for a `User`'s
role and clinic-specific data (per Clarification Q1) — the `User` identity
itself is not clinic-scoped, but every `ClinicMembership` row MUST carry
`ClinicId` and is subject to the three-layer tenant isolation (constitution
Principle I).

- `Id`, `ClinicId` (FK → Clinic), `UserId` (FK → User)
- `Role` (Patient / Receptionist / Doctor / ClinicOwner) — FR-014
- `InvitedBy` (for Receptionist accounts invited by a ClinicOwner — FR-015)
- `Status` (Active / Restricted) — set by receptionist for repeated-absence
  booking restriction (FR-047)

**Relationships**: belongs to one `Clinic` and one `User`; a Patient's
`ClinicMembership` links to that clinic's `PatientProfile`.

**Validation rules**: unique `(ClinicId, UserId, Role)`; every clinic-scoped
query filters by `ClinicId` at execution time (constitution Principle I).

## Dependent

A patient managed under a guardian's account (FR-046).

- `Id`, `GuardianUserId` (FK → User, the guardian), `ClinicId` (FK → Clinic —
  registered per clinic, consistent with `ClinicMembership`)
- `Name`, `DateOfBirth`, relationship-to-guardian label

**Relationships**: bookings made "on behalf of" a Dependent reference this
entity as the appointment's patient-of-record while `Appointment.BookedByUserId`
records the guardian.

**Validation rules**: a Dependent MUST be pre-registered before a guardian can
book on its behalf (per spec.md Assumptions); a Dependent belongs to exactly one
guardian per clinic.

## PatientProfile

Per-clinic medical summary (FR-080).

- `Id`, `ClinicMembershipId` (FK → ClinicMembership, Patient role)
- `BloodType`, `ChronicConditions`, `Allergies`, `CurrentMedications`

**Relationships**: one per `(Patient ClinicMembership)`; has many `Visit`.

## QueueSession

One operating day's queue for a clinic (FR-020).

- `Id`, `ClinicId`, `OperatingDate`
- `Status` (Open / Paused / Closed)
- `PauseReason`, `PausedAt`, `PausedBy` (FR-050, FR-054) — null when not paused
- `NormalSinceLastUrgent` (counter driving the 2:1 interleave, FR-024)
- `RowVersion` (concurrency token for the session row locked via
  `SELECT ... FOR UPDATE` during numbering — constitution Principle II)

**State transitions**: `Open` → `Paused` (FR-050) → `Open` (resume, FR-052) →
`Closed` (end-of-day, FR-060). A `Closed` session cannot accept new bookings
(FR-020) or further arrival confirmations.

**Relationships**: has many `Appointment` for that operating day.

## Appointment

A patient's booked or walk-in visit for a specific day.

- `Id`, `ClinicId`, `QueueSessionId`, `PatientRef` (either the booking
  `ClinicMembership` or a `Dependent`), `BookedByUserId`
- `Type` (Normal / Urgent) — Urgent settable only by Receptionist/Doctor
  (Clarification Q2); `PossibleUrgentFlag` (patient-submitted, advisory only)
- `Method` (Walk-in / Phone-Deposit)
- `TemporaryNumber` (assigned at booking, FR-021), `RealNumber` (assigned only
  on arrival confirmation, by arrival order — FR-022, nullable until assigned)
- `CompanionsCount` (FR-032)
- `Status`: `Booked` → `Present` (arrival confirmed) → `Inside` (called,
  FR-028) → `Completed`, or `Booked`/`Present` → `Absent` (FR-029/FR-047), or
  `Booked`/`Present` → `Cancelled` (FR-044), or `Absent`/unserved-at-close →
  `CarriedOver` (FR-062/FR-065) → re-enters next day's `QueueSession` at the
  front, ahead of new bookings; when several carry over together they keep their
  prior-day relative order (FR-064, round-2 Clarification)
- `IsInstantEntry` (bool), `InstantEntryReason` (mandatory when true — FR-031;
  ratio-neutral per Clarification Q3)
- `RowVersion` (optimistic concurrency — constitution Principle II)

**Relationships**: belongs to one `QueueSession`; has zero or one `Visit`.

**Validation rules**: `RealNumber` is immutable once assigned to a confirmed
patient (never renumbered — FR-024); `InstantEntryReason` required when
`IsInstantEntry = true`; `RealNumber` assignment MUST occur inside a
`SELECT ... FOR UPDATE` transaction on the owning `QueueSession`. At most one
active (`Booked`/`Present`) appointment may exist per patient-of-record, per
clinic, per operating day — a duplicate same-day booking MUST be rejected; a
`Dependent` is a distinct patient-of-record (FR-041a, round-2 Clarification).

## Visit

Clinical outcome of a completed appointment (FR-081).

- `Id`, `AppointmentId` (FK, unique), `TreatingDoctorUserId`
- `Diagnosis`, `Notes`

**Relationships**: has zero or one `Prescription`.

**Validation rules**: only the `TreatingDoctorUserId`'s own role may create/edit
(Clarification Q4); ClinicOwner has read-only access; Receptionist has no access
to this entity's clinical fields (FR-085). Every read is written to `AuditLog`
(FR-087).

## Prescription

E-prescription tied to a Visit (FR-082).

- `Id`, `VisitId` (FK), `Drug`, `Dose`, `Frequency`, `Duration`

**Validation rules**: same write/read restrictions as `Visit` (Clarification Q4);
viewable by the patient (FR-083).

## Rating

Post-visit patient feedback (FR-094).

- `Id`, `AppointmentId` (FK, unique), `Score` (1–5), `Comment` (optional)

**Relationships**: aggregates up to `Clinic.AggregateRating` shown in clinic
search (FR-095).

## OutboxNotification

Durable notification record (constitution Principle III).

- `Id`, `ClinicId`, `RecipientUserId`, `Type` (BookingConfirmation /
  HeadToClinic / YourTurn / PauseResume / CarryOver / …), `Payload`
- `Status` (Pending / Delivered / Failed), `Attempts`, `CreatedAt`,
  `LastAttemptAt`

**Validation rules**: MUST be created in the same transaction as the triggering
state change; dispatch retries ≤3× with exponential backoff before `Failed`
(FR-076); dispatcher is channel-agnostic (SignalR/WebPush now, FCM slot
reserved).

## Subscription

Clinic's platform plan (FR-101).

- `Id`, `ClinicId`, `PlanName`, `Status` (Active / Expired / Suspended),
  `RenewalDate`

**State transitions**: `Active` → `Expired` (past `RenewalDate`, restricts
writes but preserves reads — FR-102) → `Active` (renewed). On `Expired`, an
already-open `QueueSession` runs to its normal close — arrival confirmation,
calling patients, and numbering keep working for that in-progress session —
while new bookings and new `QueueSession` creation are blocked until renewal
(FR-102, round-2 Clarification). A notification fires 7 days before
`RenewalDate` (FR-103).

## AuditLog

Immutable record of sensitive/state-changing actions (FR-054, FR-087, and
general audit requirement).

- `Id`, `ClinicId`, `ActorUserId`, `Action`, `EntityType`, `EntityId`,
  `Timestamp`, `Metadata` (e.g., Instant Entry reason, pause/resume reason)

**Validation rules**: append-only (no update/delete path); retained ≥2 years
per spec.md Assumptions and the constitution's audit-logging requirement.

## Entity-Relationship Summary

```text
Clinic ──< ClinicMembership >── User
Clinic ──< QueueSession ──< Appointment >── Visit ── Prescription
Clinic ──< Subscription
ClinicMembership ──< PatientProfile
ClinicMembership ──< Dependent (via GuardianUserId)
Appointment ──< Rating
(any state change) ──> OutboxNotification
(any sensitive action) ──> AuditLog
```

Every entity except `User` carries `ClinicId` directly or transitively through
its parent (`QueueSession`, `ClinicMembership`, `Appointment`) and is subject to
the three-layer tenant isolation defense (constitution Principle I).
