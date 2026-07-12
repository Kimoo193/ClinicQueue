export type VisitResponse = {
  visitId: string;
  appointmentId: string;
  treatingDoctorUserId: string;
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

export type TodaySession = {
  queueSessionId: string;
  status: string;
  pauseReason?: string | null;
  pausedAt?: string | null;
};

export type DoctorAppointment = {
  appointmentId: string;
  patientMembershipId?: string | null;
  bookedByUserId: string;
  type: "Normal" | "Urgent" | string;
  method: string;
  status: string;
  temporaryNumber: number;
  realNumber?: number | null;
  isInstantEntry: boolean;
  possibleUrgentFlag: boolean;
  repeatedAbsenceFlag?: boolean;
  absenceCount?: number;
  membershipStatus?: string | null;
  depositConfirmed?: boolean;
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

export async function getTodaySession(): Promise<TodaySession | null> {
  const response = await fetch("/api/v1/queue-sessions/today", {
    credentials: "include",
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as TodaySession | null;
}

export async function listTodayAppointments(): Promise<DoctorAppointment[]> {
  const response = await fetch("/api/v1/appointments/today", {
    credentials: "include",
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as DoctorAppointment[];
}

export async function callNext(appointmentId: string): Promise<void> {
  const response = await fetch(
    `/api/v1/appointments/${appointmentId}/call-next`,
    {
      method: "POST",
      credentials: "include",
      headers: jsonHeaders(),
    },
  );
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
}

export async function pauseSession(
  sessionId: string,
  reason: string,
): Promise<{ queueSessionId: string; status: string; pausedAt: string }> {
  const response = await fetch(`/api/v1/queue-sessions/${sessionId}/pause`, {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
    body: JSON.stringify({ reason }),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as {
    queueSessionId: string;
    status: string;
    pausedAt: string;
  };
}

export async function resumeSession(
  sessionId: string,
): Promise<{ queueSessionId: string; status: string }> {
  const response = await fetch(`/api/v1/queue-sessions/${sessionId}/resume`, {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as { queueSessionId: string; status: string };
}

export async function getVisit(appointmentId: string): Promise<VisitResponse> {
  const response = await fetch(`/api/v1/appointments/${appointmentId}/visit`, {
    credentials: "include",
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as VisitResponse;
}

export async function createVisit(
  appointmentId: string,
  diagnosis: string,
  notes: string,
): Promise<{ visitId: string }> {
  const response = await fetch(`/api/v1/appointments/${appointmentId}/visit`, {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
    body: JSON.stringify({ diagnosis, notes }),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as { visitId: string };
}

export async function issuePrescription(
  visitId: string,
  payload: {
    drug: string;
    dose: string;
    frequency: string;
    duration: string;
  },
): Promise<{ prescriptionId: string }> {
  const response = await fetch(`/api/v1/visits/${visitId}/prescription`, {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
    body: JSON.stringify(payload),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as { prescriptionId: string };
}
