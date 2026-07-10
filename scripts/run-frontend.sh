#!/usr/bin/env bash
# Terminal 2 — frontend (Vite on :5173, proxies /api and /hubs to :8080)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT/web"

if [[ ! -d node_modules ]]; then
  npm install
fi

echo "Web on http://localhost:5173 (API must already be on :8080)"
npm run dev
