export type TodaySession = {
  queueSessionId: string;
  status: string;
  pauseReason?: string | null;
  pausedAt?: string | null;
};

export type ReceptionAppointment = {
  appointmentId: string;
  patientMembershipId?: string | null;
  bookedByUserId: string;
  type: string;
  method: string;
  status: string;
  temporaryNumber: number;
  realNumber?: number | null;
  isInstantEntry: boolean;
  possibleUrgentFlag: boolean;
  companionsCount: number;
  depositConfirmed?: boolean;
};

export type UnservedAppointment = {
  appointmentId: string;
  patientMembershipId?: string | null;
  dependentId?: string | null;
  type: string;
  method: string;
  status: string;
  temporaryNumber: number;
  realNumber?: number | null;
  depositConfirmed: boolean;
};

export type CloseDayResponse = {
  queueSessionId: string;
  status: string;
  autoCarriedOverCount: number;
  unservedAppointments: UnservedAppointment[];
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

export async function listTodayAppointments(): Promise<ReceptionAppointment[]> {
  const response = await fetch("/api/v1/appointments/today", {
    credentials: "include",
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as ReceptionAppointment[];
}

export async function confirmDeposit(
  appointmentId: string,
): Promise<{ appointmentId: string; depositConfirmedAt: string }> {
  const response = await fetch(
    `/api/v1/appointments/${appointmentId}/confirm-deposit`,
    {
      method: "POST",
      credentials: "include",
      headers: jsonHeaders(),
    },
  );
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as {
    appointmentId: string;
    depositConfirmedAt: string;
  };
}

export async function closeDay(
  queueSessionId: string,
): Promise<CloseDayResponse> {
  const response = await fetch(`/api/v1/queue-sessions/${queueSessionId}/close`, {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as CloseDayResponse;
}

export async function carryOver(
  appointmentId: string,
): Promise<{ appointmentId: string; status: string; nextDayPosition: number }> {
  const response = await fetch(
    `/api/v1/appointments/${appointmentId}/carry-over`,
    {
      method: "POST",
      credentials: "include",
      headers: jsonHeaders(),
    },
  );
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as {
    appointmentId: string;
    status: string;
    nextDayPosition: number;
  };
}

export async function cancelUnserved(
  appointmentId: string,
): Promise<{ appointmentId: string; status: string }> {
  const response = await fetch(
    `/api/v1/appointments/${appointmentId}/carry-over/cancel`,
    {
      method: "POST",
      credentials: "include",
      headers: jsonHeaders(),
    },
  );
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as { appointmentId: string; status: string };
}
