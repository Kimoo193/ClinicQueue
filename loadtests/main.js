import http from "k6/http";
import { check, sleep } from "k6";
import { Rate } from "k6/metrics";
import {
  buildOptions,
  config,
  apiUrl,
  todayIsoDate,
  patientEmailForVu,
} from "./lib/config.js";
import { login, loginWithBackoff, me, getClinicBySlug } from "./lib/auth.js";
import { bearerHeaders, bearerJsonHeaders } from "./lib/cookies.js";

/**
 * ClinicQueue HTTP load tests (k6).
 *
 * Profiles: smoke (default) | load | stress
 * Env: BASE_URL, PROFILE, VUS, DURATION, CLINIC_SLUG, PATIENT_EMAILS, …
 *
 * SignalR (/hubs/queue) is out of scope for v1 — HTTP poll of appointment status
 * covers the patient live-status path.
 *
 * Patient/reception scenarios reuse a per-VU Bearer session (realistic). Auth
 * scenario still hammers login — run the API with CLINICQUEUE_RELAX_RATE_LIMIT=1.
 */

export const options = buildOptions(config.profile);

const businessErrorRate = new Rate("business_errors");

/** Per-VU session cache (each VU has its own JS isolate). */
let vuSession = null;
let vuEmail = null;

function think(min = 0.3, max = 1.2) {
  sleep(min + Math.random() * (max - min));
}

function ensureSession(email) {
  if (vuSession && vuEmail === email) {
    return vuSession;
  }
  const session = loginWithBackoff(email);
  if (session) {
    vuSession = session;
    vuEmail = email;
  }
  return session;
}

/** Public clinic discovery (by-slug + light search). */
export function discoveryScenario() {
  const bySlug = getClinicBySlug(config.clinicSlug);
  const slugOk = check(bySlug, {
    "by-slug 200": (r) => r.status === 200,
    "by-slug has clinicId": (r) => {
      try {
        const b = r.json();
        return !!(b.clinicId || b.ClinicId);
      } catch (_) {
        return false;
      }
    },
  });
  businessErrorRate.add(!slugOk);

  const search = http.get(
    apiUrl(`/api/v1/clinics/search?name=${encodeURIComponent(config.clinicSlug)}`),
    {
      headers: { Accept: "application/json" },
      tags: { endpoint: "clinic_search", name: "GET /api/v1/clinics/search" },
    },
  );
  const searchOk = check(search, {
    "search 200": (r) => r.status === 200,
  });
  businessErrorRate.add(!searchOk);

  think(0.2, 0.8);
}

/** Concurrent login + /me (login every iteration — needs relaxed rate limits). */
export function authScenario() {
  const email = patientEmailForVu(__VU);
  const session = login(email);
  const loggedIn = !!session;
  businessErrorRate.add(!loggedIn);
  if (!session) {
    // Back off hard on rate limit so we do not burn the window further.
    think(3, 6);
    return;
  }

  const meRes = me(session.token);
  const meOk = check(meRes, {
    "me 200": (r) => r.status === 200,
  });
  businessErrorRate.add(!meOk);

  think(0.5, 1.5);
}

/**
 * Patient queue join + status poll.
 * POST /appointments may return 409 (duplicate active booking) under concurrency —
 * that is an expected domain response and counts as success for the write path.
 */
export function patientScenario() {
  const email = patientEmailForVu(__VU);
  const session = ensureSession(email);
  if (!session) {
    businessErrorRate.add(true);
    think(2, 4);
    return;
  }

  let clinicId = session.clinicId;
  if (!clinicId) {
    const profile = getClinicBySlug(config.clinicSlug);
    if (profile.status === 200) {
      try {
        const b = profile.json();
        clinicId = b.clinicId || b.ClinicId || null;
      } catch (_) {
        clinicId = null;
      }
    }
  }

  if (!clinicId) {
    check(null, { "resolved clinic id": () => false });
    businessErrorRate.add(true);
    return;
  }

  const book = http.post(
    apiUrl("/api/v1/appointments"),
    JSON.stringify({
      clinicId,
      day: todayIsoDate(),
      method: "WalkIn",
      companionsCount: 0,
      possibleUrgentFlag: false,
    }),
    {
      headers: bearerJsonHeaders(session.token),
      tags: {
        endpoint: "appointments_create",
        name: "POST /api/v1/appointments",
      },
    },
  );

  const bookOk = check(book, {
    "book 201 or 409": (r) => r.status === 201 || r.status === 409,
  });
  businessErrorRate.add(!bookOk);

  // Poll live status (HTTP stand-in for SignalR)
  for (let i = 0; i < 2; i++) {
    const list = http.get(
      apiUrl("/api/v1/patients/me/appointments?status=upcoming&unified=true"),
      {
        headers: bearerHeaders(session.token),
        tags: {
          endpoint: "patient_appointments",
          name: "GET /api/v1/patients/me/appointments",
        },
      },
    );
    const listOk = check(list, {
      "appointments list 200": (r) => r.status === 200,
    });
    businessErrorRate.add(!listOk);
    think(0.4, 0.9);
  }
}

/**
 * Reception console reads + occasional call-next.
 * call-next is rate-limited (sensitive); keep mutation share low and tolerate
 * 400/409 when the queue has nothing callable.
 */
export function receptionScenario() {
  const session = ensureSession(config.receptionEmail);
  if (!session) {
    businessErrorRate.add(true);
    think(2, 4);
    return;
  }

  const today = http.get(apiUrl("/api/v1/appointments/today"), {
    headers: bearerHeaders(session.token),
    tags: {
      endpoint: "reception_today",
      name: "GET /api/v1/appointments/today",
    },
  });
  const todayOk = check(today, {
    "today 200": (r) => r.status === 200,
  });
  businessErrorRate.add(!todayOk);

  const sessionRes = http.get(apiUrl("/api/v1/queue-sessions/today"), {
    headers: bearerHeaders(session.token),
    tags: {
      endpoint: "queue_session_today",
      name: "GET /api/v1/queue-sessions/today",
    },
  });
  const sessionOk = check(sessionRes, {
    "queue session 200": (r) => r.status === 200,
  });
  businessErrorRate.add(!sessionOk);

  // ~25% of iterations attempt call-next on a Present appointment
  if (todayOk && Math.random() < 0.25) {
    let appointments = [];
    try {
      const body = today.json();
      appointments = Array.isArray(body) ? body : body.items || [];
    } catch (_) {
      appointments = [];
    }

    const present = appointments.find((a) => {
      const status = a.status || a.Status;
      return status === "Present";
    });

    if (present) {
      const id = present.appointmentId || present.AppointmentId;
      const call = http.post(
        apiUrl(`/api/v1/appointments/${id}/call-next`),
        null,
        {
          headers: bearerJsonHeaders(session.token),
          tags: {
            endpoint: "call_next",
            name: "POST /api/v1/appointments/{id}/call-next",
          },
        },
      );
      // 200 success; 400/409 domain conflict; 429 rate limit under stress
      const callOk = check(call, {
        "call-next acceptable": (r) =>
          r.status === 200 ||
          r.status === 400 ||
          r.status === 409 ||
          r.status === 429,
      });
      businessErrorRate.add(!callOk);
    }
  }

  think(0.5, 1.5);
}

/**
 * Used when VUS + DURATION override a single mixed scenario.
 */
export function mixedScenario() {
  const roll = Math.random();
  if (roll < 0.25) {
    discoveryScenario();
  } else if (roll < 0.45) {
    authScenario();
  } else if (roll < 0.8) {
    patientScenario();
  } else {
    receptionScenario();
  }
}

export function setup() {
  const health = http.get(apiUrl("/health"), {
    tags: { endpoint: "health", name: "GET /health" },
  });
  const status = http.get(apiUrl("/api/v1/status"), {
    tags: { endpoint: "status", name: "GET /api/v1/status" },
  });

  const ready =
    check(health, { "API /health ok": (r) => r.status === 200 }) &&
    check(status, { "API /status ok": (r) => r.status === 200 });

  if (!ready) {
    throw new Error(
      `API not reachable at ${config.baseUrl}. Start with CLINICQUEUE_RELAX_RATE_LIMIT=1 ./scripts/run-backend.sh (and seed with ./scripts/seed-demo.sh).`,
    );
  }

  return {
    baseUrl: config.baseUrl,
    profile: config.profile,
    clinicSlug: config.clinicSlug,
  };
}
