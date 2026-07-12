export type ClinicSettings = {
  urgentRatio: number;
  headToClinicThreshold: number;
  autoAbsenceTimeoutMinutes: number;
  repeatedAbsenceThreshold: number;
  workingHoursJson: string | null;
  timezone: string;
};

export type ClinicBranding = {
  logoUrl: string | null;
  primaryColor: string | null;
  secondaryColor: string | null;
};

export type ClinicSettingsResponse = {
  clinicId: string;
  name: string;
  subdomainSlug: string;
  settings: ClinicSettings;
};

export type ClinicBrandingResponse = {
  clinicId: string;
  branding: ClinicBranding;
};

export type ClinicQrResponse = {
  clinicId: string;
  subdomainSlug: string;
  qrCodeUrl: string | null;
};

function csrfToken(): string | undefined {
  if (typeof document === "undefined") {
    return undefined;
  }
  const match = document.cookie
    .split("; ")
    .find((row) => row.startsWith("cq_csrf="));
  return match ? decodeURIComponent(match.slice("cq_csrf=".length)) : undefined;
}

function jsonHeaders(): HeadersInit {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
  };
  const csrf = csrfToken();
  if (csrf) {
    headers["X-CSRF-Token"] = csrf;
  }
  return headers;
}

async function parseError(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { detail?: string; title?: string };
    return body.detail ?? body.title ?? response.statusText;
  } catch {
    return response.statusText;
  }
}

function mapSettings(raw: Record<string, unknown>): ClinicSettings {
  return {
    urgentRatio: Number(raw.urgentRatio ?? raw.UrgentRatio ?? 2),
    headToClinicThreshold: Number(
      raw.headToClinicThreshold ?? raw.HeadToClinicThreshold ?? 4,
    ),
    autoAbsenceTimeoutMinutes: Number(
      raw.autoAbsenceTimeoutMinutes ?? raw.AutoAbsenceTimeoutMinutes ?? 15,
    ),
    repeatedAbsenceThreshold: Number(
      raw.repeatedAbsenceThreshold ?? raw.RepeatedAbsenceThreshold ?? 2,
    ),
    workingHoursJson:
      (raw.workingHoursJson as string | null | undefined) ??
      (raw.WorkingHoursJson as string | null | undefined) ??
      null,
    timezone: String(raw.timezone ?? raw.Timezone ?? "Africa/Cairo"),
  };
}

function mapBranding(raw: Record<string, unknown>): ClinicBranding {
  return {
    logoUrl:
      (raw.logoUrl as string | null | undefined) ??
      (raw.LogoUrl as string | null | undefined) ??
      null,
    primaryColor:
      (raw.primaryColor as string | null | undefined) ??
      (raw.PrimaryColor as string | null | undefined) ??
      null,
    secondaryColor:
      (raw.secondaryColor as string | null | undefined) ??
      (raw.SecondaryColor as string | null | undefined) ??
      null,
  };
}

export async function getClinicSettings(
  clinicId: string,
): Promise<ClinicSettingsResponse> {
  const response = await fetch(`/api/v1/clinics/${clinicId}/settings`, {
    credentials: "include",
  });
  if (!response.ok) throw new Error(await parseError(response));
  const body = (await response.json()) as Record<string, unknown>;
  const settingsRaw = (body.settings ?? body.Settings ?? {}) as Record<
    string,
    unknown
  >;
  return {
    clinicId: String(body.clinicId ?? body.ClinicId ?? clinicId),
    name: String(body.name ?? body.Name ?? ""),
    subdomainSlug: String(body.subdomainSlug ?? body.SubdomainSlug ?? ""),
    settings: mapSettings(settingsRaw),
  };
}

export async function patchClinicSettings(
  clinicId: string,
  patch: Partial<{
    urgentRatio: number;
    headToClinicThreshold: number;
    autoAbsenceTimeoutMinutes: number;
    repeatedAbsenceThreshold: number;
    workingHoursJson: string | null;
    timezone: string;
  }>,
): Promise<ClinicSettings> {
  const response = await fetch(`/api/v1/clinics/${clinicId}/settings`, {
    method: "PATCH",
    credentials: "include",
    headers: jsonHeaders(),
    body: JSON.stringify(patch),
  });
  if (!response.ok) throw new Error(await parseError(response));
  const body = (await response.json()) as Record<string, unknown>;
  const settingsRaw = (body.settings ?? body.Settings ?? {}) as Record<
    string,
    unknown
  >;
  return mapSettings(settingsRaw);
}

export async function getClinicBranding(
  clinicId: string,
): Promise<ClinicBrandingResponse> {
  const response = await fetch(`/api/v1/clinics/${clinicId}/branding`, {
    credentials: "include",
  });
  if (!response.ok) throw new Error(await parseError(response));
  const body = (await response.json()) as Record<string, unknown>;
  const brandingRaw = (body.branding ?? body.Branding ?? {}) as Record<
    string,
    unknown
  >;
  return {
    clinicId: String(body.clinicId ?? body.ClinicId ?? clinicId),
    branding: mapBranding(brandingRaw),
  };
}

export async function patchClinicBranding(
  clinicId: string,
  patch: Partial<{
    logoUrl: string | null;
    primaryColor: string | null;
    secondaryColor: string | null;
  }>,
): Promise<ClinicBranding> {
  const response = await fetch(`/api/v1/clinics/${clinicId}/branding`, {
    method: "PATCH",
    credentials: "include",
    headers: jsonHeaders(),
    body: JSON.stringify(patch),
  });
  if (!response.ok) throw new Error(await parseError(response));
  const body = (await response.json()) as Record<string, unknown>;
  const brandingRaw = (body.branding ?? body.Branding ?? {}) as Record<
    string,
    unknown
  >;
  return mapBranding(brandingRaw);
}

export async function getClinicQr(clinicId: string): Promise<ClinicQrResponse> {
  const response = await fetch(`/api/v1/clinics/${clinicId}/qr-code`, {
    credentials: "include",
  });
  if (!response.ok) throw new Error(await parseError(response));
  const body = (await response.json()) as Record<string, unknown>;
  return {
    clinicId: String(body.clinicId ?? body.ClinicId ?? clinicId),
    subdomainSlug: String(body.subdomainSlug ?? body.SubdomainSlug ?? ""),
    qrCodeUrl:
      (body.qrCodeUrl as string | null | undefined) ??
      (body.QrCodeUrl as string | null | undefined) ??
      null,
  };
}
