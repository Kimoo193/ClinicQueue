export type VolumeReport = {
  series: Array<{ date: string; count: number }>;
};

export type AbsenceRateReport = {
  absenceRate: number;
};

export type PeakHoursReport = {
  peakDays: string[];
  peakHours: number[];
};

export type RevenueReport = {
  depositRevenue: number;
};

export type ReceptionistMembership = {
  membershipId: string;
  userId: string;
  emailOrPhone: string;
  status: string;
  createdAt: string;
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

function reportQuery(from?: string, to?: string, period?: string): string {
  const params = new URLSearchParams();
  if (from) params.set("from", `${from}T00:00:00.000Z`);
  if (to) params.set("to", `${to}T23:59:59.999Z`);
  if (period) params.set("period", period);
  const qs = params.toString();
  return qs ? `?${qs}` : "";
}

export async function getVolumeReport(
  clinicId: string,
  opts: { from?: string; to?: string; period?: string },
): Promise<VolumeReport> {
  const response = await fetch(
    `/api/v1/clinics/${clinicId}/reports/volume${reportQuery(opts.from, opts.to, opts.period)}`,
    { credentials: "include" },
  );
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as VolumeReport;
}

export async function getAbsenceRateReport(
  clinicId: string,
  opts: { from?: string; to?: string },
): Promise<AbsenceRateReport> {
  const response = await fetch(
    `/api/v1/clinics/${clinicId}/reports/absence-rate${reportQuery(opts.from, opts.to)}`,
    { credentials: "include" },
  );
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as AbsenceRateReport;
}

export async function getPeakHoursReport(
  clinicId: string,
  opts: { from?: string; to?: string },
): Promise<PeakHoursReport> {
  const response = await fetch(
    `/api/v1/clinics/${clinicId}/reports/peak-hours${reportQuery(opts.from, opts.to)}`,
    { credentials: "include" },
  );
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as PeakHoursReport;
}

export async function getRevenueReport(
  clinicId: string,
  opts: { from?: string; to?: string },
): Promise<RevenueReport> {
  const response = await fetch(
    `/api/v1/clinics/${clinicId}/reports/revenue${reportQuery(opts.from, opts.to)}`,
    { credentials: "include" },
  );
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as RevenueReport;
}

export async function listReceptionists(
  clinicId: string,
): Promise<ReceptionistMembership[]> {
  const response = await fetch(
    `/api/v1/clinics/${clinicId}/memberships/receptionists`,
    { credentials: "include" },
  );
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as ReceptionistMembership[];
}

export async function inviteReceptionist(
  clinicId: string,
  payload: { email: string; name: string },
): Promise<{ membershipId: string; userId: string; name: string }> {
  const response = await fetch(
    `/api/v1/clinics/${clinicId}/memberships/receptionists`,
    {
      method: "POST",
      credentials: "include",
      headers: jsonHeaders(),
      body: JSON.stringify(payload),
    },
  );
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as {
    membershipId: string;
    userId: string;
    name: string;
  };
}

export async function revokeReceptionist(
  clinicId: string,
  membershipId: string,
): Promise<void> {
  const response = await fetch(
    `/api/v1/clinics/${clinicId}/memberships/receptionists/${membershipId}`,
    {
      method: "DELETE",
      credentials: "include",
      headers: jsonHeaders(),
    },
  );
  if (!response.ok) throw new Error(await parseError(response));
}
