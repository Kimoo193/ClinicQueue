# Contract: Medical Module

All endpoints under `/api/v1/`. Every read of a medical file is written to
`AuditLog` (FR-087) regardless of caller role.

| Method | Path | Auth | Request | Response | FR(s) |
|---|---|---|---|---|---|
| GET | `/patients/{membershipId}/profile` | Patient (self) / Doctor / ClinicOwner | — | `{ bloodType, chronicConditions, allergies, currentMedications }` | FR-080 |
| PATCH | `/patients/{membershipId}/profile` | Patient (self) | `{ bloodType?, chronicConditions?, allergies?, currentMedications? }` | Updated profile | FR-080 |
| POST | `/appointments/{id}/visit` | Doctor (treating only) | `{ diagnosis, notes }` | `{ visitId }` — write restricted to treating Doctor; ClinicOwner read-only (Clarification Q4) | FR-081 |
| GET | `/appointments/{id}/visit` | Doctor (treating) / ClinicOwner (read-only) | — | `{ visitId, diagnosis, notes }` — Receptionist gets 403, booking-relevant fields only via the booking endpoint | FR-081, FR-085 |
| POST | `/visits/{id}/prescription` | Doctor (treating only) | `{ drug, dose, frequency, duration }` | `{ prescriptionId }` — ClinicOwner read-only (Clarification Q4) | FR-082 |
| GET | `/patients/me/clinics/{clinicId}/visits` | Patient (self) | — | `[{ visitId, date, diagnosis, notes, prescription }]` | FR-083, FR-086 |

**Access matrix** (constitution Principle IV, Clarification Q4):

| Role | Visit/Prescription read | Visit/Prescription write |
|---|---|---|
| Patient (own record) | Yes | No |
| Receptionist | No (booking fields only) | No |
| Treating Doctor | Yes | Yes |
| ClinicOwner (not treating doctor) | Yes | No |
