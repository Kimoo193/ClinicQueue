import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useActiveClinic } from "@/features/auth/AuthProvider";
import { useQueueHub } from "@/features/realtime/QueueHubProvider";
import { todayInClinicTimezone } from "@/lib/datetime";
import { ensurePushSubscription } from "@/serviceWorker/push";

import {
  createAppointment,
  listMyAppointments,
  type PatientAppointment,
  type PositionUpdate,
} from "./api";
import {
  LiveQueueCard,
  LiveQueueEmpty,
  LiveQueueError,
  LiveQueueSkeleton,
} from "./LiveQueueCard";
import {
  NOTIFICATION_TYPE,
  parseAppointmentType,
  resolveQueueVisualState,
  type HubNotification,
  type PauseResumePayload,
  type YourTurnPayload,
} from "./liveQueueTypes";

const DEFAULT_TIMEZONE = "Africa/Cairo";

type Props = {
  clinicTimezone?: string;
};

type LiveExtras = {
  pausedById: Record<string, boolean>;
  yourTurnById: Record<string, boolean>;
};

export function PatientQueuePage({
  clinicTimezone = DEFAULT_TIMEZONE,
}: Props = {}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { id: clinicId } = useActiveClinic();
  const { connection } = useQueueHub();
  const [possibleUrgent, setPossibleUrgent] = useState(false);
  const [liveById, setLiveById] = useState<Record<string, PositionUpdate>>({});
  const [extras, setExtras] = useState<LiveExtras>({
    pausedById: {},
    yourTurnById: {},
  });
  const [formError, setFormError] = useState<string | null>(null);

  const appointmentsQuery = useQuery({
    queryKey: ["my-appointments", "upcoming"],
    queryFn: () => listMyAppointments("upcoming"),
  });

  const bookMutation = useMutation({
    mutationFn: createAppointment,
    onSuccess: async () => {
      setFormError(null);
      await queryClient.invalidateQueries({ queryKey: ["my-appointments"] });
      // Request push only after a successful book (not on every page load).
      void ensurePushSubscription(clinicId);
    },
    onError: () => {
      setFormError(t("patientQueue.bookFailed"));
    },
  });

  // Subscribe to live position updates from the session-scoped hub (AuthProvider lifecycle).
  useEffect(() => {
    if (!connection) {
      return;
    }

    const onPosition = (update: PositionUpdate) => {
      setLiveById((prev) => ({ ...prev, [update.appointmentId]: update }));
      // A fresh position reasserts live reality: if the patient still has people ahead the
      // queue has moved on (reorder, urgent insertion, mis-fire), so a prior "your turn" must
      // be cleared — otherwise the hero stays stuck on "It's your turn" indefinitely.
      setExtras((prev) => ({
        ...prev,
        yourTurnById: {
          ...prev.yourTurnById,
          [update.appointmentId]: update.instruction === "yourTurn",
        },
        pausedById: { ...prev.pausedById, [update.appointmentId]: false },
      }));
    };

    const onNotification = (raw: HubNotification) => {
      const typeNum =
        typeof raw.type === "number"
          ? raw.type
          : typeof raw.type === "string"
            ? Number.parseInt(raw.type, 10)
            : NaN;

      let payload: PauseResumePayload & YourTurnPayload = {};
      if (raw.payloadJson) {
        try {
          payload = JSON.parse(raw.payloadJson) as PauseResumePayload &
            YourTurnPayload;
        } catch {
          return;
        }
      }

      const appointmentId = payload.appointmentId;
      if (!appointmentId) {
        return;
      }

      if (typeNum === NOTIFICATION_TYPE.YourTurn) {
        setExtras((prev) => ({
          ...prev,
          yourTurnById: { ...prev.yourTurnById, [appointmentId]: true },
          pausedById: { ...prev.pausedById, [appointmentId]: false },
        }));
        return;
      }

      if (typeNum === NOTIFICATION_TYPE.PauseResume) {
        const paused = payload.action === "pause";
        setExtras((prev) => ({
          ...prev,
          pausedById: { ...prev.pausedById, [appointmentId]: paused },
        }));
      }
    };

    connection.on("positionUpdated", onPosition);
    connection.on("notification", onNotification);
    return () => {
      connection.off("positionUpdated", onPosition);
      connection.off("notification", onNotification);
    };
  }, [connection]);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    bookMutation.mutate({
      clinicId,
      day: todayInClinicTimezone(clinicTimezone),
      method: "WalkIn",
      possibleUrgentFlag: possibleUrgent,
    });
  };

  const appointments = appointmentsQuery.data ?? [];
  const isLive = Boolean(connection);

  return (
    <section className="cq-page">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("patientQueue.title")}</h1>
        <p className="cq-page-subtitle">{t("patientQueue.subtitle")}</p>
      </header>

      <div className="flex flex-col gap-4">
        {appointmentsQuery.isLoading ? <LiveQueueSkeleton /> : null}

        {appointmentsQuery.isError ? (
          <LiveQueueError onRetry={() => void appointmentsQuery.refetch()} />
        ) : null}

        {!appointmentsQuery.isLoading &&
        !appointmentsQuery.isError &&
        appointments.length === 0 ? (
          <LiveQueueEmpty />
        ) : null}

        {!appointmentsQuery.isLoading && !appointmentsQuery.isError
          ? appointments.map((appointment) => (
              <AppointmentHero
                key={appointment.appointmentId}
                appointment={appointment}
                live={liveById[appointment.appointmentId]}
                paused={Boolean(extras.pausedById[appointment.appointmentId])}
                forcedYourTurn={Boolean(
                  extras.yourTurnById[appointment.appointmentId],
                )}
                isLive={isLive}
              />
            ))
          : null}
      </div>

      <form onSubmit={onSubmit} className="cq-section gap-4">
        <h2 className="cq-section-title">{t("patientQueue.bookSection")}</h2>
        <label className="cq-check">
          <input
            type="checkbox"
            checked={possibleUrgent}
            onChange={(e) => setPossibleUrgent(e.target.checked)}
          />
          <span>{t("patientQueue.possibleUrgent")}</span>
        </label>

        {formError ? (
          <p className="cq-alert" role="alert">
            {formError}
          </p>
        ) : null}

        <Button
          type="submit"
          className="cq-touch-btn w-full sm:w-auto"
          disabled={bookMutation.isPending}
        >
          {bookMutation.isPending
            ? t("patientQueue.booking")
            : t("patientQueue.book")}
        </Button>
      </form>
    </section>
  );
}

function AppointmentHero({
  appointment,
  live,
  paused,
  forcedYourTurn,
  isLive,
}: {
  appointment: PatientAppointment;
  live?: PositionUpdate;
  paused: boolean;
  forcedYourTurn: boolean;
  isLive: boolean;
}) {
  const countAhead = live?.countAhead ?? appointment.countAhead ?? null;
  const instruction =
    forcedYourTurn
      ? ("yourTurn" as const)
      : (live?.instruction ?? appointment.instruction ?? null);

  const visualState = resolveQueueVisualState({
    status: appointment.status,
    instruction,
    paused,
  });

  const hasReal =
    appointment.realNumber !== null &&
    appointment.realNumber !== undefined &&
    appointment.realNumber > 0;

  return (
    <LiveQueueCard
      model={{
        appointmentId: appointment.appointmentId,
        displayNumber: hasReal
          ? appointment.realNumber!
          : appointment.temporaryNumber,
        numberKind: hasReal ? "real" : "temporary",
        countAhead,
        visualState,
        appointmentType: parseAppointmentType(appointment.type),
        isLive,
      }}
    />
  );
}
