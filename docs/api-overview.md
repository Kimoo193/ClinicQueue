# API overview

All versioned business routes hang under `/api/v{version}` with **v1** (`Program.cs`). Paths below are relative to `/api/v1` unless noted.

Auth: cookie JWT (`cq_access`) unless noted anonymous. Mutating calls from browsers need `X-CSRF-Token` matching `cq_csrf` (exceptions include anonymous auth register/login/refresh as configured in `CsrfMiddleware`).

For request/response field detail, also see `specs/001-clinic-queue-mvp/contracts/*.md`. If a contract disagrees with a route listed here, the **Map\*** registration in `src/ClinicQueue.Api/Endpoints` wins until contracts are updated.

## Unversioned

| Method | Path | Notes |
|--------|------|--------|
| GET | `/health` | Liveness JSON `{ status: "Healthy" }` |
| GET | `/health/ready` | Tagged readiness checks (DB/Redis as registered) |
| WS/HTTP | `/hubs/queue` | SignalR `QueueHub` |

OpenAPI document + Scalar UI: Development, or Production when `OpenApi:EnableUi=true`. Document JSON pattern `/openapi/{documentName}.json`.

## Versioned utility

| Method | Path | Notes |
|--------|------|--------|
| GET | `/api/v1/status` | Heartbeat `{ service, version, utc }` |

## Identity / clinics

| Method | Path | Auth (typical) |
|--------|------|----------------|
| POST | `/auth/login` | Anonymous + rate limit |
| POST | `/auth/register` | Anonymous + rate limit |
| POST | `/auth/refresh` | Anonymous + rate limit |
| POST | `/auth/switch-clinic` | Authenticated (re-issues a token scoped to another clinic the user is a member of) |
| POST | `/auth/logout` | Authenticated |
| POST | `/clinics` | Anonymous (creates clinic + admin) |
| PATCH | `/clinics/{clinicId}/branding` | ClinicOwner |
| PATCH | `/clinics/{clinicId}/settings` | ClinicOwner |
| GET | `/clinics/{clinicId}/qr-code` | ClinicOwner |
| GET | `/patients/me/clinics` | Patient |
| POST | `/clinics/{clinicId}/memberships/receptionists` | Owner staff mgmt |
| GET | `/clinics/{clinicId}/memberships/receptionists` | Owner staff mgmt |
| DELETE | `/clinics/{clinicId}/memberships/receptionists/{membershipId}` | Owner staff mgmt |
| GET | `/clinics/search` | Search clinics |

## Queue / booking

| Method | Path |
|--------|------|
| POST | `/appointments` |
| POST | `/appointments/on-behalf` |
| POST | `/appointments/{id}/cancel` |
| PATCH | `/appointments/{id}/type` |
| GET | `/patients/me/appointments` |
| POST | `/patients/me/dependents` |
| GET | `/patients/me/dependents` |
| POST | `/appointments/{id}/confirm-arrival` |
| POST | `/appointments/{id}/call-next` |
| POST | `/appointments/instant-entry` |
| GET | `/appointments/today` |
| POST | `/patients/{membershipId}/restrict-booking` |
| GET | `/queue-sessions/today` |
| POST | `/queue-sessions/{id}/pause` |
| POST | `/queue-sessions/{id}/resume` |
| POST | `/queue-sessions/{id}/close` |
| POST | `/appointments/{id}/carry-over` |
| POST | `/appointments/{id}/carry-over/cancel` |
| POST | `/appointments/{id}/confirm-deposit` |

## Medical

| Method | Path |
|--------|------|
| POST | `/appointments/{id}/visit` |
| GET | `/appointments/{id}/visit` |
| PATCH | `/visits/{id}` |
| POST | `/visits/{id}/prescription` |
| GET | `/patients/me/clinics/{clinicId}/visits` |
| GET | `/patients/{membershipId}/profile` |
| PATCH | `/patients/{membershipId}/profile` |

## Notifications

| Method | Path |
|--------|------|
| POST | `/push-subscriptions` |

## Reporting / ratings

| Method | Path |
|--------|------|
| GET | `/clinics/{clinicId}/reports/volume` |
| GET | `/clinics/{clinicId}/reports/absence-rate` |
| GET | `/clinics/{clinicId}/reports/peak-hours` |
| GET | `/clinics/{clinicId}/reports/revenue` |
| POST | `/appointments/{id}/rating` |
| GET | `/clinics/{clinicId}/rating` |

## Platform admin

| Method | Path |
|--------|------|
| GET | `/admin/clinics` |
| POST | `/admin/clinics/{id}/activate` |
| POST | `/admin/clinics/{id}/suspend` |
| PATCH | `/admin/clinics/{id}/subscription` |
| GET | `/admin/analytics` |
| POST | `/clinics/{id}/export` |
| GET | `/clinics/{id}/export/{jobId}` |

Exact authorization policies are on each `Map*` call (e.g. `Policies.Receptionist`, `Policies.SuperAdmin`). Prefer reading the endpoint file when debugging a `401`/`403`.
