# Demo accounts

Every account below is created by `./scripts/seed-demo.sh` (source: `tools/ClinicQueue.Seeder/DemoSeeder.cs`). The seed is idempotent — re-run it any time to reset today's queue back to this exact state.

**Password for every account: `Test@1234`**

## Run it

```bash
./scripts/run-backend.sh    # API on :8080 (Postgres + Redis via Docker)
./scripts/seed-demo.sh      # creates/reset the accounts and data below
```

Then either:

- **Open the web app** — `./scripts/run-frontend.sh`, go to http://localhost:5173, sign in with any email/password pair below.
- **Or watch every account work over the API** — `./scripts/demo-users.sh`. It logs in as all eleven accounts and makes one real, role-appropriate call per account (reception's live queue, the doctor's consultation screen, each patient's count-ahead instruction, the SuperAdmin's clinic list, ...), printing the actual JSON responses. See [Run the accounts over the API](#run-the-accounts-over-the-api) below for prerequisites.

## Accounts

| Email | Role | Clinic | Scenario |
|---|---|---|---|
| `admin@clinicqueue.app` | SuperAdmin | — (platform) | Platform-wide; manages every clinic's activation/subscription |
| `ghada@clinic.test` | ClinicOwner | Dr. Ghada Kamel Clinic | Branding, settings, reports |
| `doctor@clinic.test` | Doctor | Dr. Ghada Kamel Clinic | Doctor console + medical records |
| `reception@clinic.test` | Receptionist | Dr. Ghada Kamel Clinic | Reception console + day close |
| `patient1@clinic.test` | Patient | Dr. Ghada Kamel Clinic | Waiting, 7 ahead → **WAIT AT HOME** |
| `patient2@clinic.test` | Patient | Dr. Ghada Kamel Clinic | Waiting, 2 ahead → **HEAD TO CLINIC** |
| `patient3@clinic.test` | Patient | Dr. Ghada Kamel Clinic | Currently **Inside** with the doctor |
| `patient4@clinic.test` | Patient | Dr. Ghada Kamel Clinic | Booked by reception (deposit, temp number only), not arrived → arrival confirmation flow |
| `urgent@clinic.test` | Patient | Dr. Ghada Kamel Clinic | `Urgent` type, confirmed present (2:1 interleave demo) |
| `multi@clinic.test` | Receptionist **+** Patient | Ghada **+** Smile Dental | The multi-clinic switcher account — two different roles in two different clinics |
| `newbie@clinic.test` | Patient (no membership) | — (none) | Registered but has not joined any clinic → sent to `/find-clinic` on login |

Two clinics exist:

| Clinic | Slug | QR / join link |
|---|---|---|
| Dr. Ghada Kamel Clinic (OB/GYN) | `ghada` | `/r/ghada` |
| Smile Dental Clinic | `smile` | `/r/smile` |

Dr. Ghada's clinic also carries 60 days of closed-session history (~20–35 patients/day, absences, deposits, ratings) so reports and analytics have real data to show.

## Logging in as each one

**Web app:** go to http://localhost:5173, enter the email and `Test@1234`.

- Every account except `multi@clinic.test` has exactly one clinic membership, so login lands you straight in the right console.
- `multi@clinic.test` has two memberships (Receptionist at Ghada, Patient at Smile) — after password entry the app prompts you to pick a clinic; picking one issues a token scoped to that clinic's role only. Use the in-app clinic switcher to hop to the other.
- `newbie@clinic.test` has zero memberships — login succeeds but there's no clinic context yet, so the app routes it to `/find-clinic` (join by slug/QR).

**API (curl):**

```bash
curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"emailOrPhone":"ghada@clinic.test","password":"Test@1234"}' \
  -c cookies.txt
```

Auth is cookie-based (`cq_access`, `cq_refresh`, `cq_csrf` — see the root `README.md` auth model section and `docs/api-overview.md`), so reuse `-c/-b cookies.txt` across calls. For an account with more than one membership (`multi@clinic.test`), pass `"clinicId"` in the login body to pick which one to authenticate into:

```bash
-d '{"emailOrPhone":"multi@clinic.test","password":"Test@1234","clinicId":"11111111-1111-1111-1111-111111111111"}'
```

(`11111111-…` = Ghada, `22222222-…` = Smile — stable ids, see `DemoSeeder.cs`.)

## Run the accounts over the API

`./scripts/demo-users.sh` automates the curl flow above for all eleven accounts and exercises one real endpoint per role, so you can see what each login can actually do without opening the browser eleven times:

```bash
./scripts/demo-users.sh
```

It needs `curl` and `jq`, and the API reachable at `http://localhost:8080` (override with `BASE_URL=... ./scripts/demo-users.sh`, e.g. `BASE_URL=https://localhost` for the `docker/up.sh` stack).

The script does 12 logins in a few seconds, and the production default rate limit on `/auth/login` is 10/minute per IP — so a strict run can `429` near the end. If that happens, restart the API with the same relax flag used for k6 load tests:

```bash
CLINICQUEUE_RELAX_RATE_LIMIT=1 ./scripts/run-backend.sh
./scripts/seed-demo.sh
./scripts/demo-users.sh
```

What it shows, per account:

| Account | Call made |
|---|---|
| `admin@clinicqueue.app` | `GET /api/v1/admin/clinics` — every clinic on the platform |
| `ghada@clinic.test` | `GET /api/v1/clinics/{id}/settings` — urgent ratio, thresholds, timezone |
| `reception@clinic.test` | `GET /api/v1/appointments/today` — the live queue |
| `doctor@clinic.test` | `GET /api/v1/appointments/{id}/consultation` — the consultation screen for whoever is currently Inside |
| `patient1`–`patient4`, `urgent` | `GET /api/v1/patients/me/appointments` — their own status, count-ahead, and instruction |
| `multi@clinic.test` | Logs in twice (once per clinic) and calls the Receptionist endpoint at Ghada, then the Patient endpoint at Smile |
| `newbie@clinic.test` | Login succeeds with no roles/clinic; a Patient-only call (`GET /api/v1/patients/me/clinics`) is attempted and correctly gets `403` — proof role gating works before onboarding |

## Notes

- `patient4`'s "my appointments" call returns `[]`. That's not a bug: the appointment was booked *on their behalf* by reception (a phone-deposit booking), so it belongs to `BookedByUserId=reception`, not the patient. It's visible in the reception `today` list instead (temp number 12, status `Booked`). This is the seeded scenario for the arrival-confirmation flow.
- Re-running `./scripts/seed-demo.sh` wipes and rebuilds all of the above (matched by email domain `@clinic.test` / `@seed.local` and `admin@clinicqueue.app`) — safe to run repeatedly.
