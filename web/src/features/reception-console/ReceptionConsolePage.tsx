import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { todayInClinicTimezone } from "@/lib/datetime";

import {
  bookOnBehalf,
  callNext,
  confirmArrival,
  instantEntry,
  listTodayAppointments,
  setAppointmentType,
  type ReceptionAppointment,
} from "./api";

const DEFAULT_TIMEZONE = "Africa/Cairo";

type Props = {
  clinicId: string;
  clinicTimezone?: string;
};

export function ReceptionConsolePage({
  clinicId,
  clinicTimezone = DEFAULT_TIMEZONE,
}: Props) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [actionError, setActionError] = useState<string | null>(null);

  const [patientRef, setPatientRef] = useState("");
  const [onBehalfType, setOnBehalfType] = useState<"Normal" | "Urgent">(
    "Normal",
  );
  const [companions, setCompanions] = useState(0);

  const [instantPatientRef, setInstantPatientRef] = useState("");
  const [instantReason, setInstantReason] = useState("");

  const queueQuery = useQuery({
    queryKey: ["reception-today", clinicId],
    queryFn: listTodayAppointments,
    refetchInterval: 5_000,
  });

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ["reception-today"] });
  };

  const confirmMutation = useMutation({
    mutationFn: confirmArrival,
    onSuccess: async () => {
      setActionError(null);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const callMutation = useMutation({
    mutationFn: callNext,
    onSuccess: async () => {
      setActionError(null);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const typeMutation = useMutation({
    mutationFn: ({
      id,
      type,
    }: {
      id: string;
      type: "Normal" | "Urgent";
    }) => setAppointmentType(id, type),
    onSuccess: async () => {
      setActionError(null);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const onBehalfMutation = useMutation({
    mutationFn: bookOnBehalf,
    onSuccess: async () => {
      setActionError(null);
      setPatientRef("");
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const instantMutation = useMutation({
    mutationFn: instantEntry,
    onSuccess: async () => {
      setActionError(null);
      setInstantPatientRef("");
      setInstantReason("");
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const onBehalfSubmit = (event: FormEvent) => {
    event.preventDefault();
    onBehalfMutation.mutate({
      clinicId,
      patientRef,
      day: todayInClinicTimezone(clinicTimezone),
      type: onBehalfType,
      method: "WalkIn",
      companionsCount: companions,
    });
  };

  const onInstantSubmit = (event: FormEvent) => {
    event.preventDefault();
    instantMutation.mutate({
      clinicId,
      patientRef: instantPatientRef,
      reason: instantReason,
    });
  };

  const appointments = queueQuery.data ?? [];

  return (
    <section className="mx-auto flex w-full max-w-3xl flex-col gap-10 px-4 py-8">
      <header className="space-y-2">
        <h1 className="text-3xl font-semibold tracking-tight">
          {t("reception.title")}
        </h1>
        <p className="text-muted-foreground text-sm">{t("reception.subtitle")}</p>
      </header>

      {actionError ? (
        <p className="text-sm text-red-700" role="alert">
          {actionError}
        </p>
      ) : null}

      <div className="space-y-3">
        <h2 className="text-lg font-medium">{t("reception.queue")}</h2>
        {queueQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">{t("reception.loading")}</p>
        ) : appointments.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("reception.empty")}</p>
        ) : (
          <ul className="divide-y divide-stone-200 border-y border-stone-200">
            {appointments.map((row) => (
              <QueueRow
                key={row.appointmentId}
                row={row}
                busy={
                  confirmMutation.isPending ||
                  callMutation.isPending ||
                  typeMutation.isPending
                }
                onConfirm={() => confirmMutation.mutate(row.appointmentId)}
                onCall={() => callMutation.mutate(row.appointmentId)}
                onToggleUrgent={() =>
                  typeMutation.mutate({
                    id: row.appointmentId,
                    type: row.type === "Urgent" ? "Normal" : "Urgent",
                  })
                }
              />
            ))}
          </ul>
        )}
      </div>

      <form
        onSubmit={onBehalfSubmit}
        className="flex flex-col gap-3 border-t border-stone-200 pt-6"
      >
        <h2 className="text-lg font-medium">{t("reception.onBehalf")}</h2>
        <label className="flex flex-col gap-1 text-sm">
          <span>{t("reception.patientRef")}</span>
          <input
            required
            value={patientRef}
            onChange={(e) => setPatientRef(e.target.value)}
            className="rounded-md border border-input bg-background px-3 py-2 font-mono text-xs"
            placeholder="membership-guid"
          />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span>{t("reception.type")}</span>
          <select
            value={onBehalfType}
            onChange={(e) =>
              setOnBehalfType(e.target.value as "Normal" | "Urgent")
            }
            className="rounded-md border border-input bg-background px-3 py-2 text-sm"
          >
            <option value="Normal">{t("reception.normal")}</option>
            <option value="Urgent">{t("reception.urgent")}</option>
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span>{t("reception.companions")}</span>
          <input
            type="number"
            min={0}
            max={20}
            value={companions}
            onChange={(e) => setCompanions(Number(e.target.value))}
            className="rounded-md border border-input bg-background px-3 py-2 text-sm"
          />
        </label>
        <Button type="submit" disabled={onBehalfMutation.isPending}>
          {onBehalfMutation.isPending
            ? t("reception.booking")
            : t("reception.bookOnBehalf")}
        </Button>
      </form>

      <form
        onSubmit={onInstantSubmit}
        className="flex flex-col gap-3 border-t border-stone-200 pt-6"
      >
        <h2 className="text-lg font-medium">{t("reception.instantEntry")}</h2>
        <label className="flex flex-col gap-1 text-sm">
          <span>{t("reception.patientRef")}</span>
          <input
            required
            value={instantPatientRef}
            onChange={(e) => setInstantPatientRef(e.target.value)}
            className="rounded-md border border-input bg-background px-3 py-2 font-mono text-xs"
            placeholder="membership-guid"
          />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span>{t("reception.reason")}</span>
          <textarea
            required
            value={instantReason}
            onChange={(e) => setInstantReason(e.target.value)}
            rows={2}
            className="rounded-md border border-input bg-background px-3 py-2 text-sm"
          />
        </label>
        <Button type="submit" disabled={instantMutation.isPending}>
          {instantMutation.isPending
            ? t("reception.inserting")
            : t("reception.insertNow")}
        </Button>
      </form>
    </section>
  );
}

function QueueRow({
  row,
  busy,
  onConfirm,
  onCall,
  onToggleUrgent,
}: {
  row: ReceptionAppointment;
  busy: boolean;
  onConfirm: () => void;
  onCall: () => void;
  onToggleUrgent: () => void;
}) {
  const { t } = useTranslation();
  const canConfirm = row.status === "Booked";
  const canCall = row.status === "Present" && row.realNumber != null;
  const canToggleType = row.realNumber == null && row.status === "Booked";

  return (
    <li className="flex flex-col gap-2 py-4 sm:flex-row sm:items-center sm:justify-between">
      <div className="space-y-1 text-sm">
        <p className="font-medium">
          {t("reception.tempNumber", { number: row.temporaryNumber })}
          {row.realNumber != null
            ? ` · ${t("reception.realNumber", { number: row.realNumber })}`
            : ""}
        </p>
        <p className="text-muted-foreground">
          {row.status} · {row.type}
          {row.possibleUrgentFlag ? ` · ${t("reception.flaggedUrgent")}` : ""}
          {row.isInstantEntry ? ` · ${t("reception.instantBadge")}` : ""}
        </p>
      </div>
      <div className="flex flex-wrap gap-2">
        {canToggleType ? (
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={busy}
            onClick={onToggleUrgent}
          >
            {row.type === "Urgent"
              ? t("reception.markNormal")
              : t("reception.markUrgent")}
          </Button>
        ) : null}
        {canConfirm ? (
          <Button
            type="button"
            size="sm"
            disabled={busy}
            onClick={onConfirm}
          >
            {t("reception.confirm")}
          </Button>
        ) : null}
        {canCall ? (
          <Button type="button" size="sm" disabled={busy} onClick={onCall}>
            {t("reception.callNext")}
          </Button>
        ) : null}
      </div>
    </li>
  );
}
