import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useActiveClinic } from "@/features/auth/AuthProvider";

import {
  cancelUnserved,
  carryOver,
  closeDay,
  confirmDeposit,
  getTodaySession,
  listTodayAppointments,
  type ReceptionAppointment,
  type UnservedAppointment,
} from "./api";

function isWaitingStatus(status: string): boolean {
  return status === "Booked" || status === "Present" || status === "Inside";
}

function isUnservedStatus(status: string): boolean {
  return (
    status === "Booked" ||
    status === "Present" ||
    status === "Inside" ||
    status === "Absent"
  );
}

function isAutoCarryCandidate(row: ReceptionAppointment): boolean {
  return (
    row.method === "PhoneDeposit" &&
    Boolean(row.depositConfirmed) &&
    row.status === "Booked"
  );
}

export function ReceptionDayClosePage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { id: clinicId } = useActiveClinic();
  const [actionError, setActionError] = useState<string | null>(null);
  const [unserved, setUnserved] = useState<UnservedAppointment[] | null>(null);
  const [autoCarried, setAutoCarried] = useState<number | null>(null);
  const [confirmCloseOpen, setConfirmCloseOpen] = useState(false);
  const [cancelTargetId, setCancelTargetId] = useState<string | null>(null);

  const sessionQuery = useQuery({
    queryKey: ["dayclose-session", clinicId],
    queryFn: getTodaySession,
    refetchInterval: 10_000,
  });

  const queueQuery = useQuery({
    queryKey: ["dayclose-today", clinicId],
    queryFn: listTodayAppointments,
    refetchInterval: 5_000,
  });

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ["dayclose-session"] });
    await queryClient.invalidateQueries({ queryKey: ["dayclose-today"] });
    await queryClient.invalidateQueries({ queryKey: ["reception-today"] });
  };

  const confirmDepositMutation = useMutation({
    mutationFn: confirmDeposit,
    onSuccess: async () => {
      setActionError(null);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const closeMutation = useMutation({
    mutationFn: closeDay,
    onSuccess: async (result) => {
      setActionError(null);
      setConfirmCloseOpen(false);
      setUnserved(result.unservedAppointments);
      setAutoCarried(result.autoCarriedOverCount);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const carryMutation = useMutation({
    mutationFn: carryOver,
    onSuccess: async (_, appointmentId) => {
      setActionError(null);
      setUnserved((prev) =>
        prev ? prev.filter((row) => row.appointmentId !== appointmentId) : prev,
      );
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const cancelMutation = useMutation({
    mutationFn: cancelUnserved,
    onSuccess: async (_, appointmentId) => {
      setActionError(null);
      setCancelTargetId(null);
      setUnserved((prev) =>
        prev ? prev.filter((row) => row.appointmentId !== appointmentId) : prev,
      );
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const appointments = queueQuery.data ?? [];

  const summary = useMemo(() => {
    let served = 0;
    let absent = 0;
    let waiting = 0;
    let autoCarry = 0;
    let decisionNeeded = 0;

    for (const row of appointments) {
      if (row.status === "Completed") served += 1;
      if (row.status === "Absent") absent += 1;
      if (isWaitingStatus(row.status)) waiting += 1;
      if (isAutoCarryCandidate(row)) {
        autoCarry += 1;
      } else if (isUnservedStatus(row.status)) {
        decisionNeeded += 1;
      }
    }

    return { served, absent, waiting, autoCarry, decisionNeeded };
  }, [appointments]);

  const pendingDeposits = appointments.filter(
    (row) =>
      row.method === "PhoneDeposit" &&
      !row.depositConfirmed &&
      row.status !== "Cancelled" &&
      row.status !== "CarriedOver",
  );

  const session = sessionQuery.data;
  const sessionClosed = session?.status === "Closed";
  const sessionError = sessionQuery.isError || queueQuery.isError;

  const statusLabel = (status: string) => {
    const key = `receptionDayclose.apptStatus.${status}`;
    const translated = t(key);
    return translated === key ? status : translated;
  };

  const sessionStatusLabel = (status: string) => {
    const key = `receptionDayclose.sessionStates.${status}`;
    const translated = t(key);
    return translated === key ? status : translated;
  };

  return (
    <div className="cq-page">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("receptionDayclose.title")}</h1>
        <p className="cq-page-subtitle">{t("receptionDayclose.subtitle")}</p>
      </header>

      {actionError ? (
        <p className="cq-alert" role="alert">
          {actionError}
        </p>
      ) : null}

      {sessionError ? (
        <div className="cq-alert" role="alert">
          <p className="font-medium">{t("receptionDayclose.errorTitle")}</p>
          <p className="mt-1 text-sm">{t("receptionDayclose.error")}</p>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="mt-2"
            onClick={() => {
              void sessionQuery.refetch();
              void queueQuery.refetch();
            }}
          >
            {t("receptionDayclose.retry")}
          </Button>
        </div>
      ) : null}

      <section className="cq-section" aria-labelledby="dayclose-summary">
        <h2 id="dayclose-summary" className="cq-section-title">
          {t("receptionDayclose.summaryTitle")}
        </h2>
        {queueQuery.isLoading ? (
          <p className="cq-status">{t("receptionDayclose.loading")}</p>
        ) : (
          <dl className="cq-summary-grid">
            <div className="cq-summary-stat">
              <dd className="cq-summary-stat__value">{summary.served}</dd>
              <dt className="cq-summary-stat__label">
                {t("receptionDayclose.summaryServed")}
              </dt>
            </div>
            <div className="cq-summary-stat">
              <dd className="cq-summary-stat__value">{summary.absent}</dd>
              <dt className="cq-summary-stat__label">
                {t("receptionDayclose.summaryAbsent")}
              </dt>
            </div>
            <div className="cq-summary-stat">
              <dd className="cq-summary-stat__value">{summary.waiting}</dd>
              <dt className="cq-summary-stat__label">
                {t("receptionDayclose.summaryWaiting")}
              </dt>
            </div>
          </dl>
        )}
      </section>

      <section className="cq-section" aria-labelledby="dayclose-session">
        <h2 id="dayclose-session" className="cq-section-title">
          {t("receptionDayclose.session")}
        </h2>
        {sessionQuery.isLoading ? (
          <p className="cq-status">{t("receptionDayclose.loading")}</p>
        ) : !session ? (
          <p className="cq-status">{t("receptionDayclose.noSession")}</p>
        ) : (
          <div className="flex flex-col gap-3">
            <p className="cq-status">
              {t("receptionDayclose.sessionStatus", {
                status: sessionStatusLabel(session.status),
              })}
            </p>
            <div className="cq-callout-info" role="note">
              <p className="font-semibold">{t("receptionDayclose.fr065Title")}</p>
              <p className="mt-1">{t("receptionDayclose.fr065Body")}</p>
            </div>
            {!sessionClosed ? (
              <div>
                <Button
                  type="button"
                  variant="destructive"
                  className="cq-touch-btn"
                  disabled={closeMutation.isPending}
                  onClick={() => setConfirmCloseOpen(true)}
                >
                  {t("receptionDayclose.closeDay")}
                </Button>
              </div>
            ) : null}
          </div>
        )}
        {autoCarried != null ? (
          <p className="cq-status mt-3">
            {t("receptionDayclose.autoCarried", { count: autoCarried })}
          </p>
        ) : null}
      </section>

      <section className="cq-section" aria-labelledby="dayclose-deposits">
        <h2 id="dayclose-deposits" className="cq-section-title">
          {t("receptionDayclose.deposits")}
        </h2>
        {queueQuery.isLoading ? (
          <p className="cq-status">{t("receptionDayclose.loading")}</p>
        ) : pendingDeposits.length === 0 ? (
          <p className="cq-status">{t("receptionDayclose.noDeposits")}</p>
        ) : (
          <ul className="cq-list">
            {pendingDeposits.map((row) => (
              <li
                key={row.appointmentId}
                className="flex flex-wrap items-center gap-2 py-3"
              >
                <span>
                  {t("receptionDayclose.tempNumber", {
                    number: row.temporaryNumber,
                  })}{" "}
                  · {statusLabel(row.status)}
                </span>
                <Button
                  type="button"
                  size="sm"
                  disabled={confirmDepositMutation.isPending}
                  onClick={() =>
                    confirmDepositMutation.mutate(row.appointmentId)
                  }
                >
                  {t("receptionDayclose.confirmDeposit")}
                </Button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="cq-section" aria-labelledby="dayclose-unserved">
        <h2 id="dayclose-unserved" className="cq-section-title">
          {t("receptionDayclose.unserved")}
        </h2>
        <p className="mb-3 text-sm text-muted-foreground">
          {t("receptionDayclose.unservedHint")}
        </p>
        {unserved == null ? (
          <p className="cq-status">{t("receptionDayclose.closeFirst")}</p>
        ) : unserved.length === 0 ? (
          <p className="cq-empty-friendly">{t("receptionDayclose.noUnserved")}</p>
        ) : (
          <ul className="flex flex-col gap-3">
            {unserved.map((row) => (
              <li key={row.appointmentId} className="cq-booking-card">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-medium">
                    {t("receptionDayclose.tempNumber", {
                      number: row.temporaryNumber,
                    })}
                  </span>
                  {row.realNumber != null ? (
                    <span className="text-sm text-muted-foreground">
                      {t("receptionDayclose.realNumber", {
                        number: row.realNumber,
                      })}
                    </span>
                  ) : null}
                  <span className="cq-type-badge" data-type={row.type}>
                    {row.type === "Urgent"
                      ? t("receptionDayclose.typeUrgent")
                      : t("receptionDayclose.typeNormal")}
                  </span>
                  <span className="text-sm text-muted-foreground">
                    {statusLabel(row.status)}
                  </span>
                  {row.depositConfirmed ? (
                    <span className="text-sm text-success">
                      {t("receptionDayclose.depositOk")}
                    </span>
                  ) : null}
                </div>
                <div className="flex flex-wrap gap-2">
                  <Button
                    type="button"
                    size="sm"
                    disabled={carryMutation.isPending}
                    onClick={() => carryMutation.mutate(row.appointmentId)}
                  >
                    {t("receptionDayclose.carryOver")}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    disabled={cancelMutation.isPending}
                    onClick={() => setCancelTargetId(row.appointmentId)}
                  >
                    {t("receptionDayclose.cancel")}
                  </Button>
                </div>
                <p className="text-xs text-muted-foreground">
                  {t("receptionDayclose.carryDefaultHint")}
                </p>
              </li>
            ))}
          </ul>
        )}
      </section>

      {confirmCloseOpen && session ? (
        <div
          className="cq-confirm-backdrop"
          role="dialog"
          aria-modal="true"
          aria-labelledby="dayclose-confirm-title"
        >
          <div className="cq-confirm-panel">
            <h3 id="dayclose-confirm-title" className="cq-section-title">
              {t("receptionDayclose.confirmTitle")}
            </h3>
            <p className="text-sm text-destructive">
              {t("receptionDayclose.confirmIrreversible")}
            </p>
            <ul className="list-inside list-disc space-y-1 text-sm text-foreground">
              <li>
                {t("receptionDayclose.confirmAutoCarry", {
                  count: summary.autoCarry,
                })}
              </li>
              <li>
                {t("receptionDayclose.confirmNotified", {
                  count: summary.autoCarry,
                })}
              </li>
              <li>
                {t("receptionDayclose.confirmRemaining", {
                  count: summary.decisionNeeded,
                })}
              </li>
            </ul>
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                variant="destructive"
                disabled={closeMutation.isPending}
                onClick={() => closeMutation.mutate(session.queueSessionId)}
              >
                {closeMutation.isPending
                  ? t("receptionDayclose.closing")
                  : t("receptionDayclose.confirmClose")}
              </Button>
              <Button
                type="button"
                variant="outline"
                disabled={closeMutation.isPending}
                onClick={() => setConfirmCloseOpen(false)}
              >
                {t("receptionDayclose.confirmBack")}
              </Button>
            </div>
          </div>
        </div>
      ) : null}

      {cancelTargetId ? (
        <div
          className="cq-confirm-backdrop"
          role="dialog"
          aria-modal="true"
          aria-labelledby="dayclose-cancel-title"
        >
          <div className="cq-confirm-panel">
            <h3 id="dayclose-cancel-title" className="cq-section-title">
              {t("receptionDayclose.cancelConfirmTitle")}
            </h3>
            <p className="text-sm text-muted-foreground">
              {t("receptionDayclose.cancelConfirmBody")}
            </p>
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                variant="destructive"
                disabled={cancelMutation.isPending}
                onClick={() => cancelMutation.mutate(cancelTargetId)}
              >
                {t("receptionDayclose.cancelConfirmYes")}
              </Button>
              <Button
                type="button"
                variant="outline"
                disabled={cancelMutation.isPending}
                onClick={() => setCancelTargetId(null)}
              >
                {t("receptionDayclose.cancelConfirmNo")}
              </Button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
