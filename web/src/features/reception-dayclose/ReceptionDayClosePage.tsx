import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
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
  type UnservedAppointment,
} from "./api";

export function ReceptionDayClosePage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { id: clinicId } = useActiveClinic();
  const [actionError, setActionError] = useState<string | null>(null);
  const [unserved, setUnserved] = useState<UnservedAppointment[] | null>(null);
  const [autoCarried, setAutoCarried] = useState<number | null>(null);

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
      setUnserved((prev) =>
        prev ? prev.filter((row) => row.appointmentId !== appointmentId) : prev,
      );
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const pendingDeposits =
    queueQuery.data?.filter(
      (row) =>
        row.method === "PhoneDeposit" &&
        !row.depositConfirmed &&
        row.status !== "Cancelled" &&
        row.status !== "CarriedOver",
    ) ?? [];

  const session = sessionQuery.data;
  const sessionClosed = session?.status === "Closed";

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

      <section className="cq-section" aria-labelledby="dayclose-session">
        <h2 id="dayclose-session" className="cq-section-title">
          {t("receptionDayclose.session")}
        </h2>
        {sessionQuery.isLoading ? (
          <p className="cq-status">{t("receptionDayclose.loading")}</p>
        ) : !session ? (
          <p className="cq-status">{t("receptionDayclose.noSession")}</p>
        ) : (
          <div className="flex flex-wrap items-center gap-3">
            <p className="cq-status">
              {t("receptionDayclose.sessionStatus", { status: session.status })}
            </p>
            {!sessionClosed ? (
              <Button
                type="button"
                className="cq-touch-btn"
                disabled={closeMutation.isPending}
                onClick={() => closeMutation.mutate(session.queueSessionId)}
              >
                {closeMutation.isPending
                  ? t("receptionDayclose.closing")
                  : t("receptionDayclose.closeDay")}
              </Button>
            ) : null}
          </div>
        )}
        {autoCarried != null ? (
          <p className="cq-status mt-2">
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
              <li key={row.appointmentId} className="flex flex-wrap items-center gap-2">
                <span>
                  {t("receptionDayclose.tempNumber", {
                    number: row.temporaryNumber,
                  })}{" "}
                  · {row.status}
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
        {unserved == null ? (
          <p className="cq-status">{t("receptionDayclose.closeFirst")}</p>
        ) : unserved.length === 0 ? (
          <p className="cq-status">{t("receptionDayclose.noUnserved")}</p>
        ) : (
          <ul className="cq-list">
            {unserved.map((row) => (
              <li
                key={row.appointmentId}
                className="flex flex-wrap items-center gap-2"
              >
                <span>
                  {t("receptionDayclose.tempNumber", {
                    number: row.temporaryNumber,
                  })}{" "}
                  · {row.status}
                  {row.depositConfirmed
                    ? ` · ${t("receptionDayclose.depositOk")}`
                    : null}
                </span>
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
                  onClick={() => cancelMutation.mutate(row.appointmentId)}
                >
                  {t("receptionDayclose.cancel")}
                </Button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
