# Quickstart: Validating the Clinic Queue SaaS MVP

This is a runnable validation guide, not an implementation reference — see
`data-model.md` for entity shapes and `contracts/` for endpoint details.

## Prerequisites

- Docker Compose stack running locally: `nginx`, `api` (ASP.NET Core 10),
  `postgres` (16, migrations applied), `redis` (7), `hangfire-worker`.
- At least one seeded `Clinic` (e.g., a local "Dr. Ghada Kamel" clinic) with an
  open `QueueSession` for today, urgent ratio 2, head-to-clinic threshold 4,
  auto-absence timeout 15 minutes.
- Three test accounts: a Patient, a Receptionist, and a Doctor/ClinicOwner, each
  with an active `ClinicMembership` on the seeded clinic.

## Scenario 1 — Core live queue (User Story 1 + 2, P1)

Proves the product's core differentiator end to end.

1. As the Patient, call `POST /api/v1/appointments` for today with
   `method: Walkin`. **Expect**: `201` with a `temporaryNumber` and a
   `BookingConfirmation` notification received (via the `queue` SignalR hub or
   Web Push).
2. As the Receptionist, call `POST /api/v1/appointments/{id}/confirm-arrival`.
   **Expect**: `200` with a `realNumber` assigned by arrival order.
3. Repeat steps 1–2 for two more Normal patients, then book and confirm one
   Urgent patient's arrival. **Expect**: per FR-024, the Urgent patient is
   inserted immediately after the 2nd confirmed Normal patient — not appended
   to the back, not jumped to the front.
4. As the Patient from step 1, poll `GET /api/v1/patients/me/appointments` (or
   observe the `queue` hub push). **Expect**: `countAhead` decreases in real
   time as patients ahead are confirmed/absent, and the `instruction` field
   flips from `waitHome` to `headToClinic` when `countAhead <= 4` — never a
   time estimate (Locked Decision #2).
5. As the Receptionist, call `POST /api/v1/appointments/{id}/call-next` for the
   first confirmed patient. **Expect**: status becomes `Inside` and a
   `YourTurn` notification is received (FR-072).

**Pass condition**: SC-002, SC-007, SC-011 all hold — live update within 2s,
correct urgent interleave, no duplicate real numbers.

## Scenario 2 — Concurrency guarantee (constitution Principle II release gate)

1. Book two Normal patients (temporary numbers only).
2. Fire two `POST /api/v1/appointments/{id}/confirm-arrival` requests for both
   patients concurrently (e.g., parallel HTTP calls with no artificial delay).
3. **Expect**: both requests succeed with two *distinct* `realNumber` values —
   never a duplicate, even though both hit the same `QueueSession` at the same
   instant (SC-011). This is the automated concurrency test that gates the
   Queue module's Definition of Done.

## Scenario 3 — Emergency pause/resume (User Story 3, P1)

1. With several patients waiting, as the Receptionist call
   `POST /api/v1/queue-sessions/{id}/pause` with a reason.
   **Expect**: all pending/confirmed patients receive a `Pause` notification;
   `GET` on each appointment shows unchanged `realNumber`/position.
2. As the Doctor, call `POST /api/v1/queue-sessions/{id}/resume`.
   **Expect**: a `Resume` notification to all waiting patients; the next
   `call-next` continues exactly from where it stopped (SC-004).

## Scenario 4 — Medical record least-privilege (User Story 3, Clarification Q4)

1. As the treating Doctor, call `POST /api/v1/appointments/{id}/visit` with a
   diagnosis and notes, then `POST /api/v1/visits/{id}/prescription`.
   **Expect**: `201` on both.
2. As the ClinicOwner (not the treating doctor on this appointment), call
   `GET /api/v1/appointments/{id}/visit`. **Expect**: `200` with full clinical
   content (read-only) — but a `PATCH`/write attempt on the same resource is
   rejected (`403`).
3. As the Receptionist, call the same `GET`. **Expect**: `403`, or a response
   containing booking-relevant fields only, no diagnosis/notes/prescription
   content (FR-085).
4. Inspect `AuditLog` for the clinic. **Expect**: one entry per read in steps
   2–3, recording actor, action, and timestamp (FR-087).

## Scenario 5 — Tenant isolation release gate (constitution Principle I)

1. As a user with a valid session on Clinic A, attempt
   `GET /api/v1/patients/{clinicBMembershipId}/profile` for a membership that
   belongs to Clinic B. **Expect**: `403`/`404` — never Clinic B's data.
2. Manually tamper a decoded JWT's `clinic_id` claim to Clinic B's id (re-sign
   with an invalid signature, or attempt to pass `clinic_id` as a query
   parameter instead). **Expect**: request rejected — `clinic_id` is only ever
   read from the verified JWT (FR-005), never request input.

**Pass condition**: 100% of isolation tests pass — this is the release gate
required by SC-005 and constitution Principle I.

## Scenario 6 — Day close and carry-over (User Story 5, P2)

1. Leave at least two confirmed patients unserved at end of day, in a known
   order (e.g., #5 then #6).
2. As the Receptionist, call `POST /api/v1/queue-sessions/{id}/close`.
   **Expect**: response lists all unserved patients (FR-061).
3. Call `POST /api/v1/appointments/{id}/carry-over` for both of them.
   **Expect**: on the next operating day's `QueueSession`, both appointments are
   at the front, ahead of any new bookings (FR-064), in the same relative order
   they held when the previous day closed (#5 before #6), and each patient
   received a `CarryOver` notification with their next-day position (FR-063).

## Scenario 7 — Booking and subscription guardrails (round-2 clarifications)

1. As the Patient, book an appointment at a clinic for today, then attempt a
   second booking at the same clinic for the same day. **Expect**: the second
   request is rejected (`409`) — at most one active appointment per
   patient-of-record per clinic per day (FR-041a). Booking for a registered
   dependent on the same day still succeeds (distinct patient-of-record).
2. Set the clinic's subscription to expired while a `QueueSession` is already
   open. **Expect**: `confirm-arrival` and `call-next` still succeed for that
   in-progress session, but a NEW `POST /api/v1/appointments` and creating the
   next day's session are blocked until renewal, while all reads keep working
   (FR-102).
