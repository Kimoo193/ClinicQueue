export type AdminClinic = {
  clinicId: string;
  name: string;
  status: string;
  subscriptionStatus?: string | null;
  planName?: string | null;
  renewalDate?: string | null;
};

export type AdminClinicListResponse = {
  items: AdminClinic[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export type SubscriptionResponse = {
  subscriptionId: string;
  status: string;
  renewalDate: string;
  planName: string;
};

export type PlatformAnalytics = {
  usage: {
    clinicCount: number;
    activeClinicCount: number;
    appointmentCount: number;
    visitCount: number;
    patientProfileCount: number;
    queueSessionCount: number;
  };
  revenue: {
    confirmedDepositCount: number;
    activeSubscriptionCount: number;
    expiredSubscriptionCount: number;
  };
  from?: string | null;
  to?: string | null;
};

export type ClinicExportStartResponse = {
  exportJobId: string;
  status: string;
};

export type ClinicExportPayload = {
  clinicId: string;
  exportedAtUtc: string;
  patients: unknown[];
  appointments: unknown[];
  visits: unknown[];
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

export async function listAdminClinics(
  status?: string,
): Promise<AdminClinicListResponse> {
  const params = new URLSearchParams();
  if (status) {
    params.set("status", status);
  }
  const qs = params.toString();
  const response = await fetch(
    `/api/v1/admin/clinics${qs ? `?${qs}` : ""}`,
    { credentials: "include" },
  );
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as AdminClinicListResponse;
}

export async function activateClinic(
  clinicId: string,
): Promise<{ clinicId: string; status: string }> {
  const response = await fetch(`/api/v1/admin/clinics/${clinicId}/activate`, {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as { clinicId: string; status: string };
}

export async function suspendClinic(
  clinicId: string,
): Promise<{ clinicId: string; status: string }> {
  const response = await fetch(`/api/v1/admin/clinics/${clinicId}/suspend`, {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as { clinicId: string; status: string };
}

export async function updateSubscription(
  clinicId: string,
  planName: string,
  renewalDate: string,
): Promise<SubscriptionResponse> {
  const response = await fetch(
    `/api/v1/admin/clinics/${clinicId}/subscription`,
    {
      method: "PATCH",
      credentials: "include",
      headers: jsonHeaders(),
      body: JSON.stringify({ planName, renewalDate }),
    },
  );
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as SubscriptionResponse;
}

export async function getPlatformAnalytics(params?: {
  from?: string;
  to?: string;
}): Promise<PlatformAnalytics> {
  const search = new URLSearchParams();
  if (params?.from) {
    search.set("from", params.from);
  }
  if (params?.to) {
    search.set("to", params.to);
  }
  const qs = search.toString();
  const response = await fetch(
    `/api/v1/admin/analytics${qs ? `?${qs}` : ""}`,
    { credentials: "include" },
  );
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as PlatformAnalytics;
}

export async function startClinicExport(
  clinicId: string,
): Promise<ClinicExportStartResponse> {
  const response = await fetch(`/api/v1/clinics/${clinicId}/export`, {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as ClinicExportStartResponse;
}

export async function getClinicExport(
  clinicId: string,
  exportJobId: string,
): Promise<ClinicExportPayload> {
  const response = await fetch(
    `/api/v1/clinics/${clinicId}/export/${exportJobId}`,
    { credentials: "include" },
  );
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as ClinicExportPayload;
}
