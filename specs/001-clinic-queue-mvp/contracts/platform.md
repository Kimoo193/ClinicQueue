# Contract: Platform Module (Super Admin)

All endpoints under `/api/v1/`, SuperAdmin-only unless noted.

| Method | Path | Auth | Request | Response | FR(s) |
|---|---|---|---|---|---|
| GET | `/admin/clinics` | SuperAdmin | `?status=&page=&pageSize=` | `[{ clinicId, name, status, subscriptionStatus }]` (paginated) | FR-100 |
| POST | `/admin/clinics/{id}/activate` | SuperAdmin | — | `{ clinicId, status: Active }` | FR-100 |
| POST | `/admin/clinics/{id}/suspend` | SuperAdmin | — | `{ clinicId, status: Suspended }` | FR-100 |
| PATCH | `/admin/clinics/{id}/subscription` | SuperAdmin | `{ planName, renewalDate }` | `{ subscriptionId, status, renewalDate }` | FR-101 |
| GET | `/admin/analytics` | SuperAdmin | `?from=&to=` | `{ usage: {...}, revenue: {...} }` (platform-wide) | FR-104 |
| POST | `/clinics/{id}/export` | ClinicOwner | — | `{ exportJobId }` → async export of patients/appointments/visits for that clinic only | FR-105 |

**Automatic behaviors** (no endpoint — background jobs):
- Expired subscription restricts writes but preserves read access for that
  clinic (FR-102). An already-open `QueueSession` is exempt and runs to normal
  close — arrival confirmation, calling patients, and numbering keep working —
  while only new bookings and new `QueueSession` creation are blocked until
  renewal (FR-102, round-2 Clarification).
- Clinic owner notified 7 days before subscription renewal date (FR-103).
