# ClinicQueue web

React 19 + TypeScript + Vite SPA for ClinicQueue.

## Scripts

```bash
npm install
npm run dev      # http://localhost:5173 — proxies /api and /hubs to :8080
npm run build
npm run lint
```

Proxy config: `vite.config.ts`. Run the API on port **8080** in Development (see [docs/running.md](../docs/running.md)).

## Features

Under `src/features/`: `patient-queue`, `patient-bookings`, `patient-medical`, `reception-console`, `reception-dayclose`, `doctor-console`, `owner-reports`, `admin-console`.

i18n: `src/locales/en.json`, `ar.json`. Design tokens: `src/index.css` (`cq-*`). Product/design notes: repo root `PRODUCT.md`, `DESIGN.md`.
