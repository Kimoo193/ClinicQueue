/** Visual states for the patient live-queue hero (presentation only). */
export type QueueVisualState =
  | "waitHome"
  | "headToClinic"
  | "yourTurn"
  | "paused"
  | "pending";

export type AppointmentTypeBadge = "Normal" | "Urgent";

export type LiveQueueViewModel = {
  appointmentId: string;
  displayNumber: number;
  numberKind: "temporary" | "real";
  countAhead: number | null;
  visualState: QueueVisualState;
  appointmentType: AppointmentTypeBadge;
  isLive: boolean;
};

/** SignalR notification envelope from SignalRNotificationChannel. */
export type HubNotification = {
  id?: string;
  type?: number | string;
  payloadJson?: string;
  clinicId?: string;
};

export type PauseResumePayload = {
  appointmentId?: string;
  action?: string;
  reason?: string;
};

export type YourTurnPayload = {
  appointmentId?: string;
  instruction?: string;
  realNumber?: number | null;
};

/** Backend NotificationType.PauseResume = 3, YourTurn = 2 */
export const NOTIFICATION_TYPE = {
  YourTurn: 2,
  PauseResume: 3,
} as const;

export function resolveQueueVisualState(input: {
  status: string;
  instruction?: "waitHome" | "headToClinic" | "yourTurn" | null;
  paused?: boolean;
}): QueueVisualState {
  if (input.paused) {
    return "paused";
  }
  if (
    input.status === "Inside" ||
    input.instruction === "yourTurn"
  ) {
    return "yourTurn";
  }
  if (input.instruction === "headToClinic") {
    return "headToClinic";
  }
  if (input.instruction === "waitHome") {
    return "waitHome";
  }
  return "pending";
}

export function parseAppointmentType(
  raw: string | null | undefined,
): AppointmentTypeBadge {
  return raw?.toLowerCase() === "urgent" ? "Urgent" : "Normal";
}
