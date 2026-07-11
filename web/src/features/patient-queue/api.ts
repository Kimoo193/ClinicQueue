export type CreateAppointmentPayload = {
  clinicId: string;
  day: string;
  method: "WalkIn" | "PhoneDeposit";
  companionsCount: number;
  possibleUrgentFlag?: boolean;
  dependentId?: string;
};

export type CreateAppointmentResponse = {
  appointmentId: string;
  temporaryNumber: number;
};

export type PatientAppointment = {
  appointmentId: string;
  clinicId: string;
  status: string;
  temporaryNumber: number;
  realNumber?: number | null;
  countAhead?: number | null;
  instruction?: "waitHome" | "headToClinic" | "yourTurn" | null;
  /** Present when API includes appointment type; defaults to Normal in UI. */
  type?: string | null;
};

export type PositionUpdate = {
  appointmentId: string;
  countAhead: number;
  instruction: "waitHome" | "headToClinic" | "yourTurn";
};

export type PushSubscriptionPayload = {
  clinicId: string;
  endpoint: string;
  p256dh: string;
  auth: string;
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

export async function createAppointment(
  payload: CreateAppointmentPayload,
): Promise<CreateAppointmentResponse> {
  const response = await fetch("/api/v1/appointments", {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
    body: JSON.stringify(payload),
  });

  if (!response.ok) {
    throw new Error(await parseError(response));
  }

  return (await response.json()) as CreateAppointmentResponse;
}

export async function listMyAppointments(
  status = "upcoming",
): Promise<PatientAppointment[]> {
  const response = await fetch(
    `/api/v1/patients/me/appointments?status=${encodeURIComponent(status)}`,
    { credentials: "include" },
  );

  if (!response.ok) {
    throw new Error(await parseError(response));
  }

  return (await response.json()) as PatientAppointment[];
}

export async function persistPushSubscription(
  payload: PushSubscriptionPayload,
): Promise<void> {
  const response = await fetch("/api/v1/push-subscriptions", {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(),
    body: JSON.stringify(payload),
  });

  if (!response.ok) {
    throw new Error(await parseError(response));
  }
}
