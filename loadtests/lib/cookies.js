/**
 * Parse Set-Cookie headers from a k6 response.
 * Secure cookies are not stored by k6 against plain HTTP; we extract manually.
 */

export function extractCookie(response, name) {
  const headers = response.headers || {};
  // k6 may expose Set-Cookie as a string or (rarely) join multiples with commas.
  // Cookie values are JWTs / tokens without commas, so splitting on ", " is OK for names.
  const raw = headers["Set-Cookie"] || headers["set-cookie"] || "";
  const parts = Array.isArray(raw) ? raw : String(raw).split(/,(?=\s*[^;=]+=)/);

  for (const part of parts) {
    const trimmed = part.trim();
    if (trimmed.startsWith(`${name}=`)) {
      const value = trimmed.slice(name.length + 1).split(";")[0];
      return decodeURIComponent(value);
    }
  }
  return null;
}

export function bearerHeaders(token, extra = {}) {
  return Object.assign(
    {
      Accept: "application/json",
      Authorization: `Bearer ${token}`,
    },
    extra,
  );
}

export function bearerJsonHeaders(token) {
  return bearerHeaders(token, { "Content-Type": "application/json" });
}
