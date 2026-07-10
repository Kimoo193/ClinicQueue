# ClinicQueue

Multi-tenant web app for clinic queue management: patients book and track **count-ahead** position (not estimated wait time); reception runs arrival numbering, urgent interleave, and day close; doctors pause the queue and record visits; platform admins manage clinics and subscriptions.

**Stack (as implemented):** ASP.NET Core **10** Modular Monolith (`net10.0`), PostgreSQL 16, Redis 7, Hangfire, React 19 + Vite, Docker Compose + Nginx.

## Repository layout

| Path | Role |
|------|------|
| `src/ClinicQueue.Domain` | Domain entities and rules |
| `src/ClinicQueue.Application` | Use-case handlers, DTOs, FluentValidation |
| `src/ClinicQueue.Infrastructure` | EF Core, Identity tokens, Hangfire jobs, Redis/SignalR |
| `src/ClinicQueue.Api` | Minimal APIs, hubs, middleware |
| `web/` | React SPA (patient, reception, doctor, owner, admin) |
| `tests/` | Unit + integration (Testcontainers Postgres) |
| `docker/` | Compose, Nginx, Dockerfiles, backup/monitoring helpers |
| `specs/001-clinic-queue-mvp/` | Spec Kit: `spec.md`, `plan.md`, `tasks.md`, `contracts/` |
| `docs/` | Operator and contributor documentation |

## Quick start

### Way 1 — Backend first, then frontend (two terminals)

```bash
# Terminal 1 — API (+ Postgres/Redis in Docker)
chmod +x scripts/run-backend.sh scripts/run-frontend.sh   # once
./scripts/run-backend.sh
```

```bash
# Terminal 2 — React (Vite)
./scripts/run-frontend.sh
```

Open **http://localhost:5173**. API is on **http://localhost:8080**.

### Way 2 — Everything together (one command)

```bash
chmod +x docker/up.sh   # once
./docker/up.sh
```

Open **https://localhost** (accept the self-signed certificate warning).

Stop: `cd docker && docker compose down`

### First-time data

No seed DB. Create a clinic, then log in:

```bash
curl -s -X POST http://localhost:8080/api/v1/clinics \
  -H 'Content-Type: application/json' \
  -d '{"name":"Demo Clinic","specialty":"OB/GYN","contact":null,"adminEmail":"owner@example.com","adminPassword":"Password1!"}'
```

(With Docker/nginx use `https://localhost/api/v1/clinics` and `-k` for the self-signed cert.)

Paste the returned `clinicId` into the web UI clinic field, then log in as `owner@example.com`.

More detail: [docs/running.md](docs/running.md).

---

## Repository layout

```bash
dotnet test ClinicQueue.slnx
# or map quickstart scenarios 1–7:
bash docker/validate-quickstart.sh
```

CI: [`.github/workflows/ci.yml`](.github/workflows/ci.yml) (restore → build → format → test on `master`).

## Documentation

| Doc | Contents |
|-----|----------|
| [docs/running.md](docs/running.md) | Local + Docker run, bootstrap, ports |
| [docs/architecture.md](docs/architecture.md) | Modules, tenancy, outbox, numbering |
| [docs/configuration.md](docs/configuration.md) | Config keys and env vars |
| [docs/api-overview.md](docs/api-overview.md) | `/api/v1` route map |
| [docs/CLAUDE.md](docs/CLAUDE.md) | Contributor / agent non-negotiables |
| [specs/001-clinic-queue-mvp/](specs/001-clinic-queue-mvp/) | Requirements, plan, tasks, OpenAPI-oriented contracts |
| [PRODUCT.md](PRODUCT.md) / [DESIGN.md](DESIGN.md) | Product register and UI system |

## Auth model (web)

- Access / refresh / CSRF cookies: `cq_access`, `cq_refresh`, `cq_csrf` (see `TokenService`)
- State-changing requests send header `X-CSRF-Token` matching `cq_csrf`
- Live queue: SignalR hub `/hubs/queue`

## License / status

Private application repository. Spec Kit feature `001-clinic-queue-mvp` task list is complete; treat security/ops hardening (e.g. non-owner DB role + `FORCE ROW LEVEL SECURITY`) as follow-up work documented in review notes, not as finished guarantees.
