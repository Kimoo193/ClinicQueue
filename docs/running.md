# Running ClinicQueue

Two supported ways to run. Prefer the scripts below.

## Way 1 — Backend, then frontend

**Terminal 1 (backend):**

```bash
./scripts/run-backend.sh
```

Starts Postgres + Redis (Docker), then the API on **http://localhost:8080**. Migrations apply on API startup in Development.

**Terminal 2 (frontend):**

```bash
./scripts/run-frontend.sh
```

Opens Vite on **http://localhost:5173** and proxies `/api` + `/hubs` to port 8080.

## Way 2 — All services with one command

```bash
./docker/up.sh
```

Builds and starts nginx, api, web, postgres, redis, hangfire-worker. Open **https://localhost** (self-signed cert).

Equivalent manual command:

```bash
cd docker && docker compose up -d --build
```

Stop: `cd docker && docker compose down`

## Demo seed (recommended for manual QA + E2E)

Idempotent two-clinic demo (`ghada` + `smile`) with accounts password **`Test@1234`**:

```bash
./scripts/seed-demo.sh
```

Prints the account table and QR paths (`/r/ghada`, `/r/smile`). Uses the production three-role DB model (owner migrations, BYPASSRLS privileged writes, app-role tenant data).

### Playwright E2E (release gate S1–S9)

With API on `:8080` and Vite on `:5173` (or let the harness start them):

```bash
./scripts/run-e2e.sh
# or, if services are already up and seeded:
cd web && npm run test:e2e
```

Scenarios cover patient queue states, reception, urgent interleave, pause/resume, day close, role isolation, multi-clinic switcher, QR onboarding, and Arabic RTL. Failures are product findings — do not weaken assertions to go green.

## Load tests (k6)

HTTP load harness for auth, patient book/status poll, reception console reads (+ occasional call-next), and clinic discovery. Default profile is a short **smoke** run with failing latency/error thresholds.

```bash
# API must be up with relaxed rate limits (do not change production defaults in code)
CLINICQUEUE_RELAX_RATE_LIMIT=1 ./scripts/run-backend.sh
./scripts/seed-demo.sh

./scripts/run-load-test.sh          # smoke (default)
./scripts/run-load-test.sh load
./scripts/run-load-test.sh stress
```

Uses local `k6` if installed, otherwise Docker (`grafana/k6`). Details, env vars (`BASE_URL`, `VUS`, `DURATION`, …), auth/CSRF notes, and SignalR out-of-scope: [loadtests/README.md](../loadtests/README.md).

## Bootstrap (single clinic, no demo pack)

```bash
curl -s -X POST http://localhost:8080/api/v1/clinics \
  -H 'Content-Type: application/json' \
  -d '{"name":"Demo Clinic","specialty":"OB/GYN","contact":null,"adminEmail":"owner@example.com","adminPassword":"Password1!"}'
```

Response includes `subdomainSlug` — open `/r/{slug}` or log in as the admin email (do **not** paste clinic UUIDs into the SPA).

## Prerequisites

| Way | Need |
|-----|------|
| 1 | Docker (for DB/Redis), .NET 10 SDK, Node.js |
| 2 | Docker only |

Config details: [configuration.md](configuration.md). Architecture: [architecture.md](architecture.md).
