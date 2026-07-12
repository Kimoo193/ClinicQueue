import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  useEffect,
  useId,
  useRef,
  useState,
  type FormEvent,
} from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useActiveClinic } from "@/features/auth/AuthProvider";

import {
  callNext,
  createVisit,
  getTodaySession,
  getVisit,
  issuePrescription,
  listTodayAppointments,
  pauseSession,
  resumeSession,
  type DoctorAppointment,
  type VisitResponse,
} from "./api";

type Props = {
  /** When true, clinical fields are view-only (ClinicOwner oversight). */
  readOnlyClinical?: boolean;
};

type DayMetrics = {
  served: number;
  waiting: number;
  absent: number;
  urgent: number;
};

function deriveMetrics(rows: DoctorAppointment[]): DayMetrics {
  let served = 0;
  let waiting = 0;
  let absent = 0;
  let urgent = 0;
  for (const row of rows) {
    if (row.type === "Urgent") urgent += 1;
    if (row.status === "Completed") served += 1;
    else if (row.status === "Absent") absent += 1;
    else if (
      row.status === "Booked" ||
      row.status === "Present" ||
      row.status === "Inside"
    ) {
      waiting += 1;
    }
  }
  return { served, waiting, absent, urgent };
}

export function DoctorConsolePage({
  readOnlyClinical = false,
}: Props = {}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { id: clinicId } = useActiveClinic();
  const [actionError, setActionError] = useState<string | null>(null);
  const [pauseReason, setPauseReason] = useState("");
  const [pauseReasonError, setPauseReasonError] = useState<string | null>(null);
  const [confirmPause, setConfirmPause] = useState(false);
  const [selectedAppointmentId, setSelectedAppointmentId] = useState<
    string | null
  >(null);
  const [diagnosis, setDiagnosis] = useState("");
  const [notes, setNotes] = useState("");
  const [drug, setDrug] = useState("");
  const [dose, setDose] = useState("");
  const [frequency, setFrequency] = useState("");
  const [duration, setDuration] = useState("");
  const [loadedVisit, setLoadedVisit] = useState<VisitResponse | null>(null);
  const [visitSaving, setVisitSaving] = useState(false);
  const diagnosisRef = useRef<HTMLInputElement>(null);

  const sessionQuery = useQuery({
    queryKey: ["doctor-session", clinicId],
    queryFn: getTodaySession,
    refetchInterval: 5_000,
  });

  const queueQuery = useQuery({
    queryKey: ["doctor-today", clinicId],
    queryFn: listTodayAppointments,
    refetchInterval: 5_000,
  });

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ["doctor-session"] });
    await queryClient.invalidateQueries({ queryKey: ["doctor-today"] });
  };

  const pauseMutation = useMutation({
    mutationFn: ({ sessionId, reason }: { sessionId: string; reason: string }) =>
      pauseSession(sessionId, reason),
    onSuccess: async () => {
      setActionError(null);
      setPauseReason("");
      setPauseReasonError(null);
      setConfirmPause(false);
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

  const callMutation = useMutation({
    mutationFn: callNext,
    onSuccess: async () => {
      setActionError(null);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const loadVisit = async (appointmentId: string) => {
    setSelectedAppointmentId(appointmentId);
    setActionError(null);
    setLoadedVisit(null);
    setDiagnosis("");
    setNotes("");
    setDrug("");
    setDose("");
    setFrequency("");
    setDuration("");
    try {
      const visit = await getVisit(appointmentId);
      setLoadedVisit(visit);
      setDiagnosis(visit.diagnosis);
      setNotes(visit.notes);
    } catch {
      setLoadedVisit(null);
      queueMicrotask(() => diagnosisRef.current?.focus());
    }
  };

  const onCreateVisit = async (event: FormEvent) => {
    event.preventDefault();
    if (!selectedAppointmentId || readOnlyClinical) {
      return;
    }
    setVisitSaving(true);
    try {
      const created = await createVisit(
        selectedAppointmentId,
        diagnosis,
        notes,
      );
      setActionError(null);
      const visit = await getVisit(selectedAppointmentId);
      setLoadedVisit({ ...visit, visitId: created.visitId });
    } catch (error) {
      setActionError(error instanceof Error ? error.message : String(error));
    } finally {
      setVisitSaving(false);
    }
  };

  const onIssuePrescription = async (event: FormEvent) => {
    event.preventDefault();
    if (!loadedVisit || readOnlyClinical) {
      return;
    }
    setVisitSaving(true);
    try {
      await issuePrescription(loadedVisit.visitId, {
        drug,
        dose,
        frequency,
        duration,
      });
      setActionError(null);
      const visit = await getVisit(loadedVisit.appointmentId);
      setLoadedVisit(visit);
      setDrug("");
      setDose("");
      setFrequency("");
      setDuration("");
    } catch (error) {
      setActionError(error instanceof Error ? error.message : String(error));
    } finally {
      setVisitSaving(false);
    }
  };

  const requestPause = () => {
    if (!pauseReason.trim()) {
      setPauseReasonError(t("doctor.pauseReasonRequired"));
      return;
    }
    setPauseReasonError(null);
    setConfirmPause(true);
  };

  const session = sessionQuery.data;
  const appointments = queueQuery.data ?? [];
  const isPaused = session?.status === "Paused";
  const metrics = deriveMetrics(appointments);
  const repeatedAbsenceRows = appointments.filter(
    (row) =>
      Boolean(row.repeatedAbsenceFlag) || (row.absenceCount ?? 0) >= 2,
  );

  return (
    <section
      className="cq-page cq-doctor-console"
      data-paused={isPaused ? "true" : "false"}
    >
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("doctor.title")}</h1>
        <p className="cq-page-subtitle">{t("doctor.subtitle")}</p>
        {readOnlyClinical ? (
          <p className="cq-banner-warning" role="status">
            {t("doctor.readOnlyBanner")}
          </p>
        ) : null}
      </header>

      {isPaused ? (
        <div className="cq-paused-banner" role="status" aria-live="assertive">
          <div className="space-y-1">
            <p className="text-base font-semibold">
              {t("doctor.pausedBannerTitle")}
            </p>
            <p className="text-sm">
              {t("doctor.pausedBannerBody")}
              {session?.pauseReason ? ` — ${session.pauseReason}` : ""}
            </p>
          </div>
          {session && !readOnlyClinical ? (
            <Button
              type="button"
              size="lg"
              className="cq-touch-btn shrink-0"
              disabled={resumeMutation.isPending}
              onClick={() => resumeMutation.mutate(session.queueSessionId)}
            >
              {resumeMutation.isPending
                ? t("doctor.resuming")
                : t("doctor.resume")}
            </Button>
          ) : null}
        </div>
      ) : null}

      <dl className="cq-doctor-summary" aria-label={t("doctor.summaryAria")}>
        <SummaryMetric
          label={t("doctor.metricServed")}
          value={
            sessionQuery.isLoading || queueQuery.isLoading
              ? "…"
              : String(metrics.served)
          }
        />
        <SummaryMetric
          label={t("doctor.metricWaiting")}
          value={
            sessionQuery.isLoading || queueQuery.isLoading
              ? "…"
              : String(metrics.waiting)
          }
        />
        <SummaryMetric
          label={t("doctor.metricAbsent")}
          value={
            sessionQuery.isLoading || queueQuery.isLoading
              ? "…"
              : String(metrics.absent)
          }
        />
        <SummaryMetric
          label={t("doctor.metricUrgent")}
          value={
            sessionQuery.isLoading || queueQuery.isLoading
              ? "…"
              : String(metrics.urgent)
          }
          tone="urgent"
        />
      </dl>

      {actionError ? (
        <p className="cq-alert" role="alert">
          {actionError}
        </p>
      ) : null}

      {queueQuery.isError ? (
        <p className="cq-alert" role="alert">
          {t("doctor.loadError")}
        </p>
      ) : null}

      {!readOnlyClinical ? (
        <div className="cq-emergency-zone">
          <h2 className="cq-section-title">{t("doctor.emergency")}</h2>
          <p className="text-sm text-muted-foreground">
            {t("doctor.emergencyHint")}
          </p>
          {sessionQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">{t("doctor.loading")}</p>
          ) : !session ? (
            <p className="text-sm text-muted-foreground">{t("doctor.noSession")}</p>
          ) : isPaused ? (
            <p className="text-sm">{t("doctor.pausedUseBanner")}</p>
          ) : (
            <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
              <label className="cq-label flex-1">
                <span>{t("doctor.pauseReason")}</span>
                <input
                  value={pauseReason}
                  onChange={(e) => {
                    setPauseReason(e.target.value);
                    if (pauseReasonError) setPauseReasonError(null);
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
                type="button"
                variant="destructive"
                className="cq-touch-btn"
                disabled={pauseMutation.isPending}
                onClick={requestPause}
              >
                {t("doctor.pause")}
              </Button>
            </div>
          )}
        </div>
      ) : null}

      {repeatedAbsenceRows.length > 0 ? (
        <div className="cq-banner-warning" role="status">
          <p className="font-medium">{t("doctor.repeatedAbsenceTitle")}</p>
          <ul className="mt-2 space-y-1 text-sm">
            {repeatedAbsenceRows.map((row) => (
              <li key={row.appointmentId}>
                {t("doctor.tempNumber", { number: row.temporaryNumber })}
                {row.realNumber != null
                  ? ` · ${t("doctor.realNumber", { number: row.realNumber })}`
                  : ""}
                {" — "}
                {t("doctor.repeatedAbsence", {
                  count: row.absenceCount ?? 0,
                })}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <div className="cq-section">
        <h2 className="cq-section-title">{t("doctor.queue")}</h2>
        {queueQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">{t("doctor.loading")}</p>
        ) : appointments.length === 0 ? (
          <p className="rounded-xl border border-dashed border-border bg-surface px-4 py-8 text-center text-sm text-muted-foreground">
            {t("doctor.empty")}
          </p>
        ) : (
          <ul className="cq-list">
            {appointments.map((row) => {
              const canCall =
                !readOnlyClinical &&
                row.status === "Present" &&
                row.realNumber != null;
              const isSelected = selectedAppointmentId === row.appointmentId;
              return (
                <li
                  key={row.appointmentId}
                  className="flex flex-col gap-2 py-3 sm:flex-row sm:items-center sm:justify-between"
                >
                  <div className="text-sm">
                    <p className="font-medium tabular-nums">
                      {row.realNumber != null
                        ? t("doctor.realNumber", { number: row.realNumber })
                        : t("doctor.tempNumber", {
                            number: row.temporaryNumber,
                          })}
                      <span
                        className="cq-type-badge ms-2"
                        data-type={
                          row.type === "Urgent" ? "Urgent" : "Normal"
                        }
                      >
                        {row.type === "Urgent"
                          ? t("doctor.typeUrgent")
                          : t("doctor.typeNormal")}
                      </span>
                    </p>
                    <p className="text-muted-foreground">
                      {t(`doctor.status.${row.status}`, {
                        defaultValue: row.status,
                      })}
                      {row.repeatedAbsenceFlag ||
                      (row.absenceCount ?? 0) >= 2 ? (
                        <>
                          {" · "}
                          <span className="text-destructive">
                            {t("doctor.repeatedAbsence", {
                              count: row.absenceCount ?? 0,
                            })}
                          </span>
                        </>
                      ) : null}
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {canCall ? (
                      <Button
                        type="button"
                        className="cq-touch-btn"
                        disabled={callMutation.isPending}
                        onClick={() => callMutation.mutate(row.appointmentId)}
                      >
                        {t("doctor.callNext")}
                      </Button>
                    ) : null}
                    <Button
                      type="button"
                      className="cq-touch-btn"
                      variant={isSelected ? "default" : "outline"}
                      onClick={() => void loadVisit(row.appointmentId)}
                    >
                      {t("doctor.openVisit")}
                    </Button>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </div>

      {selectedAppointmentId ? (
        <div className="cq-section space-y-4">
          <h3 className="cq-section-title">{t("doctor.visitForm")}</h3>
          <p className="text-sm text-muted-foreground">
            {t("doctor.visitHint")}
          </p>
          {loadedVisit ? (
            <div className="space-y-4 text-sm">
              <p>
                <span className="text-muted-foreground">
                  {t("doctor.diagnosis")}:{" "}
                </span>
                {loadedVisit.diagnosis}
              </p>
              <p>
                <span className="text-muted-foreground">
                  {t("doctor.notes")}:{" "}
                </span>
                {loadedVisit.notes || "—"}
              </p>
              {loadedVisit.prescription ? (
                <div className="cq-rx-card">
                  <p className="font-medium">{t("doctor.prescription")}</p>
                  <p>
                    {loadedVisit.prescription.drug} ·{" "}
                    {loadedVisit.prescription.dose} ·{" "}
                    {loadedVisit.prescription.frequency} ·{" "}
                    {loadedVisit.prescription.duration}
                  </p>
                </div>
              ) : !readOnlyClinical ? (
                <form
                  onSubmit={onIssuePrescription}
                  className="grid gap-3 sm:grid-cols-2"
                >
                  <h4 className="font-medium sm:col-span-2">
                    {t("doctor.issuePrescription")}
                  </h4>
                  <label className="cq-label">
                    <span>{t("doctor.drug")}</span>
                    <input
                      required
                      autoComplete="off"
                      value={drug}
                      onChange={(e) => setDrug(e.target.value)}
                      className="cq-field"
                    />
                  </label>
                  <label className="cq-label">
                    <span>{t("doctor.dose")}</span>
                    <input
                      required
                      autoComplete="off"
                      value={dose}
                      onChange={(e) => setDose(e.target.value)}
                      className="cq-field"
                    />
                  </label>
                  <label className="cq-label">
                    <span>{t("doctor.frequency")}</span>
                    <input
                      required
                      autoComplete="off"
                      value={frequency}
                      onChange={(e) => setFrequency(e.target.value)}
                      className="cq-field"
                    />
                  </label>
                  <label className="cq-label">
                    <span>{t("doctor.duration")}</span>
                    <input
                      required
                      autoComplete="off"
                      value={duration}
                      onChange={(e) => setDuration(e.target.value)}
                      className="cq-field"
                    />
                  </label>
                  <div className="sm:col-span-2">
                    <Button type="submit" disabled={visitSaving}>
                      {visitSaving
                        ? t("doctor.saving")
                        : t("doctor.savePrescription")}
                    </Button>
                  </div>
                </form>
              ) : null}
            </div>
          ) : readOnlyClinical ? (
            <p className="text-sm text-muted-foreground">
              {t("doctor.noVisitYet")}
            </p>
          ) : (
            <form onSubmit={onCreateVisit} className="flex flex-col gap-3">
              <label className="cq-label">
                <span>{t("doctor.diagnosis")}</span>
                <input
                  ref={diagnosisRef}
                  required
                  autoComplete="off"
                  value={diagnosis}
                  onChange={(e) => setDiagnosis(e.target.value)}
                  className="cq-field"
                />
              </label>
              <label className="cq-label">
                <span>{t("doctor.notes")}</span>
                <textarea
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  rows={3}
                  className="cq-field"
                />
              </label>
              <Button type="submit" disabled={visitSaving}>
                {visitSaving ? t("doctor.saving") : t("doctor.saveVisit")}
              </Button>
            </form>
          )}
        </div>
      ) : null}

      {confirmPause && session ? (
        <ConfirmDialog
          title={t("doctor.confirmPauseTitle")}
          body={t("doctor.confirmPauseBody", { reason: pauseReason.trim() })}
          confirmLabel={t("doctor.pause")}
          cancelLabel={t("doctor.confirmCancel")}
          busy={pauseMutation.isPending}
          destructive
          onCancel={() => setConfirmPause(false)}
          onConfirm={() =>
            pauseMutation.mutate({
              sessionId: session.queueSessionId,
              reason: pauseReason.trim(),
            })
          }
        />
      ) : null}
    </section>
  );
}

function SummaryMetric({
  label,
  value,
  tone,
}: {
  label: string;
  value: string;
  tone?: "urgent";
}) {
  return (
    <div
      className="cq-doctor-summary-metric"
      data-tone={tone}
    >
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
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
        if (!busy) onCancel();
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
            {busy ? t("doctor.confirmWorking") : confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}
