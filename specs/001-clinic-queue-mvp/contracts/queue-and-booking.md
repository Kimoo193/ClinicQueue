# Contract: Queue Module (Booking, Arrival, Numbering)

All endpoints under `/api/v1/`. `clinic_id` resolved from JWT, never from the
request. Real-time position updates are pushed via the `queue` SignalR hub, not
polled.

| Method | Path | Auth | Request | Response | FR(s) |
|---|---|---|---|---|---|
| GET | `/clinics/search` | Public | `?specialty=&name=&location=` | `[{ clinicId, name, specialty, aggregateRating }]` | FR-040 |
| POST | `/appointments` | Patient | `{ clinicId, day, method (Walkin/PhoneDeposit), companionsCount, possibleUrgentFlag?, dependentId? }` | `{ appointmentId, temporaryNumber }` — always created as `Normal` (Clarification Q2); rejects (`409`) a duplicate active same-day booking for the same patient-of-record (FR-041a) | FR-021, FR-032, FR-041, FR-041a, FR-042, FR-046, FR-070 |
| POST | `/appointments/on-behalf` | Receptionist | `{ clinicId, patientRef, day, type (Normal/Urgent), method, companionsCount }` | `{ appointmentId, temporaryNumber }` — same duplicate-active-same-day rejection applies (FR-041a) | FR-043, FR-041a |
| POST | `/appointments/{id}/cancel` | Patient/Receptionist | — | `{ appointmentId, status: Cancelled }` | FR-044 |
| GET | `/patients/me/appointments` | Patient | `?status=upcoming|completed|cancelled` | `[{ appointmentId, clinicId, status, temporaryNumber, realNumber?, countAhead? }]` | FR-045 |
| POST | `/appointments/{id}/confirm-arrival` | Receptionist | — | `{ appointmentId, realNumber, status: Present }` — assigned inside `SELECT ... FOR UPDATE` transaction on the session (constitution Principle II) | FR-022, FR-030 |
| PATCH | `/appointments/{id}/type` | Receptionist/Doctor | `{ type: Normal|Urgent }` | `{ appointmentId, type }` — only role permitted to set Urgent (Clarification Q2) | FR-023, FR-041 |
| POST | `/appointments/{id}/call-next` | Receptionist | — | `{ appointmentId, status: Inside }` → triggers "your turn now" notification | FR-028, FR-072 |
| POST | `/appointments/instant-entry` | Receptionist | `{ clinicId, patientRef, reason }` (reason mandatory) | `{ appointmentId, status: Present }` — ratio-neutral, does not touch the urgent counter (Clarification Q3) | FR-031 |
| GET | `/queue-sessions/{clinicId}/today` | Patient/Receptionist | — | `{ queueSessionId, status, normalSinceLastUrgent }` | FR-020 |
| GET (SignalR) | `queue` hub, group `clinic:{clinicId}` | Patient/Receptionist | — | Pushes `{ appointmentId, countAhead, instruction: waitHome|headToClinic }` on every position change | FR-025, FR-026, FR-027, FR-071 |

**Concurrency & ordering invariants** (constitution Principle II, enforced
server-side, not merely by convention):
- `RealNumber` assignment always happens inside a `SELECT ... FOR UPDATE`
  transaction on the owning `QueueSession`.
- Two simultaneous `confirm-arrival` calls against the same session MUST NOT
  produce duplicate `RealNumber`s (SC-011).
- Urgent insertion follows the 2:1 interleave (FR-024); Instant Entry never
  mutates `normalSinceLastUrgent` (Clarification Q3).
