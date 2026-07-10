export type ReceptionAppointment = {
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
  companionsCount: number;
  repeatedAbsenceFlag?: boolean;
  absenceCount?: number;
  membershipStatus?: string | null;
};

export type ConfirmArrivalResponse = {
  appointmentId: string;
  realNumber?: number | null;
  status: string;
  waitingForUrgentRatio?: boolean;
};

export type OnBehalfPayload = {
  clinicId: string;
  patientRef: string;
  day: string;
  type: "Normal" | "Urgent";
  method: "WalkIn" | "PhoneDeposit";
  companionsCount: number;
};

export type InstantEntryPayload = {
  clinicId: string;
  patientRef: string;
  reason: string;
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

export async function listTodayAppointments(): Promise<ReceptionAppointment[]> {
  const response = await fetch("/api/v1/appointments/today", {
    credentials: "include",
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as ReceptionAppointment[];
}

export async function confirmArrival(
  appointmentId: string,
): Promise<ConfirmArrivalResponse> {
  const response = await fetch(
    `/api/v1/appointments/${appointmentId}/confirm-arrival`,
    {
      method: "POST",
      credentials: "include",
      headers: jsonHeaders(),
    },
  );
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as ConfirmArrivalResponse;
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

export async function setAppointmentType(
  appointmentId: string,
  type: "Normal" | "Urgent",
): Promise<void> {
  const response = await fetch(`/api/v1/appointments/${appointmentId}/type`, {
    method: "PATCH",
    credentials: "include",
    headers: jsonHeaders(),
    body: JSON.stringify({ type }),
  });
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
}

export async function bookOnBehalf(
  payload: OnBehalfPayload,
): Promise<{ appointmentId: string; temporaryNumber: number }> {
  const response = await fetch("/api/v1/appointments/on-behalf", {
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

export async function instantEntry(
  payload: InstantEntryPayload,
): Promise<{ appointmentId: string; realNumber: number; status: string }> {
  const response = await fetch("/api/v1/appointments/instant-entry", {
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
    realNumber: number;
    status: string;
  };
}

export async function restrictBooking(
  membershipId: string,
): Promise<{ membershipId: string; status: string }> {
  const response = await fetch(
    `/api/v1/patients/${membershipId}/restrict-booking`,
    {
      method: "POST",
      credentials: "include",
      headers: jsonHeaders(),
    },
  );
  if (!response.ok) {
    throw new Error(await parseError(response));
  }
  return (await response.json()) as { membershipId: string; status: string };
}
