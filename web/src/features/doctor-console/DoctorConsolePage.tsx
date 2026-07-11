import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useActiveClinic } from "@/features/auth/AuthProvider";

import {
  createVisit,
  getTodaySession,
  getVisit,
  issuePrescription,
  listTodayAppointments,
  pauseSession,
  resumeSession,
  type VisitResponse,
} from "./api";

type Props = {
  /** When true, clinical fields are view-only (ClinicOwner oversight). */
  readOnlyClinical?: boolean;
};

export function DoctorConsolePage({
  readOnlyClinical = false,
}: Props = {}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { id: clinicId } = useActiveClinic();
  const [actionError, setActionError] = useState<string | null>(null);
  const [pauseReason, setPauseReason] = useState("");
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
      // No visit yet — doctor can create one.
      setLoadedVisit(null);
    }
  };

  const onCreateVisit = async (event: FormEvent) => {
    event.preventDefault();
    if (!selectedAppointmentId || readOnlyClinical) {
      return;
    }
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
    }
  };

  const onIssuePrescription = async (event: FormEvent) => {
    event.preventDefault();
    if (!loadedVisit || readOnlyClinical) {
      return;
    }
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
    }
  };

  const session = sessionQuery.data;
  const appointments = queueQuery.data ?? [];
  const isPaused = session?.status === "Paused";

  return (
    <section className="cq-page">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("doctor.title")}</h1>
        <p className="cq-page-subtitle">{t("doctor.subtitle")}</p>
        {readOnlyClinical ? (
          <p className="cq-banner-warning" role="status">
            {t("doctor.readOnlyBanner")}
          </p>
        ) : null}
      </header>

      {actionError ? (
        <p className="cq-alert" role="alert">
          {actionError}
        </p>
      ) : null}

      <div className="cq-section">
        <h2 className="cq-section-title">{t("doctor.emergency")}</h2>
        {sessionQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">{t("doctor.loading")}</p>
        ) : !session ? (
          <p className="text-sm text-muted-foreground">{t("doctor.noSession")}</p>
        ) : (
          <div className="space-y-3">
            <p className="text-sm">
              {t("doctor.sessionStatus", { status: session.status })}
              {session.pauseReason ? ` — ${session.pauseReason}` : ""}
            </p>
            {!isPaused ? (
              <form
                className="flex flex-col gap-2 sm:flex-row sm:items-end"
                onSubmit={(event) => {
                  event.preventDefault();
                  pauseMutation.mutate({
                    sessionId: session.queueSessionId,
                    reason: pauseReason,
                  });
                }}
              >
                <label className="cq-label flex-1">
                  <span>{t("doctor.pauseReason")}</span>
                  <input
                    required
                    value={pauseReason}
                    onChange={(e) => setPauseReason(e.target.value)}
                    className="cq-field"
                  />
                </label>
                <Button type="submit" disabled={pauseMutation.isPending}>
                  {pauseMutation.isPending
                    ? t("doctor.pausing")
                    : t("doctor.pause")}
                </Button>
              </form>
            ) : (
              <Button
                type="button"
                disabled={resumeMutation.isPending}
                onClick={() => resumeMutation.mutate(session.queueSessionId)}
              >
                {resumeMutation.isPending
                  ? t("doctor.resuming")
                  : t("doctor.resume")}
              </Button>
            )}
          </div>
        )}
      </div>

      <div className="cq-section">
        <h2 className="cq-section-title">{t("doctor.clinical")}</h2>
        {queueQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">{t("doctor.loading")}</p>
        ) : appointments.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("doctor.empty")}</p>
        ) : (
          <ul className="cq-list">
            {appointments.map((row) => (
              <li
                key={row.appointmentId}
                className="flex flex-col gap-2 py-3 sm:flex-row sm:items-center sm:justify-between"
              >
                <div className="text-sm">
                  <p className="font-medium">
                    {t("doctor.tempNumber", { number: row.temporaryNumber })}
                    {row.realNumber != null
                      ? ` · ${t("doctor.realNumber", { number: row.realNumber })}`
                      : ""}
                  </p>
                  <p className="text-muted-foreground">
                    {row.status} · {row.type}
                  </p>
                </div>
                <Button
                  type="button"
                  className="cq-touch-btn"
                  variant={
                    selectedAppointmentId === row.appointmentId
                      ? "default"
                      : "outline"
                  }
                  onClick={() => void loadVisit(row.appointmentId)}
                >
                  {t("doctor.openVisit")}
                </Button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {selectedAppointmentId ? (
        <div className="cq-section space-y-6">
          <h3 className="cq-section-title">{t("doctor.visitForm")}</h3>
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
                <p>
                  <span className="text-muted-foreground">
                    {t("doctor.prescription")}:{" "}
                  </span>
                  {loadedVisit.prescription.drug} ·{" "}
                  {loadedVisit.prescription.dose} ·{" "}
                  {loadedVisit.prescription.frequency} ·{" "}
                  {loadedVisit.prescription.duration}
                </p>
              ) : !readOnlyClinical ? (
                <form
                  onSubmit={onIssuePrescription}
                  className="flex flex-col gap-3"
                >
                  <h4 className="font-medium">{t("doctor.issuePrescription")}</h4>
                  <label className="cq-label">
                    <span>{t("doctor.drug")}</span>
                    <input
                      required
                      value={drug}
                      onChange={(e) => setDrug(e.target.value)}
                      className="cq-field"
                    />
                  </label>
                  <label className="cq-label">
                    <span>{t("doctor.dose")}</span>
                    <input
                      required
                      value={dose}
                      onChange={(e) => setDose(e.target.value)}
                      className="cq-field"
                    />
                  </label>
                  <label className="cq-label">
                    <span>{t("doctor.frequency")}</span>
                    <input
                      required
                      value={frequency}
                      onChange={(e) => setFrequency(e.target.value)}
                      className="cq-field"
                    />
                  </label>
                  <label className="cq-label">
                    <span>{t("doctor.duration")}</span>
                    <input
                      required
                      value={duration}
                      onChange={(e) => setDuration(e.target.value)}
                      className="cq-field"
                    />
                  </label>
                  <Button type="submit">{t("doctor.savePrescription")}</Button>
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
                  required
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
              <Button type="submit">{t("doctor.saveVisit")}</Button>
            </form>
          )}
        </div>
      ) : null}
    </section>
  );
}
