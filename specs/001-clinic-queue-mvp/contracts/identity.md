# Contract: Identity Module

All endpoints under `/api/v1/`. Auth column shows the minimum role required;
"Public" means no authentication. Every list endpoint is paginated (default 20,
max 100). Errors return ProblemDetails (RFC 7807).

| Method | Path | Auth | Request | Response | FR(s) |
|---|---|---|---|---|---|
| POST | `/clinics` | Public | `{ name, specialty, contact, adminEmail, adminPassword }` | `{ clinicId, subdomainSlug, qrCodeUrl }` | FR-001, FR-007 |
| PATCH | `/clinics/{clinicId}/branding` | ClinicOwner | `{ logoUrl?, primaryColor?, secondaryColor? }` | `{ clinicId, branding }` | FR-003 |
| GET | `/clinics/{clinicId}/settings` | ClinicOwner | — | `{ clinicId, name, subdomainSlug, settings }` | FR-004 |
| PATCH | `/clinics/{clinicId}/settings` | ClinicOwner | `{ urgentRatio?, headToClinicThreshold?, autoAbsenceTimeoutMinutes?, repeatedAbsenceThreshold?, workingHours?, timezone? }` | `{ clinicId, settings }` | FR-004 |
| GET | `/clinics/{clinicId}/branding` | ClinicOwner | — | `{ clinicId, branding }` | FR-003 |
| PATCH | `/clinics/{clinicId}/branding` | ClinicOwner | `{ logoUrl?, primaryColor?, secondaryColor? }` | `{ clinicId, branding }` | FR-003 |
| POST | `/auth/register` | Public | `{ emailOrPhone, password }` | `{ userId }` (global identity — Clarification Q1) | FR-006, FR-010 |
| POST | `/auth/login` | Public | `{ emailOrPhone, password }` | Sets HttpOnly Secure access + refresh cookies; `{ userId, roles: [...] }` | FR-010, FR-013, FR-016 |
| POST | `/auth/refresh` | Cookie (refresh) | — | Rotates refresh cookie, reissues access cookie | FR-013 |
| POST | `/auth/logout` | Cookie (access) | — | Clears cookies | FR-016 |
| POST | `/clinics/{clinicId}/memberships/receptionists` | ClinicOwner | `{ email, name }` | `{ membershipId, userId }` (invite) | FR-015 |
| GET | `/patients/me/clinics` | Patient | — | `[{ clinicId, membershipId, status }]` (unified list — Clarification Q1) | FR-006 |

**Cross-cutting**: `clinic_id` for any clinic-scoped call is resolved server-side
from the JWT/session, never accepted from the request body/query/path beyond
routing (constitution Principle I). Account lockout after 5 failed logins for 15
minutes (FR-012). Login rate-limited to 10/min/IP (SC-013).
