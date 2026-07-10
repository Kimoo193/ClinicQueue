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

## Bootstrap

```bash
curl -s -X POST http://localhost:8080/api/v1/clinics \
  -H 'Content-Type: application/json' \
  -d '{"name":"Demo Clinic","specialty":"OB/GYN","contact":null,"adminEmail":"owner@example.com","adminPassword":"Password1!"}'
```

Use the returned `clinicId` in the SPA, then log in.

## Prerequisites

| Way | Need |
|-----|------|
| 1 | Docker (for DB/Redis), .NET 10 SDK, Node.js |
| 2 | Docker only |

Config details: [configuration.md](configuration.md). Architecture: [architecture.md](architecture.md).
