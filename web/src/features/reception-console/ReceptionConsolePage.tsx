import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useActiveClinic } from "@/features/auth/AuthProvider";
import { todayInClinicTimezone } from "@/lib/datetime";

import {
  bookOnBehalf,
  callNext,
  confirmArrival,
  instantEntry,
  listTodayAppointments,
  restrictBooking,
  setAppointmentType,
  type ReceptionAppointment,
} from "./api";

const DEFAULT_TIMEZONE = "Africa/Cairo";

type Props = {
  clinicTimezone?: string;
};

export function ReceptionConsolePage({
  clinicTimezone = DEFAULT_TIMEZONE,
}: Props = {}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { id: clinicId } = useActiveClinic();
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

  const restrictMutation = useMutation({
    mutationFn: restrictBooking,
    onSuccess: async () => {
      setActionError(null);
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
    <section className="cq-page">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("reception.title")}</h1>
        <p className="cq-page-subtitle">{t("reception.subtitle")}</p>
      </header>

      {actionError ? (
        <p className="cq-alert" role="alert">
          {actionError}
        </p>
      ) : null}

      <div className="cq-section">
        <h2 className="cq-section-title">{t("reception.queue")}</h2>
        {queueQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">{t("reception.loading")}</p>
        ) : appointments.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("reception.empty")}</p>
        ) : (
          <ul className="cq-list">
            {appointments.map((row) => (
              <QueueRow
                key={row.appointmentId}
                row={row}
                busy={
                  confirmMutation.isPending ||
                  callMutation.isPending ||
                  typeMutation.isPending ||
                  restrictMutation.isPending
                }
                onConfirm={() => confirmMutation.mutate(row.appointmentId)}
                onCall={() => callMutation.mutate(row.appointmentId)}
                onToggleUrgent={() =>
                  typeMutation.mutate({
                    id: row.appointmentId,
                    type: row.type === "Urgent" ? "Normal" : "Urgent",
                  })
                }
                onRestrict={() => {
                  if (row.patientMembershipId) {
                    restrictMutation.mutate(row.patientMembershipId);
                  }
                }}
              />
            ))}
          </ul>
        )}
      </div>

      <form onSubmit={onBehalfSubmit} className="cq-section gap-3">
        <h2 className="cq-section-title">{t("reception.onBehalf")}</h2>
        <label className="cq-label">
          <span>{t("reception.patientRef")}</span>
          <input
            required
            value={patientRef}
            onChange={(e) => setPatientRef(e.target.value)}
            className="cq-field-mono"
            placeholder={t("reception.membershipGuidPlaceholder")}
            aria-label={t("reception.patientRef")}
          />
        </label>
        <label className="cq-label">
          <span>{t("reception.type")}</span>
          <select
            value={onBehalfType}
            onChange={(e) =>
              setOnBehalfType(e.target.value as "Normal" | "Urgent")
            }
            className="cq-field"
          >
            <option value="Normal">{t("reception.normal")}</option>
            <option value="Urgent">{t("reception.urgent")}</option>
          </select>
        </label>
        <label className="cq-label">
          <span>{t("reception.companions")}</span>
          <input
            type="number"
            min={0}
            max={20}
            value={companions}
            onChange={(e) => setCompanions(Number(e.target.value))}
            className="cq-field"
          />
        </label>
        <Button type="submit" disabled={onBehalfMutation.isPending}>
          {onBehalfMutation.isPending
            ? t("reception.booking")
            : t("reception.bookOnBehalf")}
        </Button>
      </form>

      <form onSubmit={onInstantSubmit} className="cq-section gap-3">
        <h2 className="cq-section-title">{t("reception.instantEntry")}</h2>
        <label className="cq-label">
          <span>{t("reception.patientRef")}</span>
          <input
            required
            value={instantPatientRef}
            onChange={(e) => setInstantPatientRef(e.target.value)}
            className="cq-field-mono"
            placeholder={t("reception.membershipGuidPlaceholder")}
            aria-label={t("reception.patientRef")}
          />
        </label>
        <label className="cq-label">
          <span>{t("reception.reason")}</span>
          <textarea
            required
            value={instantReason}
            onChange={(e) => setInstantReason(e.target.value)}
            rows={2}
            className="cq-field"
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
  onRestrict,
}: {
  row: ReceptionAppointment;
  busy: boolean;
  onConfirm: () => void;
  onCall: () => void;
  onToggleUrgent: () => void;
  onRestrict: () => void;
}) {
  const { t } = useTranslation();
  const canConfirm = row.status === "Booked";
  const canCall = row.status === "Present" && row.realNumber != null;
  const canToggleType = row.realNumber == null && row.status === "Booked";
  const isRestricted = row.membershipStatus === "Restricted";
  const canRestrict =
    Boolean(row.patientMembershipId) &&
    Boolean(row.repeatedAbsenceFlag) &&
    !isRestricted;

  return (
    <li className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center sm:justify-between">
      <div className="space-y-1 text-sm">
        <p className="font-medium tabular-nums">
          {t("reception.tempNumber", { number: row.temporaryNumber })}
          {row.realNumber != null
            ? ` · ${t("reception.realNumber", { number: row.realNumber })}`
            : ""}
        </p>
        <p className="text-muted-foreground">
          {row.status} · {row.type}
          {row.possibleUrgentFlag ? ` · ${t("reception.flaggedUrgent")}` : ""}
          {row.isInstantEntry ? ` · ${t("reception.instantBadge")}` : ""}
          {row.repeatedAbsenceFlag
            ? ` · ${t("reception.repeatedAbsence", { count: row.absenceCount ?? 0 })}`
            : ""}
          {isRestricted ? ` · ${t("reception.restricted")}` : ""}
        </p>
      </div>
      <div className="flex flex-wrap gap-2">
        {canToggleType ? (
          <Button
            type="button"
            variant="outline"
            className="cq-touch-btn"
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
            className="cq-touch-btn"
            disabled={busy}
            onClick={onConfirm}
          >
            {t("reception.confirm")}
          </Button>
        ) : null}
        {canCall ? (
          <Button
            type="button"
            className="cq-touch-btn"
            disabled={busy}
            onClick={onCall}
          >
            {t("reception.callNext")}
          </Button>
        ) : null}
        {canRestrict ? (
          <Button
            type="button"
            variant="outline"
            className="cq-touch-btn"
            disabled={busy}
            onClick={onRestrict}
          >
            {t("reception.restrictBooking")}
          </Button>
        ) : null}
      </div>
    </li>
  );
}
