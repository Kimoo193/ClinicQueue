/** Shared cookie/CSRF fetch helpers. Tokens stay in HttpOnly cookies — never localStorage. */

type UnauthorizedHandler = () => void;

let onUnauthorized: UnauthorizedHandler | null = null;

export function setUnauthorizedHandler(handler: UnauthorizedHandler | null): void {
  onUnauthorized = handler;
}

export function readCsrfToken(): string | undefined {
  if (typeof document === "undefined") {
    return undefined;
  }
  const match = document.cookie
    .split("; ")
    .find((row) => row.startsWith("cq_csrf="));
  return match ? decodeURIComponent(match.slice("cq_csrf=".length)) : undefined;
}

export function jsonHeaders(): HeadersInit {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
  };
  const csrf = readCsrfToken();
  if (csrf) {
    headers["X-CSRF-Token"] = csrf;
  }
  return headers;
}

export async function parseError(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { detail?: string; title?: string };
    return body.detail ?? body.title ?? response.statusText;
  } catch {
    return response.statusText;
  }
}

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

export type ApiFetchOptions = RequestInit & {
  /** When true, a 401 does not clear session / redirect (login bootstrap, credential checks). */
  skipUnauthorizedHandler?: boolean;
};

export async function apiFetch(
  input: string,
  init: ApiFetchOptions = {},
): Promise<Response> {
  const { skipUnauthorizedHandler, ...requestInit } = init;
  const headers = new Headers(requestInit.headers);
  if (requestInit.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  const csrf = readCsrfToken();
  if (csrf && !headers.has("X-CSRF-Token")) {
    headers.set("X-CSRF-Token", csrf);
  }

  const response = await fetch(input, {
    ...requestInit,
    credentials: "include",
    headers,
  });

  if (response.status === 401 && !skipUnauthorizedHandler) {
    onUnauthorized?.();
  }

  return response;
}
