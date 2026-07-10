import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { todayInClinicTimezone } from "@/lib/datetime";
import { ensurePushSubscription } from "@/serviceWorker/push";

import {
  createAppointment,
  listMyAppointments,
  type PatientAppointment,
  type PositionUpdate,
} from "./api";

const DEFAULT_TIMEZONE = "Africa/Cairo";

type Props = {
  clinicId: string;
  clinicTimezone?: string;
};

export function PatientQueuePage({
  clinicId,
  clinicTimezone = DEFAULT_TIMEZONE,
}: Props) {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const [companionsCount, setCompanionsCount] = useState(0);
  const [possibleUrgent, setPossibleUrgent] = useState(false);
  const [liveById, setLiveById] = useState<Record<string, PositionUpdate>>({});
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
    onError: (error: Error) => {
      setFormError(error.message);
    },
  });

  useEffect(() => {
    if (!clinicId) {
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/queue", { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("positionUpdated", (update: PositionUpdate) => {
      setLiveById((prev) => ({ ...prev, [update.appointmentId]: update }));
    });

    void connection
      .start()
      .then(() => connection.invoke("JoinClinic", clinicId))
      .catch((err: unknown) => {
        console.warn("SignalR connect failed", err);
      });

    return () => {
      void connection.stop();
    };
  }, [clinicId]);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    bookMutation.mutate({
      clinicId,
      day: todayInClinicTimezone(clinicTimezone),
      method: "WalkIn",
      companionsCount,
      possibleUrgentFlag: possibleUrgent,
    });
  };

  const appointments = appointmentsQuery.data ?? [];

  return (
    <section className="mx-auto flex w-full max-w-xl flex-col gap-8 px-4 py-8">
      <header className="space-y-2">
        <h1 className="text-3xl font-semibold tracking-tight">
          {t("patientQueue.title")}
        </h1>
        <p className="text-muted-foreground text-sm">
          {t("patientQueue.subtitle")}
        </p>
      </header>

      <form
        onSubmit={onSubmit}
        className="flex flex-col gap-4 border-t border-emerald-900/10 pt-6"
      >
        <label className="flex flex-col gap-1 text-sm">
          <span>{t("patientQueue.companions")}</span>
          <input
            type="number"
            min={0}
            max={20}
            value={companionsCount}
            onChange={(e) => setCompanionsCount(Number(e.target.value))}
            className="rounded-md border border-input bg-background px-3 py-2"
          />
        </label>

        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={possibleUrgent}
            onChange={(e) => setPossibleUrgent(e.target.checked)}
          />
          <span>{t("patientQueue.possibleUrgent")}</span>
        </label>

        {formError ? (
          <p className="text-sm text-red-700" role="alert">
            {formError}
          </p>
        ) : null}

        <Button type="submit" disabled={bookMutation.isPending}>
          {bookMutation.isPending
            ? t("patientQueue.booking")
            : t("patientQueue.book")}
        </Button>
      </form>

      <div className="space-y-4 border-t border-emerald-900/10 pt-6">
        <h2 className="text-xl font-medium">{t("patientQueue.myBookings")}</h2>
        {appointmentsQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">{t("patientQueue.loading")}</p>
        ) : null}
        {appointments.length === 0 && !appointmentsQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">{t("patientQueue.empty")}</p>
        ) : null}
        <ul className="flex flex-col gap-4">
          {appointments.map((appointment) => (
            <AppointmentCard
              key={appointment.appointmentId}
              appointment={appointment}
              live={liveById[appointment.appointmentId]}
              locale={i18n.language}
            />
          ))}
        </ul>
      </div>
    </section>
  );
}

function AppointmentCard({
  appointment,
  live,
  locale,
}: {
  appointment: PatientAppointment;
  live?: PositionUpdate;
  locale: string;
}) {
  const { t } = useTranslation();
  const countAhead = live?.countAhead ?? appointment.countAhead ?? null;
  const instruction = live?.instruction ?? appointment.instruction ?? null;
  const isRtl = locale.startsWith("ar");

  return (
    <li className="rounded-lg border border-emerald-900/15 bg-emerald-50/40 p-4">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <p className="text-2xl font-semibold tabular-nums">
          {t("patientQueue.tempNumber", { number: appointment.temporaryNumber })}
        </p>
        <span className="text-xs uppercase tracking-wide text-muted-foreground">
          {appointment.status}
        </span>
      </div>

      {countAhead !== null ? (
        <p className="mt-2 text-sm">
          {t("patientQueue.countAhead", { count: countAhead })}
        </p>
      ) : null}

      {instruction ? (
        <p
          className={`mt-3 rounded-md px-3 py-2 text-sm font-medium ${
            instruction === "headToClinic"
              ? "bg-amber-100 text-amber-950"
              : "bg-sky-100 text-sky-950"
          }`}
          dir={isRtl ? "rtl" : "ltr"}
          role="status"
        >
          {instruction === "headToClinic"
            ? t("patientQueue.instruction.headToClinic")
            : t("patientQueue.instruction.waitHome")}
        </p>
      ) : null}
    </li>
  );
}
