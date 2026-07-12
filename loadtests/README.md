# ClinicQueue load tests (k6)

HTTP load harness for the hot paths of the clinic queue API. Prefer **k6** for
scripts that are easy to run via Docker when the CLI is not installed.

## Prerequisites

1. API up on `BASE_URL` (default `http://127.0.0.1:8080`):

   ```bash
   CLINICQUEUE_RELAX_RATE_LIMIT=1 ./scripts/run-backend.sh
   ```

2. Demo seed (accounts `*@clinic.test`, password `Test@1234`):

   ```bash
   ./scripts/seed-demo.sh
   ```

3. **Rate limits:** production defaults throttle login (10/min/IP) and sensitive
   reception mutations (30/min). Load tests **must** run the API with
   `CLINICQUEUE_RELAX_RATE_LIMIT=1` (same flag as Playwright E2E). Do **not**
   change production defaults in code.

4. **k6:** local `k6` binary, or Docker image `grafana/k6` (used automatically
   by the runner when `k6` is missing).

## Quick start

```bash
# Smoke (default) — ~20s, low VUs, fails on p95 / error thresholds
./scripts/run-load-test.sh

# Named profiles
./scripts/run-load-test.sh smoke
./scripts/run-load-test.sh load
./scripts/run-load-test.sh stress

# Overrides
BASE_URL=http://127.0.0.1:8080 PROFILE=load VUS=20 DURATION=1m ./scripts/run-load-test.sh
```

## Scenarios

| Scenario   | What it hits |
|------------|----------------|
| discovery  | `GET /clinics/by-slug/{slug}`, `GET /clinics/search` |
| auth       | `POST /auth/login`, `GET /auth/me` |
| patient    | login → `POST /appointments` (201 or 409 OK) → poll `GET /patients/me/appointments` |
| reception  | login → `GET /appointments/today`, `GET /queue-sessions/today`, occasional `POST .../call-next` |

### Auth model

Browser sessions use HttpOnly cookies (`cq_access` / `cq_csrf`) with double-submit
CSRF. Cookies are `Secure=true`, so k6 against plain `http://` cannot use the
cookie jar reliably. These scripts:

1. Log in (patient/reception: **once per VU**, then reuse)
2. Read `cq_access` from `Set-Cookie`
3. Call authenticated APIs with `Authorization: Bearer <token>`

That matches integration-test style and **skips CSRF** (middleware only applies
when the access cookie is present).

The **auth** scenario still logs in on every iteration (login concurrency). Without
`CLINICQUEUE_RELAX_RATE_LIMIT=1` on the API, expect **429** and a failed run —
that is intentional.

### SignalR (out of scope for v1)

Live queue updates go through `/hubs/queue`. v1 load tests **poll** patient
appointments over HTTP instead. A dedicated SignalR scenario can be added later
if needed.

## Thresholds (fail the run)

| Profile | p95 latency | Failed HTTP rate | Checks pass rate |
|---------|-------------|------------------|------------------|
| smoke   | &lt; 800 ms   | &lt; 2%             | &gt; 95%            |
| load    | &lt; 1.5 s    | &lt; 5%             | &gt; 90%            |
| stress  | &lt; 3 s      | &lt; 10%            | &gt; 80%            |

`POST /appointments` returning **409** (duplicate active booking) is treated as
success. `call-next` may return 400/409/429 under contention — those are not
counted as hard failures.

## Environment variables

| Variable | Default | Meaning |
|----------|---------|---------|
| `BASE_URL` | `http://127.0.0.1:8080` | API origin |
| `PROFILE` | `smoke` | `smoke` \| `load` \| `stress` |
| `VUS` + `DURATION` | unset | If both set, single mixed scenario |
| `CLINIC_SLUG` | `ghada` | Public slug for discovery / clinic id |
| `LOADTEST_PASSWORD` | `Test@1234` | Demo password |
| `RECEPTION_EMAIL` | `reception@clinic.test` | Reception login |
| `PATIENT_EMAILS` | patient1–4,urgent | Comma-separated patient pool |
| `K6_IMAGE` | `grafana/k6:0.54.0` | Docker image when CLI missing |

## Interpreting results

- **PASS** — thresholds green; API handled the profile within latency/error budgets.
- **http_req_failed** — unexpected HTTP status (not 2xx). Investigate 401/403/5xx first.
- **429** on login/call-next — API likely missing `CLINICQUEUE_RELAX_RATE_LIMIT=1`.
- **setup() error** — API down or wrong `BASE_URL`.
- High **409** on book — normal when few demo patients already have today's booking;
  the write path is still exercised.

HTML/JSON summaries (when using the runner with `--out`):

```bash
./scripts/run-load-test.sh smoke --out json=loadtests/results/smoke.json
```

## Layout

```
loadtests/
  README.md          # this file
  main.js            # entry + scenarios
  lib/config.js
  lib/auth.js
  lib/cookies.js
  results/           # optional local outputs (gitignored)
scripts/run-load-test.sh
```
