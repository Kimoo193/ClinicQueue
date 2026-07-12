import { defineConfig, devices } from "@playwright/test";

/**
 * E2E release-gate scenarios (S1–S9). Requires:
 *   - API on http://localhost:8080
 *   - Vite on http://localhost:5173
 *   - Demo seed applied (`./scripts/seed-demo.sh`)
 *
 * Prefer `./scripts/run-e2e.sh` which seeds and starts services.
 */
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  reporter: [["list"], ["html", { open: "never", outputFolder: "playwright-report" }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://127.0.0.1:5173",
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    video: "off",
    locale: "en-US",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
});
