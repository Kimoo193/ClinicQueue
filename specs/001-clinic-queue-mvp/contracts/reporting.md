# Contract: Reporting Module

All endpoints under `/api/v1/`, ClinicOwner-scoped unless noted.

| Method | Path | Auth | Request | Response | FR(s) |
|---|---|---|---|---|---|
| GET | `/clinics/{clinicId}/reports/volume` | ClinicOwner | `?period=daily|weekly|monthly&from=&to=` | `{ series: [{ date, count }] }` | FR-090 |
| GET | `/clinics/{clinicId}/reports/absence-rate` | ClinicOwner | `?from=&to=` | `{ absenceRate }` | FR-091 |
| GET | `/clinics/{clinicId}/reports/peak-hours` | ClinicOwner | `?from=&to=` | `{ peakDays: [...], peakHours: [...] }` | FR-092 |
| GET | `/clinics/{clinicId}/reports/revenue` | ClinicOwner | `?from=&to=` | `{ depositRevenue }` | FR-093 |
| POST | `/appointments/{id}/rating` | Patient (post-visit) | `{ score (1-5), comment? }` | `{ ratingId }` | FR-094 |
| GET | `/clinics/{clinicId}/rating` | Public | — | `{ aggregateRating }` — surfaced in `/clinics/search` (FR-040) | FR-095 |
