# Documentation index

| Document | Audience | Purpose |
|----------|----------|---------|
| [running.md](running.md) | Developers / ops | How to run locally and with Docker |
| [architecture.md](architecture.md) | Developers | Modules, tenancy, queue, notifications |
| [configuration.md](configuration.md) | Developers / ops | Config keys and environment variables |
| [api-overview.md](api-overview.md) | Developers | HTTP + SignalR surface map |
| [CLAUDE.md](CLAUDE.md) | Contributors / agents | Non-negotiable coding rules |
| [cloud-design-brief.md](cloud-design-brief.md) | Design / Cloud Design | BC visual regen brief + screen checklist + paste prompts |
| [../docker/BACKUP.md](../docker/BACKUP.md) | Ops | Encrypted backup / restore |
| [../docker/MONITORING.md](../docker/MONITORING.md) | Ops | Health monitor overlay |
| [../specs/001-clinic-queue-mvp/](../specs/001-clinic-queue-mvp/) | Product / eng | Spec, plan, tasks, contracts |

Spec contracts under `specs/001-clinic-queue-mvp/contracts/` describe intended API shapes. Prefer the live route map in [api-overview.md](api-overview.md) when code and contracts disagree — then file a drift fix.
