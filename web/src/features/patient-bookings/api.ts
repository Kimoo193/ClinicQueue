export type ClinicSearchResult = {
  clinicId: string;
  name: string;
  specialty: string;
  aggregateRating?: number | null;
  location?: string | null;
};

export type PatientAppointment = {
  appointmentId: string;
  clinicId: string;
  status: string;
  temporaryNumber: number;
  realNumber?: number | null;
  countAhead?: number | null;
  instruction?: "waitHome" | "headToClinic" | null;
  dependentId?: string | null;
};

export type Dependent = {
  dependentId: string;
  clinicId: string;
  name: string;
  dateOfBirth: string;
  relationshipToGuardian: string;
};

export type HistoryTab = "upcoming" | "completed" | "cancelled";

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

export async function searchClinics(params: {
  specialty?: string;
  name?: string;
  location?: string;
}): Promise<ClinicSearchResult[]> {
  const query = new URLSearchParams();
  if (params.specialty) query.set("specialty", params.specialty);
  if (params.name) query.set("name", params.name);
  if (params.location) query.set("location", params.location);

  const response = await fetch(`/api/v1/clinics/search?${query.toString()}`, {
    credentials: "include",
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as ClinicSearchResult[];
}

export async function listMyAppointments(
  status: HistoryTab = "upcoming",
): Promise<PatientAppointment[]> {
  const response = await fetch(
    `/api/v1/patients/me/appointments?status=${encodeURIComponent(status)}&unified=true`,
    { credentials: "include" },
  );
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as PatientAppointment[];
}

export async function cancelAppointment(appointmentId: string): Promise<void> {
  const response = await fetch(`/api/v1/appointments/${appointmentId}/cancel`, {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
}

export async function listDependents(clinicId?: string): Promise<Dependent[]> {
  const query = clinicId
    ? `?clinicId=${encodeURIComponent(clinicId)}`
    : "";
  const response = await fetch(`/api/v1/patients/me/dependents${query}`, {
    credentials: "include",
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as Dependent[];
}

export async function registerDependent(payload: {
  clinicId: string;
  name: string;
  dateOfBirth: string;
  relationshipToGuardian: string;
}): Promise<Dependent> {
  const response = await fetch("/api/v1/patients/me/dependents", {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
    body: JSON.stringify(payload),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  const body = (await response.json()) as {
    dependentId: string;
    name: string;
    dateOfBirth: string;
    relationshipToGuardian: string;
  };
  return {
    dependentId: body.dependentId,
    clinicId: payload.clinicId,
    name: body.name,
    dateOfBirth: body.dateOfBirth,
    relationshipToGuardian: body.relationshipToGuardian,
  };
}

export async function createAppointment(payload: {
  clinicId: string;
  day: string;
  method: "WalkIn" | "PhoneDeposit";
  companionsCount: number;
  possibleUrgentFlag?: boolean;
  dependentId?: string;
}): Promise<{ appointmentId: string; temporaryNumber: number }> {
  const response = await fetch("/api/v1/appointments", {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
    body: JSON.stringify(payload),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as {
    appointmentId: string;
    temporaryNumber: number;
  };
}
