# Contract: Queue Module (Emergency Management, End-of-Day, Carry-Over)

All endpoints under `/api/v1/`.

| Method | Path | Auth | Request | Response | FR(s) |
|---|---|---|---|---|---|
| POST | `/queue-sessions/{id}/pause` | Receptionist/Doctor | `{ reason }` | `{ queueSessionId, status: Paused, pausedAt }` → notifies all pending/confirmed patients | FR-050, FR-051, FR-054 |
| POST | `/queue-sessions/{id}/resume` | Doctor | — | `{ queueSessionId, status: Open }` → notifies all waiting patients | FR-052, FR-053, FR-054 |
| POST | `/queue-sessions/{id}/close` | Receptionist | — | `{ queueSessionId, status: Closed, unservedAppointments: [...] }` | FR-060, FR-061 |
| POST | `/appointments/{id}/carry-over` | Receptionist | — | `{ appointmentId, status: CarriedOver, nextDayPosition }` → placed at the front of the next day's queue, before new bookings; when several patients carry over they keep their prior-day relative order; patient notified | FR-062, FR-063, FR-064 |
| POST | `/appointments/{id}/carry-over/cancel` | Receptionist | — | `{ appointmentId, status: Cancelled }` | FR-062 |

**Automatic behaviors** (no endpoint — background jobs, Hangfire):
- Deposit-paid no-shows auto-carry-over to the next operating day at close,
  without requiring a receptionist action (FR-065). Auto- and manually
  carried-over appointments together preserve their prior-day relative order at
  the front of the next day's queue (FR-064).
- A called (`Inside`-pending) patient who does not respond within the clinic's
  auto-absence window (default 15 min) is auto-marked `Absent` and the queue
  advances (FR-029).
- A patient absent at or beyond the clinic's configured repeated-absence
  threshold (default 2) without a prior cancellation is flagged for the
  receptionist to optionally restrict (FR-047).
