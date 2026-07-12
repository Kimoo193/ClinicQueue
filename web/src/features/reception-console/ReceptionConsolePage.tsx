import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useActiveClinic } from "@/features/auth/AuthProvider";
import { todayInClinicTimezone } from "@/lib/datetime";
import { cn } from "@/lib/utils";

import {
  bookOnBehalf,
  callNext,
  confirmArrival,
  getTodaySession,
  instantEntry,
  listTodayAppointments,
  markAbsent,
  pauseSession,
  restrictBooking,
  resumeSession,
  setAppointmentType,
  type ReceptionAppointment,
} from "./api";

const DEFAULT_TIMEZONE = "Africa/Cairo";

type Props = {
  clinicTimezone?: string;
};

type ConfirmKind =
  | { kind: "absent"; appointmentId: string; label: string }
  | { kind: "restrict"; membershipId: string; label: string }
  | { kind: "instant" }
  | { kind: "pause" };

function patientLabel(row: ReceptionAppointment): string {
  // Prefer the human identifier (email/phone) from the API; fall back to a short id only
  // when the booking has no linked user (e.g. a dependent).
  if (row.patientLabel) {
    return row.patientLabel;
  }
  const id = row.patientMembershipId ?? row.bookedByUserId;
  if (!id) {
    return "—";
  }
  return id.length > 10 ? `${id.slice(0, 8)}…` : id;
}

function displayNumber(row: ReceptionAppointment): string {
  if (row.realNumber != null) {
    return String(row.realNumber);
  }
  return `T${row.temporaryNumber}`;
}

function deriveMetrics(appointments: ReceptionAppointment[]) {
  const waiting = appointments.filter(
    (a) => a.status === "Booked" || a.status === "Present",
  ).length;
  const completed = appointments.filter((a) => a.status === "Completed").length;
  const absent = appointments.filter((a) => a.status === "Absent").length;
  const inside = appointments.find((a) => a.status === "Inside");
  const currentNumber =
    inside?.realNumber ??
    appointments
      .filter((a) => a.realNumber != null && a.status === "Present")
      .map((a) => a.realNumber as number)
      .sort((a, b) => a - b)[0] ??
    null;
  return { waiting, completed, absent, currentNumber };
}

export function ReceptionConsolePage({
  clinicTimezone = DEFAULT_TIMEZONE,
}: Props = {}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { id: clinicId } = useActiveClinic();
  const [actionError, setActionError] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<ConfirmKind | null>(null);

  const [patientRef, setPatientRef] = useState("");
  const [onBehalfType, setOnBehalfType] = useState<"Normal" | "Urgent">(
    "Normal",
  );
  const [companions, setCompanions] = useState(0);

  const [instantPatientRef, setInstantPatientRef] = useState("");
  const [instantReason, setInstantReason] = useState("");
  const [instantReasonError, setInstantReasonError] = useState<string | null>(
    null,
  );

  const [pauseReason, setPauseReason] = useState("");
  const [pauseReasonError, setPauseReasonError] = useState<string | null>(null);

  const queueQuery = useQuery({
    queryKey: ["reception-today", clinicId],
    queryFn: listTodayAppointments,
    refetchInterval: 5_000,
  });

  const sessionQuery = useQuery({
    queryKey: ["reception-session", clinicId],
    queryFn: getTodaySession,
    refetchInterval: 5_000,
  });

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ["reception-today"] });
    await queryClient.invalidateQueries({ queryKey: ["reception-session"] });
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

  const absentMutation = useMutation({
    mutationFn: markAbsent,
    onSuccess: async () => {
      setActionError(null);
      setConfirm(null);
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
      setInstantReasonError(null);
      setConfirm(null);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const restrictMutation = useMutation({
    mutationFn: restrictBooking,
    onSuccess: async () => {
      setActionError(null);
      setConfirm(null);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const pauseMutation = useMutation({
    mutationFn: ({
      sessionId,
      reason,
    }: {
      sessionId: string;
      reason: string;
    }) => pauseSession(sessionId, reason),
    onSuccess: async () => {
      setActionError(null);
      setPauseReason("");
      setPauseReasonError(null);
      setConfirm(null);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const resumeMutation = useMutation({
    mutationFn: resumeSession,
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

  const requestInstantConfirm = (event: FormEvent) => {
    event.preventDefault();
    const reason = instantReason.trim();
    if (!reason) {
      setInstantReasonError(t("reception.reasonRequiredError"));
      return;
    }
    if (!instantPatientRef.trim()) {
      return;
    }
    setInstantReasonError(null);
    setConfirm({ kind: "instant" });
  };

  const requestPauseConfirm = (event: FormEvent) => {
    event.preventDefault();
    const reason = pauseReason.trim();
    if (!reason) {
      setPauseReasonError(t("reception.pauseReasonRequired"));
      return;
    }
    setPauseReasonError(null);
    setConfirm({ kind: "pause" });
  };

  const runConfirmedAction = () => {
    if (!confirm) {
      return;
    }
    if (confirm.kind === "absent") {
      absentMutation.mutate(confirm.appointmentId);
      return;
    }
    if (confirm.kind === "restrict") {
      restrictMutation.mutate(confirm.membershipId);
      return;
    }
    if (confirm.kind === "instant") {
      instantMutation.mutate({
        clinicId,
        patientRef: instantPatientRef.trim(),
        reason: instantReason.trim(),
      });
      return;
    }
    const sessionId = sessionQuery.data?.queueSessionId;
    if (!sessionId) {
      return;
    }
    pauseMutation.mutate({
      sessionId,
      reason: pauseReason.trim(),
    });
  };

  const appointments = queueQuery.data ?? [];
  const session = sessionQuery.data;
  const isPaused = session?.status === "Paused";
  const metrics = deriveMetrics(appointments);
  const rowBusy =
    confirmMutation.isPending ||
    callMutation.isPending ||
    typeMutation.isPending ||
    restrictMutation.isPending ||
    absentMutation.isPending;

  const confirmBusy =
    absentMutation.isPending ||
    instantMutation.isPending ||
    pauseMutation.isPending ||
    restrictMutation.isPending;

  return (
    <section
      className="cq-page cq-reception-console"
      data-paused={isPaused ? "true" : "false"}
    >
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("reception.title")}</h1>
        <p className="cq-page-subtitle">{t("reception.subtitle")}</p>
      </header>

      {isPaused ? (
        <div className="cq-paused-banner" role="status" aria-live="assertive">
          <div className="space-y-1">
            <p className="text-base font-semibold">
              {t("reception.pausedBannerTitle")}
            </p>
            <p className="text-sm">
              {t("reception.pausedBannerBody")}
              {session?.pauseReason
                ? ` — ${session.pauseReason}`
                : ""}
            </p>
          </div>
          {session ? (
            <div className="flex shrink-0 flex-col items-stretch gap-1 sm:items-end">
              <Button
                type="button"
                size="lg"
                className="cq-touch-btn"
                disabled={resumeMutation.isPending}
                onClick={() => resumeMutation.mutate(session.queueSessionId)}
              >
                {resumeMutation.isPending
                  ? t("reception.resuming")
                  : t("reception.resume")}
              </Button>
              <p className="text-xs opacity-80">{t("reception.resumeHint")}</p>
            </div>
          ) : null}
        </div>
      ) : null}

      <StatusStrip
        isPaused={isPaused}
        loading={sessionQuery.isLoading && queueQuery.isLoading}
        currentNumber={metrics.currentNumber}
        waiting={metrics.waiting}
        completed={metrics.completed}
        absent={metrics.absent}
      />

      {actionError ? (
        <p className="cq-alert" role="alert">
          {actionError}
        </p>
      ) : null}

      {queueQuery.isError ? (
        <p className="cq-alert" role="alert">
          {t("reception.loadError")}
        </p>
      ) : null}

      <div className="cq-reception-workbench flex flex-col gap-8">
        <div className="cq-section !border-t-0 !pt-0">
          <h2 className="cq-section-title">{t("reception.queue")}</h2>
          {queueQuery.isLoading ? (
            <div className="cq-reception-skeleton" aria-busy="true">
              <p className="text-sm text-muted-foreground">
                {t("reception.loading")}
              </p>
              <div className="cq-reception-skeleton-row" />
              <div className="cq-reception-skeleton-row" />
              <div className="cq-reception-skeleton-row" />
            </div>
          ) : appointments.length === 0 ? (
            <p className="rounded-xl border border-dashed border-border bg-surface px-4 py-8 text-center text-sm text-muted-foreground">
              {t("reception.empty")}
            </p>
          ) : (
            <div className="cq-reception-table-wrap">
              <table className="cq-reception-table">
                <thead>
                  <tr>
                    <th scope="col">{t("reception.colNumber")}</th>
                    <th scope="col">{t("reception.colPatient")}</th>
                    <th scope="col">{t("reception.colType")}</th>
                    <th scope="col">{t("reception.colMethod")}</th>
                    <th scope="col">{t("reception.colStatus")}</th>
                    <th scope="col">{t("reception.colActions")}</th>
                  </tr>
                </thead>
                <tbody>
                  {appointments.map((row) => (
                    <QueueRow
                      key={row.appointmentId}
                      row={row}
                      busy={rowBusy || isPaused}
                      onConfirm={() =>
                        confirmMutation.mutate(row.appointmentId)
                      }
                      onCall={() => callMutation.mutate(row.appointmentId)}
                      onMarkAbsent={() =>
                        setConfirm({
                          kind: "absent",
                          appointmentId: row.appointmentId,
                          label: patientLabel(row),
                        })
                      }
                      onToggleUrgent={() =>
                        typeMutation.mutate({
                          id: row.appointmentId,
                          type: row.type === "Urgent" ? "Normal" : "Urgent",
                        })
                      }
                      onRestrict={() => {
                        if (row.patientMembershipId) {
                          setConfirm({
                            kind: "restrict",
                            membershipId: row.patientMembershipId,
                            label: patientLabel(row),
                          });
                        }
                      }}
                    />
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        <div className="grid gap-8 lg:grid-cols-2">
          <form onSubmit={onBehalfSubmit} className="cq-section gap-3 !pt-6">
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
                disabled={isPaused}
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
                disabled={isPaused}
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
                disabled={isPaused}
              />
            </label>
            <Button
              type="submit"
              disabled={onBehalfMutation.isPending || isPaused}
            >
              {onBehalfMutation.isPending
                ? t("reception.booking")
                : t("reception.bookOnBehalf")}
            </Button>
          </form>

          <form
            onSubmit={requestInstantConfirm}
            className="cq-section gap-3 !pt-6"
            noValidate
          >
            <h2 className="cq-section-title">{t("reception.instantEntry")}</h2>
            <p className="text-sm text-muted-foreground">
              {t("reception.reasonAuditHint")}
            </p>
            <label className="cq-label">
              <span>{t("reception.patientRef")}</span>
              <input
                required
                value={instantPatientRef}
                onChange={(e) => setInstantPatientRef(e.target.value)}
                className="cq-field-mono"
                placeholder={t("reception.membershipGuidPlaceholder")}
                aria-label={t("reception.patientRef")}
                disabled={isPaused}
              />
            </label>
            <label className="cq-label">
              <span>{t("reception.reason")}</span>
              <textarea
                required
                value={instantReason}
                onChange={(e) => {
                  setInstantReason(e.target.value);
                  if (instantReasonError && e.target.value.trim()) {
                    setInstantReasonError(null);
                  }
                }}
                rows={2}
                className="cq-field"
                aria-invalid={instantReasonError ? true : undefined}
                aria-describedby="instant-reason-hint"
                disabled={isPaused}
              />
            </label>
            <p
              id="instant-reason-hint"
              className={cn(
                "text-xs",
                instantReasonError
                  ? "text-destructive"
                  : "text-muted-foreground",
              )}
            >
              {instantReasonError ?? t("reception.reasonAuditHint")}
            </p>
            <Button
              type="submit"
              variant="destructive"
              disabled={instantMutation.isPending || isPaused}
            >
              {instantMutation.isPending
                ? t("reception.inserting")
                : t("reception.insertNow")}
            </Button>
          </form>
        </div>

        <div className="cq-emergency-zone">
          <div className="space-y-1">
            <h2 className="cq-section-title text-destructive">
              {t("reception.emergencyPause")}
            </h2>
            <p className="text-sm text-muted-foreground">
              {t("reception.emergencyPauseHint")}
            </p>
          </div>
          {sessionQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">
              {t("reception.loading")}
            </p>
          ) : !session ? (
            <p className="text-sm text-muted-foreground">
              {t("reception.noSession")}
            </p>
          ) : isPaused ? (
            <p className="text-sm font-medium text-warning-foreground">
              {t("reception.pausedUseBanner")}
            </p>
          ) : (
            <form
              className="flex flex-col gap-3 sm:flex-row sm:items-end"
              onSubmit={requestPauseConfirm}
              noValidate
            >
              <label className="cq-label flex-1">
                <span>{t("reception.pauseReason")}</span>
                <input
                  required
                  value={pauseReason}
                  onChange={(e) => {
                    setPauseReason(e.target.value);
                    if (pauseReasonError && e.target.value.trim()) {
                      setPauseReasonError(null);
                    }
                  }}
                  className="cq-field"
                  aria-invalid={pauseReasonError ? true : undefined}
                />
                {pauseReasonError ? (
                  <span className="text-xs text-destructive">
                    {pauseReasonError}
                  </span>
                ) : null}
              </label>
              <Button
                type="submit"
                variant="destructive"
                className="cq-touch-btn"
                disabled={pauseMutation.isPending}
              >
                {pauseMutation.isPending
                  ? t("reception.pausing")
                  : t("reception.pause")}
              </Button>
            </form>
          )}
        </div>
      </div>

      {confirm ? (
        <ConfirmDialog
          title={
            confirm.kind === "absent"
              ? t("reception.confirmAbsentTitle")
              : confirm.kind === "restrict"
                ? t("reception.confirmRestrictTitle")
                : confirm.kind === "instant"
                  ? t("reception.confirmInstantTitle")
                  : t("reception.confirmPauseTitle")
          }
          body={
            confirm.kind === "absent"
              ? t("reception.confirmAbsentBody", { patient: confirm.label })
              : confirm.kind === "restrict"
                ? t("reception.confirmRestrictBody", { patient: confirm.label })
                : confirm.kind === "instant"
                  ? t("reception.confirmInstantBody")
                  : t("reception.confirmPauseBody")
          }
          confirmLabel={
            confirm.kind === "absent"
              ? t("reception.confirmAbsentAction")
              : confirm.kind === "restrict"
                ? t("reception.confirmRestrictAction")
                : confirm.kind === "instant"
                  ? t("reception.confirmInstantAction")
                  : t("reception.confirmPauseAction")
          }
          cancelLabel={t("reception.confirmCancel")}
          busy={confirmBusy}
          destructive
          onCancel={() => setConfirm(null)}
          onConfirm={runConfirmedAction}
        />
      ) : null}
    </section>
  );
}

function StatusStrip({
  isPaused,
  loading,
  currentNumber,
  waiting,
  completed,
  absent,
}: {
  isPaused: boolean;
  loading: boolean;
  currentNumber: number | null;
  waiting: number;
  completed: number;
  absent: number;
}) {
  const { t } = useTranslation();

  return (
    <dl
      className="cq-reception-strip"
      data-paused={isPaused ? "true" : "false"}
      aria-label={t("reception.stripAria")}
    >
      <div
        className="cq-reception-strip-metric"
        data-tone={isPaused ? "paused" : "open"}
      >
        <dt>{t("reception.stripClinic")}</dt>
        <dd>
          {loading
            ? "…"
            : isPaused
              ? t("reception.stripPaused")
              : t("reception.stripOpen")}
        </dd>
      </div>
      <div className="cq-reception-strip-metric">
        <dt>{t("reception.stripCurrent")}</dt>
        <dd className="tabular-nums">
          {loading ? "…" : (currentNumber ?? t("reception.stripNone"))}
        </dd>
      </div>
      <div className="cq-reception-strip-metric">
        <dt>{t("reception.stripWaiting")}</dt>
        <dd className="tabular-nums">{loading ? "…" : waiting}</dd>
      </div>
      <div className="cq-reception-strip-metric">
        <dt>{t("reception.stripCompleted")}</dt>
        <dd className="tabular-nums">{loading ? "…" : completed}</dd>
      </div>
      <div className="cq-reception-strip-metric">
        <dt>{t("reception.stripAbsent")}</dt>
        <dd className="tabular-nums">{loading ? "…" : absent}</dd>
      </div>
    </dl>
  );
}

function QueueRow({
  row,
  busy,
  onConfirm,
  onCall,
  onMarkAbsent,
  onToggleUrgent,
  onRestrict,
}: {
  row: ReceptionAppointment;
  busy: boolean;
  onConfirm: () => void;
  onCall: () => void;
  onMarkAbsent: () => void;
  onToggleUrgent: () => void;
  onRestrict: () => void;
}) {
  const { t } = useTranslation();
  const isUrgent = row.type === "Urgent";
  const canConfirm = row.status === "Booked";
  const canCall = row.status === "Present" && row.realNumber != null;
  const canMarkAbsent = row.status === "Inside";
  const canToggleType = row.realNumber == null && row.status === "Booked";
  const isRestricted = row.membershipStatus === "Restricted";
  const canRestrict =
    Boolean(row.patientMembershipId) &&
    Boolean(row.repeatedAbsenceFlag) &&
    !isRestricted;

  const methodLabel = methodLabelFor(row.method, t);
  const statusLabel = statusLabelFor(row.status, t);

  return (
    <tr
      className="cq-reception-row"
      data-urgent={isUrgent ? "true" : "false"}
    >
      <td className="font-medium tabular-nums">
        <span className="block">{displayNumber(row)}</span>
        {row.realNumber != null ? (
          <span className="text-xs font-normal text-muted-foreground">
            {t("reception.tempNumber", { number: row.temporaryNumber })}
          </span>
        ) : (
          <span className="text-xs font-normal text-muted-foreground">
            {t("reception.tempOnly")}
          </span>
        )}
      </td>
      <td>
        <span className="font-mono text-xs">{patientLabel(row)}</span>
        <span className="mt-0.5 flex flex-wrap gap-1">
          {row.isInstantEntry ? (
            <span className="cq-type-badge text-[0.65rem]">
              {t("reception.instantBadge")}
            </span>
          ) : null}
          {row.possibleUrgentFlag ? (
            <span className="text-xs text-warning-foreground">
              {t("reception.flaggedUrgent")}
            </span>
          ) : null}
          {row.repeatedAbsenceFlag ? (
            <span className="text-xs text-destructive">
              {t("reception.repeatedAbsence", {
                count: row.absenceCount ?? 0,
              })}
            </span>
          ) : null}
          {isRestricted ? (
            <span className="text-xs text-destructive">
              {t("reception.restricted")}
            </span>
          ) : null}
        </span>
      </td>
      <td>
        <span
          className="cq-type-badge"
          data-type={isUrgent ? "Urgent" : "Normal"}
        >
          {isUrgent ? t("reception.urgent") : t("reception.normal")}
        </span>
      </td>
      <td className="text-muted-foreground">{methodLabel}</td>
      <td>{statusLabel}</td>
      <td>
        <div className="flex flex-wrap gap-1.5">
          {canConfirm ? (
            <Button
              type="button"
              size="sm"
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
              size="sm"
              className="cq-touch-btn"
              disabled={busy}
              onClick={onCall}
            >
              {t("reception.callNext")}
            </Button>
          ) : null}
          {canMarkAbsent ? (
            <Button
              type="button"
              size="sm"
              variant="destructive"
              className="cq-touch-btn"
              disabled={busy}
              onClick={onMarkAbsent}
            >
              {t("reception.markAbsent")}
            </Button>
          ) : null}
          {canToggleType ? (
            <Button
              type="button"
              size="sm"
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
          {canRestrict ? (
            <Button
              type="button"
              size="sm"
              variant="outline"
              className="cq-touch-btn"
              disabled={busy}
              onClick={onRestrict}
            >
              {t("reception.restrictBooking")}
            </Button>
          ) : null}
        </div>
      </td>
    </tr>
  );
}

function methodLabelFor(
  method: string,
  t: (key: string) => string,
): string {
  switch (method) {
    case "WalkIn":
      return t("reception.method.WalkIn");
    case "PhoneDeposit":
      return t("reception.method.PhoneDeposit");
    default:
      return method;
  }
}

function statusLabelFor(
  status: string,
  t: (key: string) => string,
): string {
  switch (status) {
    case "Booked":
      return t("reception.status.Booked");
    case "Present":
      return t("reception.status.Present");
    case "Inside":
      return t("reception.status.Inside");
    case "Completed":
      return t("reception.status.Completed");
    case "Absent":
      return t("reception.status.Absent");
    case "Cancelled":
      return t("reception.status.Cancelled");
    case "CarriedOver":
      return t("reception.status.CarriedOver");
    default:
      return status;
  }
}

function ConfirmDialog({
  title,
  body,
  confirmLabel,
  cancelLabel,
  busy,
  destructive,
  onCancel,
  onConfirm,
}: {
  title: string;
  body: string;
  confirmLabel: string;
  cancelLabel: string;
  busy: boolean;
  destructive?: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const { t } = useTranslation();
  const titleId = useId();
  const panelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const focusable = panelRef.current?.querySelector<HTMLElement>(
      "button[data-confirm-cancel]",
    );
    focusable?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) {
        onCancel();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("keydown", onKey);
      previouslyFocused?.focus();
    };
  }, [busy, onCancel]);

  return (
    <div
      className="cq-confirm-backdrop"
      role="presentation"
      onClick={() => {
        if (!busy) {
          onCancel();
        }
      }}
    >
      <div
        ref={panelRef}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="cq-confirm-panel"
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id={titleId} className="text-lg font-semibold text-foreground">
          {title}
        </h2>
        <p className="text-sm text-muted-foreground">{body}</p>
        <div className="flex flex-wrap justify-end gap-2">
          <Button
            type="button"
            variant="outline"
            disabled={busy}
            data-confirm-cancel=""
            onClick={onCancel}
          >
            {cancelLabel}
          </Button>
          <Button
            type="button"
            variant={destructive ? "destructive" : "default"}
            disabled={busy}
            onClick={onConfirm}
          >
            {busy ? t("reception.confirmWorking") : confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}
