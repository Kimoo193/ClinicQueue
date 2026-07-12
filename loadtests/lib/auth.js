import http from "k6/http";
import { check, sleep } from "k6";
import { apiUrl, config } from "./config.js";
import { extractCookie, bearerHeaders } from "./cookies.js";

/**
 * Login and return a Bearer access token.
 *
 * Why Bearer (not cookies): auth cookies are Secure=true. k6's cookie jar will
 * not store Secure cookies against http://localhost. Bearer skips CSRF too —
 * CsrfMiddleware only applies when the cq_access cookie is present.
 *
 * @returns {{ token: string, clinicId: string|null, userId: string|null } | null}
 */
export function login(email, password = config.password, opts = {}) {
  const tags = opts.tags || {
    endpoint: "auth_login",
    name: "POST /api/v1/auth/login",
  };

  const res = http.post(
    apiUrl("/api/v1/auth/login"),
    JSON.stringify({
      emailOrPhone: email,
      password,
    }),
    {
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      tags,
    },
  );

  if (res.status === 429) {
    check(res, {
      "login not rate-limited (set CLINICQUEUE_RELAX_RATE_LIMIT=1)": () => false,
    });
    return null;
  }

  const ok = check(res, {
    "login status 200": (r) => r.status === 200,
  });

  if (!ok) {
    return null;
  }

  const token = extractCookie(res, "cq_access");
  if (
    !check(token, {
      "login issued cq_access cookie": (t) => !!t,
    })
  ) {
    return null;
  }

  let clinicId = null;
  let userId = null;
  try {
    const body = res.json();
    clinicId = body.clinicId ?? body.ClinicId ?? null;
    userId = body.userId ?? body.UserId ?? null;
  } catch (_) {
    // body optional for callers that only need the token
  }

  return { token, clinicId, userId };
}

/**
 * Login with short backoff on 429 (helps smoke when the limiter is tight).
 * Still fails the check on 429 so missing RELAX_RATE_LIMIT is visible.
 */
export function loginWithBackoff(email, password = config.password, attempts = 3) {
  for (let i = 0; i < attempts; i++) {
    const session = login(email, password);
    if (session) {
      return session;
    }
    sleep(2 + i * 2);
  }
  return null;
}

/** GET /auth/me with Bearer token. */
export function me(token) {
  return http.get(apiUrl("/api/v1/auth/me"), {
    headers: bearerHeaders(token),
    tags: { endpoint: "auth_me", name: "GET /api/v1/auth/me" },
  });
}

/** Resolve public clinic profile by slug (anonymous). */
export function getClinicBySlug(slug = config.clinicSlug) {
  return http.get(apiUrl(`/api/v1/clinics/by-slug/${encodeURIComponent(slug)}`), {
    headers: { Accept: "application/json" },
    tags: {
      endpoint: "clinic_by_slug",
      name: "GET /api/v1/clinics/by-slug/{slug}",
    },
  });
}
