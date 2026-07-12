import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type ChangeEvent, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useAuth, useActiveClinic } from "@/features/auth/AuthProvider";
import { clinicName as resolveClinicName } from "@/features/auth/roleHome";
import { todayInClinicTimezone } from "@/lib/datetime";

import {
  cancelAppointment,
  createAppointment,
  listDependents,
  listMyAppointments,
  registerDependent,
  type HistoryTab,
  type PatientAppointment,
} from "./api";

const DEFAULT_TIMEZONE = "Africa/Cairo";
const RELATIONSHIPS = ["Child", "Spouse", "Parent", "Sibling", "Other"] as const;

type Props = {
  clinicTimezone?: string;
};

function appointmentType(appt: PatientAppointment): "Normal" | "Urgent" {
  return appt.type === "Urgent" ? "Urgent" : "Normal";
}

function readFileAsDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      if (typeof reader.result === "string") resolve(reader.result);
      else reject(new Error("empty"));
    };
    reader.onerror = () => reject(reader.error ?? new Error("read failed"));
    reader.readAsDataURL(file);
  });
}

export function PatientBookingsPage({
  clinicTimezone = DEFAULT_TIMEZONE,
}: Props = {}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { memberships } = useAuth();
  const { id: clinicId, name: activeClinicName } = useActiveClinic();
  const [tab, setTab] = useState<HistoryTab>("upcoming");
  const [dependentName, setDependentName] = useState("");
  const [dependentDob, setDependentDob] = useState("");
  const [dependentRel, setDependentRel] = useState<string>("Child");
  const [selectedDependentId, setSelectedDependentId] = useState("");
  const [bookForSelf, setBookForSelf] = useState(true);
  const [method, setMethod] = useState<"WalkIn" | "PhoneDeposit">("WalkIn");
  const [companionsCount, setCompanionsCount] = useState(0);
  const [possibleUrgent, setPossibleUrgent] = useState(false);
  const [depositProofUrl, setDepositProofUrl] = useState<string | null>(null);
  const [depositFileName, setDepositFileName] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [cancelTargetId, setCancelTargetId] = useState<string | null>(null);
  const [bookSuccess, setBookSuccess] = useState<string | null>(null);

  const historyQuery = useQuery({
    queryKey: ["patient-bookings", tab],
    queryFn: () => listMyAppointments(tab),
  });

  const dependentsQuery = useQuery({
    queryKey: ["dependents", clinicId],
    queryFn: () => listDependents(clinicId),
    enabled: Boolean(clinicId),
  });

  const cancelMutation = useMutation({
    mutationFn: cancelAppointment,
    onSuccess: async () => {
      setCancelTargetId(null);
      await queryClient.invalidateQueries({ queryKey: ["patient-bookings"] });
    },
    onError: (error: Error) => setFormError(error.message),
  });

  const registerMutation = useMutation({
    mutationFn: registerDependent,
    onSuccess: async () => {
      setDependentName("");
      setDependentDob("");
      setFormError(null);
      await queryClient.invalidateQueries({ queryKey: ["dependents", clinicId] });
    },
    onError: (error: Error) => setFormError(error.message),
  });

  const bookMutation = useMutation({
    mutationFn: createAppointment,
    onSuccess: async (result) => {
      setFormError(null);
      setDepositProofUrl(null);
      setDepositFileName(null);
      setCompanionsCount(0);
      setPossibleUrgent(false);
      setMethod("WalkIn");
      setBookSuccess(
        t("patientBookings.bookSuccess", { number: result.temporaryNumber }),
      );
      await queryClient.invalidateQueries({ queryKey: ["patient-bookings"] });
    },
    onError: (error: Error) => setFormError(error.message),
  });

  const clinicLabel = (
    apptClinicId: string,
    apiName?: string | null,
    apiSlug?: string | null,
  ) => {
    const uuidRe =
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    const usable = (value: string | null | undefined) =>
      Boolean(value?.trim()) && !uuidRe.test(value!.trim());

    if (usable(apiName)) return apiName!.trim();
    if (usable(apiSlug)) return apiSlug!.trim();
    const fromMembership = resolveClinicName(memberships, apptClinicId);
    if (usable(fromMembership)) return fromMembership;
    if (
      apptClinicId.toLowerCase() === clinicId.toLowerCase() &&
      usable(activeClinicName)
    ) {
      return activeClinicName;
    }
    return t("patientBookings.unknownClinic");
  };

  const statusLabel = (status: string) => {
    const key = `patientBookings.apptStatus.${status}`;
    const translated = t(key);
    return translated === key ? status : translated;
  };

  const relationshipLabel = (rel: string) => {
    const key = `patientBookings.relationships.${rel}`;
    const translated = t(key);
    return translated === key ? rel : translated;
  };

  const formatDay = (appt: PatientAppointment) => {
    if (!appt.day) return null;
    const parsed = new Date(appt.day);
    if (Number.isNaN(parsed.getTime())) return appt.day;
    return parsed.toLocaleDateString();
  };

  const onRegisterDependent = (event: FormEvent) => {
    event.preventDefault();
    registerMutation.mutate({
      clinicId,
      name: dependentName,
      dateOfBirth: dependentDob,
      relationshipToGuardian: dependentRel,
    });
  };

  const onDepositFile = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) {
      setDepositProofUrl(null);
      setDepositFileName(null);
      return;
    }
    try {
      const dataUrl = await readFileAsDataUrl(file);
      setDepositProofUrl(dataUrl);
      setDepositFileName(file.name);
      setFormError(null);
    } catch {
      setDepositProofUrl(null);
      setDepositFileName(null);
      setFormError(t("patientBookings.depositProofFailed"));
    }
  };

  const onBook = (event: FormEvent) => {
    event.preventDefault();
    setBookSuccess(null);
    if (!bookForSelf && !selectedDependentId) {
      setFormError(t("patientBookings.selectDependent"));
      return;
    }
    if (method === "PhoneDeposit" && !depositProofUrl) {
      setFormError(t("patientBookings.depositProofRequired"));
      return;
    }
    bookMutation.mutate({
      clinicId,
      day: todayInClinicTimezone(clinicTimezone),
      method,
      companionsCount,
      possibleUrgentFlag: possibleUrgent,
      dependentId: bookForSelf ? undefined : selectedDependentId,
      depositProofUrl:
        method === "PhoneDeposit" ? depositProofUrl : undefined,
    });
  };

  const appointments = historyQuery.data ?? [];
  const dependents = dependentsQuery.data ?? [];

  return (
    <section className="cq-page">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("patientBookings.title")}</h1>
        <p className="cq-page-subtitle">{t("patientBookings.subtitle")}</p>
      </header>

      <aside className="cq-fr022-banner" role="note">
        <p>
          <strong>{t("patientBookings.fr022Title")}</strong>
        </p>
        <p className="mt-2">{t("patientBookings.fr022Body")}</p>
      </aside>

      {formError ? (
        <p className="cq-alert" role="alert">
          {formError}
        </p>
      ) : null}

      {bookSuccess ? (
        <p className="cq-status" role="status">
          {bookSuccess}
        </p>
      ) : null}

      <section className="cq-section" aria-labelledby="bookings-new">
        <h2 id="bookings-new" className="cq-section-title">
          {t("patientBookings.bookSection")}
        </h2>
        <p className="mb-3 text-sm text-muted-foreground">
          {t("patientBookings.bookClinic", {
            clinic: clinicLabel(clinicId, activeClinicName),
          })}
        </p>
        <p className="cq-callout-info mb-3" role="note">
          {t("patientBookings.fr022Short")}
        </p>
        <form onSubmit={onBook} className="flex flex-col gap-3">
          <fieldset className="flex flex-col gap-2">
            <legend className="text-sm font-medium">
              {t("patientBookings.bookForLegend")}
            </legend>
            <label className="cq-check">
              <input
                type="radio"
                name="book-who"
                checked={bookForSelf}
                onChange={() => setBookForSelf(true)}
              />
              <span>{t("patientBookings.bookForSelf")}</span>
            </label>
            <label className="cq-check">
              <input
                type="radio"
                name="book-who"
                checked={!bookForSelf}
                onChange={() => setBookForSelf(false)}
              />
              <span>{t("patientBookings.bookForDependent")}</span>
            </label>
          </fieldset>

          {!bookForSelf ? (
            <label className="cq-label">
              <span>{t("patientBookings.bookFor")}</span>
              <select
                value={selectedDependentId}
                onChange={(e) => setSelectedDependentId(e.target.value)}
                className="cq-field"
                required
              >
                <option value="">{t("patientBookings.selectDependent")}</option>
                {dependents.map((d) => (
                  <option key={d.dependentId} value={d.dependentId}>
                    {d.name} ({relationshipLabel(d.relationshipToGuardian)})
                  </option>
                ))}
              </select>
            </label>
          ) : null}

          <fieldset className="flex flex-col gap-2">
            <legend className="text-sm font-medium">
              {t("patientBookings.typeLegend")}
            </legend>
            <label className="cq-check">
              <input
                type="checkbox"
                checked={possibleUrgent}
                onChange={(e) => setPossibleUrgent(e.target.checked)}
              />
              <span>{t("patientBookings.typeUrgent")}</span>
            </label>
            <p className="text-xs text-muted-foreground">
              {t("patientBookings.typeUrgentHint")}
            </p>
          </fieldset>

          <fieldset className="flex flex-col gap-2">
            <legend className="text-sm font-medium">
              {t("patientBookings.methodLegend")}
            </legend>
            <label className="cq-check">
              <input
                type="radio"
                name="book-method"
                checked={method === "WalkIn"}
                onChange={() => setMethod("WalkIn")}
              />
              <span>{t("patientBookings.methodWalkIn")}</span>
            </label>
            <label className="cq-check">
              <input
                type="radio"
                name="book-method"
                checked={method === "PhoneDeposit"}
                onChange={() => setMethod("PhoneDeposit")}
              />
              <span>{t("patientBookings.methodDeposit")}</span>
            </label>
          </fieldset>

          {method === "PhoneDeposit" ? (
            <label className="cq-label">
              <span>{t("patientBookings.depositProof")}</span>
              <input
                type="file"
                accept="image/*"
                capture="environment"
                onChange={(e) => void onDepositFile(e)}
                className="cq-field"
                required
              />
              <span className="text-xs text-muted-foreground">
                {t("patientBookings.depositProofHint")}
              </span>
              {depositFileName ? (
                <span className="text-xs text-success">
                  {t("patientBookings.depositProofSelected", {
                    name: depositFileName,
                  })}
                </span>
              ) : null}
            </label>
          ) : null}

          <label className="cq-label">
            <span>{t("patientBookings.companions")}</span>
            <input
              type="number"
              min={0}
              max={20}
              value={companionsCount}
              onChange={(e) =>
                setCompanionsCount(Number.parseInt(e.target.value, 10) || 0)
              }
              className="cq-field"
            />
            <span className="text-xs text-muted-foreground">
              {t("patientBookings.companionsHint")}
            </span>
          </label>

          <Button type="submit" disabled={bookMutation.isPending}>
            {bookMutation.isPending
              ? t("patientBookings.booking")
              : t("patientBookings.book")}
          </Button>
        </form>
      </section>

      <section className="cq-section" aria-labelledby="bookings-history">
        <h2 id="bookings-history" className="cq-section-title">
          {t("patientBookings.historyTitle")}
        </h2>
        <div
          className="cq-nav mb-4"
          role="tablist"
          aria-label={t("patientBookings.historyTitle")}
        >
          {(["upcoming", "completed", "cancelled"] as HistoryTab[]).map(
            (key) => (
              <Button
                key={key}
                type="button"
                size="sm"
                role="tab"
                aria-selected={tab === key}
                variant={tab === key ? "default" : "ghost"}
                onClick={() => setTab(key)}
              >
                {t(`patientBookings.tabs.${key}`)}
              </Button>
            ),
          )}
        </div>

        {historyQuery.isLoading ? (
          <p className="cq-status">{t("patientBookings.loading")}</p>
        ) : historyQuery.isError ? (
          <div className="cq-alert" role="alert">
            <p className="font-medium">{t("patientBookings.errorTitle")}</p>
            <p className="mt-1 text-sm">{t("patientBookings.error")}</p>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="mt-2"
              onClick={() => void historyQuery.refetch()}
            >
              {t("patientBookings.retry")}
            </Button>
          </div>
        ) : appointments.length === 0 ? (
          <div className="cq-empty-friendly">
            <p className="font-medium text-foreground">
              {t("patientBookings.emptyHistoryTitle")}
            </p>
            <p className="mt-1">{t("patientBookings.emptyHistory")}</p>
          </div>
        ) : (
          <ul className="flex flex-col gap-3">
            {appointments.map((appt) => {
              const type = appointmentType(appt);
              const dayLabel = formatDay(appt);
              return (
                <li key={appt.appointmentId} className="cq-booking-card">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-medium">
                      {t("patientBookings.tempNumber", {
                        number: appt.temporaryNumber,
                      })}
                    </span>
                    <span className="cq-type-badge" data-type={type}>
                      {type === "Urgent"
                        ? t("patientBookings.typeUrgent")
                        : t("patientBookings.typeNormal")}
                    </span>
                    <span className="text-sm text-muted-foreground">
                      {statusLabel(appt.status)}
                    </span>
                  </div>
                  <div className="flex flex-col gap-0.5 text-sm text-muted-foreground">
                    {dayLabel ? (
                      <span>
                        {t("patientBookings.dateLabel", { date: dayLabel })}
                      </span>
                    ) : null}
                    <span>
                      {t("patientBookings.clinicLabel", {
                        clinic: clinicLabel(
                          appt.clinicId,
                          appt.clinicName,
                          appt.clinicSlug,
                        ),
                      })}
                    </span>
                    {appt.dependentId ? (
                      <span>{t("patientBookings.forDependent")}</span>
                    ) : null}
                  </div>
                  {tab === "upcoming" && appt.status === "Booked" ? (
                    <div>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        disabled={cancelMutation.isPending}
                        onClick={() => setCancelTargetId(appt.appointmentId)}
                      >
                        {t("patientBookings.cancel")}
                      </Button>
                    </div>
                  ) : null}
                </li>
              );
            })}
          </ul>
        )}
      </section>

      <section className="cq-section" aria-labelledby="bookings-dependents">
        <h2 id="bookings-dependents" className="cq-section-title">
          {t("patientBookings.dependentsTitle")}
        </h2>
        <p className="mb-3 text-sm text-muted-foreground">
          {t("patientBookings.dependentsHint")}
        </p>

        <form onSubmit={onRegisterDependent} className="flex flex-col gap-3">
          <label className="cq-label">
            <span>{t("patientBookings.dependentName")}</span>
            <input
              required
              value={dependentName}
              onChange={(e) => setDependentName(e.target.value)}
              className="cq-field"
            />
          </label>
          <label className="cq-label">
            <span>{t("patientBookings.dependentDob")}</span>
            <input
              required
              type="date"
              value={dependentDob}
              onChange={(e) => setDependentDob(e.target.value)}
              className="cq-field"
            />
          </label>
          <label className="cq-label">
            <span>{t("patientBookings.relationship")}</span>
            <select
              required
              value={dependentRel}
              onChange={(e) => setDependentRel(e.target.value)}
              className="cq-field"
            >
              {RELATIONSHIPS.map((rel) => (
                <option key={rel} value={rel}>
                  {relationshipLabel(rel)}
                </option>
              ))}
            </select>
          </label>
          <Button type="submit" disabled={registerMutation.isPending}>
            {registerMutation.isPending
              ? t("patientBookings.registering")
              : t("patientBookings.registerDependent")}
          </Button>
        </form>

        {dependentsQuery.isLoading ? (
          <p className="cq-status mt-3">{t("patientBookings.loading")}</p>
        ) : dependents.length === 0 ? (
          <p className="cq-empty-friendly mt-3">
            {t("patientBookings.noDependents")}
          </p>
        ) : (
          <ul className="cq-list-plain mt-3">
            {dependents.map((d) => (
              <li key={d.dependentId} className="py-2 text-sm">
                <span className="font-medium">{d.name}</span>
                <span className="text-muted-foreground">
                  {" · "}
                  {relationshipLabel(d.relationshipToGuardian)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      {cancelTargetId ? (
        <div
          className="cq-confirm-backdrop"
          role="dialog"
          aria-modal="true"
          aria-labelledby="cancel-booking-title"
        >
          <div className="cq-confirm-panel">
            <h3 id="cancel-booking-title" className="cq-section-title">
              {t("patientBookings.cancelConfirmTitle")}
            </h3>
            <p className="text-sm text-muted-foreground">
              {t("patientBookings.cancelConfirmBody")}
            </p>
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                variant="destructive"
                disabled={cancelMutation.isPending}
                onClick={() => cancelMutation.mutate(cancelTargetId)}
              >
                {t("patientBookings.cancelConfirmYes")}
              </Button>
              <Button
                type="button"
                variant="outline"
                disabled={cancelMutation.isPending}
                onClick={() => setCancelTargetId(null)}
              >
                {t("patientBookings.cancelConfirmNo")}
              </Button>
            </div>
          </div>
        </div>
      ) : null}
    </section>
  );
}
