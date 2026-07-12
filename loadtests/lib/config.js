/**
 * Shared load-test configuration from environment variables.
 * Demo credentials match docs/running.md and scripts/seed-demo.sh.
 */

export function env(name, fallback) {
  const v = __ENV[name];
  if (v === undefined || v === null || v === "") {
    return fallback;
  }
  return v;
}

export function envInt(name, fallback) {
  const raw = env(name, String(fallback));
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) ? n : fallback;
}

export const config = {
  baseUrl: env("BASE_URL", "http://127.0.0.1:8080").replace(/\/$/, ""),
  password: env("LOADTEST_PASSWORD", "Test@1234"),
  clinicSlug: env("CLINIC_SLUG", "ghada"),
  profile: env("PROFILE", "smoke").toLowerCase(),

  // Demo seed accounts (docs/running.md)
  receptionEmail: env("RECEPTION_EMAIL", "reception@clinic.test"),
  patientEmails: env(
    "PATIENT_EMAILS",
    "patient1@clinic.test,patient2@clinic.test,patient3@clinic.test,patient4@clinic.test,urgent@clinic.test",
  )
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean),

  // Profile overrides (optional)
  vus: envInt("VUS", 0),
  duration: env("DURATION", ""),
};

export function apiUrl(path) {
  const p = path.startsWith("/") ? path : `/${path}`;
  return `${config.baseUrl}${p}`;
}

export function todayIsoDate() {
  // Clinic operating day is calendar date; load tests use the runner's local date.
  const d = new Date();
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, "0");
  const dd = String(d.getDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

export function patientEmailForVu(vuId) {
  const list = config.patientEmails;
  return list[(vuId - 1) % list.length];
}

/**
 * Build k6 options for smoke | load | stress.
 * Env VUS / DURATION override scenario sizing when set.
 */
export function buildOptions(profile) {
  const p = (profile || config.profile || "smoke").toLowerCase();

  const thresholds = {
    smoke: {
      http_req_failed: ["rate<0.02"],
      http_req_duration: ["p(95)<800", "p(99)<2000"],
      checks: ["rate>0.95"],
    },
    load: {
      http_req_failed: ["rate<0.05"],
      http_req_duration: ["p(95)<1500", "p(99)<4000"],
      checks: ["rate>0.90"],
    },
    stress: {
      http_req_failed: ["rate<0.10"],
      http_req_duration: ["p(95)<3000", "p(99)<8000"],
      checks: ["rate>0.80"],
    },
  };

  const thr = thresholds[p] || thresholds.smoke;

  // Shared scenario shapes; VUS/DURATION env can force a single constant-vus overlay.
  if (config.vus > 0 && config.duration) {
    return {
      thresholds: thr,
      scenarios: {
        mixed: {
          executor: "constant-vus",
          vus: config.vus,
          duration: config.duration,
          exec: "mixedScenario",
          gracefulStop: "10s",
        },
      },
    };
  }

  if (p === "load") {
    return {
      thresholds: thr,
      scenarios: {
        discovery: {
          executor: "constant-vus",
          vus: 5,
          duration: "2m",
          exec: "discoveryScenario",
          gracefulStop: "10s",
        },
        auth: {
          executor: "ramping-vus",
          startVUs: 2,
          stages: [
            { duration: "30s", target: 10 },
            { duration: "1m", target: 10 },
            { duration: "30s", target: 0 },
          ],
          exec: "authScenario",
          gracefulStop: "10s",
        },
        patient: {
          executor: "constant-vus",
          vus: 8,
          duration: "2m",
          exec: "patientScenario",
          startTime: "5s",
          gracefulStop: "10s",
        },
        reception: {
          executor: "constant-vus",
          vus: 3,
          duration: "2m",
          exec: "receptionScenario",
          startTime: "5s",
          gracefulStop: "10s",
        },
      },
    };
  }

  if (p === "stress") {
    return {
      thresholds: thr,
      scenarios: {
        discovery: {
          executor: "ramping-vus",
          startVUs: 5,
          stages: [
            { duration: "30s", target: 20 },
            { duration: "1m", target: 40 },
            { duration: "30s", target: 0 },
          ],
          exec: "discoveryScenario",
          gracefulStop: "15s",
        },
        auth: {
          executor: "ramping-vus",
          startVUs: 5,
          stages: [
            { duration: "30s", target: 25 },
            { duration: "1m", target: 40 },
            { duration: "30s", target: 0 },
          ],
          exec: "authScenario",
          gracefulStop: "15s",
        },
        patient: {
          executor: "ramping-vus",
          startVUs: 5,
          stages: [
            { duration: "30s", target: 20 },
            { duration: "1m", target: 35 },
            { duration: "30s", target: 0 },
          ],
          exec: "patientScenario",
          gracefulStop: "15s",
        },
        reception: {
          executor: "ramping-vus",
          startVUs: 2,
          stages: [
            { duration: "30s", target: 8 },
            { duration: "1m", target: 12 },
            { duration: "30s", target: 0 },
          ],
          exec: "receptionScenario",
          gracefulStop: "15s",
        },
      },
    };
  }

  // Default: smoke — short, local-friendly.
  // Auth uses 1 VU so a misconfigured (non-relaxed) limiter fails clearly
  // without drowning every other scenario in 429s; load/stress need RELAX=1.
  return {
    thresholds: thr,
    scenarios: {
      discovery: {
        executor: "constant-vus",
        vus: 2,
        duration: "20s",
        exec: "discoveryScenario",
        gracefulStop: "5s",
      },
      auth: {
        executor: "constant-vus",
        vus: 1,
        duration: "20s",
        exec: "authScenario",
        gracefulStop: "5s",
      },
      patient: {
        executor: "constant-vus",
        vus: 2,
        duration: "20s",
        exec: "patientScenario",
        gracefulStop: "5s",
      },
      reception: {
        executor: "constant-vus",
        vus: 1,
        duration: "20s",
        exec: "receptionScenario",
        gracefulStop: "5s",
      },
    },
  };
}
