import { apiFetch, parseError } from "@/lib/api";

import type {
  AuthUserResponse,
  PublicClinicProfile,
} from "./types";

export async function loginRequest(
  emailOrPhone: string,
  password: string,
  clinicId?: string,
): Promise<AuthUserResponse> {
  const response = await apiFetch("/api/v1/auth/login", {
    method: "POST",
    skipUnauthorizedHandler: true,
    body: JSON.stringify({
      emailOrPhone,
      password,
      clinicId: clinicId ?? null,
    }),
  });

  if (!response.ok) {
    throw new Error(await parseError(response));
  }

  return (await response.json()) as AuthUserResponse;
}

export async function registerRequest(
  emailOrPhone: string,
  password: string,
  clinicId?: string,
): Promise<void> {
  const response = await apiFetch("/api/v1/auth/register", {
    method: "POST",
    skipUnauthorizedHandler: true,
    body: JSON.stringify({
      emailOrPhone,
      password,
      clinicId: clinicId ?? null,
    }),
  });

  if (!response.ok) {
    throw new Error(await parseError(response));
  }
}

export async function switchClinicRequest(
  clinicId: string,
): Promise<AuthUserResponse> {
  const response = await apiFetch("/api/v1/auth/switch-clinic", {
    method: "POST",
    body: JSON.stringify({ clinicId }),
  });

  if (!response.ok) {
    throw new Error(await parseError(response));
  }

  return (await response.json()) as AuthUserResponse;
}

export async function logoutRequest(): Promise<void> {
  const response = await apiFetch("/api/v1/auth/logout", {
    method: "POST",
    skipUnauthorizedHandler: true,
  });

  if (!response.ok && response.status !== 204) {
    throw new Error(await parseError(response));
  }
}

export async function refreshRequest(): Promise<boolean> {
  const response = await apiFetch("/api/v1/auth/refresh", {
    method: "POST",
    skipUnauthorizedHandler: true,
  });
  return response.ok;
}

export async function meRequest(): Promise<AuthUserResponse | null> {
  const response = await apiFetch("/api/v1/auth/me", {
    skipUnauthorizedHandler: true,
  });
  if (response.status === 401) {
    return null;
  }
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as AuthUserResponse;
}

export async function getClinicBySlug(
  slug: string,
): Promise<PublicClinicProfile | null> {
  const response = await apiFetch(
    `/api/v1/clinics/by-slug/${encodeURIComponent(slug)}`,
    { skipUnauthorizedHandler: true },
  );
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as PublicClinicProfile;
}

/** Authenticated user joins an active clinic as Patient (post bare-register / QR). */
export async function joinClinicAsPatient(
  clinicId: string,
): Promise<{ clinicId: string; clinicName: string; membershipId: string }> {
  const response = await apiFetch(`/api/v1/clinics/${clinicId}/join`, {
    method: "POST",
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as {
    clinicId: string;
    clinicName: string;
    membershipId: string;
  };
}
