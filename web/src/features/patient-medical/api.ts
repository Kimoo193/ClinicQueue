export type MyClinicMembership = {
  clinicId: string;
  membershipId: string;
  status: string;
};

export type PatientProfile = {
  membershipId: string;
  bloodType?: string | null;
  chronicConditions?: string | null;
  allergies?: string | null;
  currentMedications?: string | null;
};

export type PatientVisit = {
  visitId: string;
  date: string;
  diagnosis: string;
  notes: string;
  prescription?: {
    prescriptionId: string;
    drug: string;
    dose: string;
    frequency: string;
    duration: string;
  } | null;
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

export async function listMyClinics(): Promise<MyClinicMembership[]> {
  const response = await fetch("/api/v1/patients/me/clinics", {
    credentials: "include",
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as MyClinicMembership[];
}

export async function getPatientProfile(
  membershipId: string,
): Promise<PatientProfile> {
  const response = await fetch(`/api/v1/patients/${membershipId}/profile`, {
    credentials: "include",
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as PatientProfile;
}

export async function updatePatientProfile(
  membershipId: string,
  payload: {
    bloodType?: string;
    chronicConditions?: string;
    allergies?: string;
    currentMedications?: string;
  },
): Promise<PatientProfile> {
  const response = await fetch(`/api/v1/patients/${membershipId}/profile`, {
    method: "PATCH",
    credentials: "include",
    headers: jsonHeaders(),
    body: JSON.stringify(payload),
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as PatientProfile;
}

export async function listMyVisits(clinicId: string): Promise<PatientVisit[]> {
  const response = await fetch(
    `/api/v1/patients/me/clinics/${clinicId}/visits`,
    { credentials: "include" },
  );
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as PatientVisit[];
}
