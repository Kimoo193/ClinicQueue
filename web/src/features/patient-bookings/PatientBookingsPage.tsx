import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useActiveClinic } from "@/features/auth/AuthProvider";
import { todayInClinicTimezone } from "@/lib/datetime";

import {
  cancelAppointment,
  createAppointment,
  listDependents,
  listMyAppointments,
  registerDependent,
  searchClinics,
  type HistoryTab,
} from "./api";

const DEFAULT_TIMEZONE = "Africa/Cairo";

type Props = {
  clinicTimezone?: string;
};

export function PatientBookingsPage({
  clinicTimezone = DEFAULT_TIMEZONE,
}: Props = {}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { id: clinicId } = useActiveClinic();
  const [tab, setTab] = useState<HistoryTab>("upcoming");
  const [specialty, setSpecialty] = useState("");
  const [name, setName] = useState("");
  const [location, setLocation] = useState("");
  const [searchSubmitted, setSearchSubmitted] = useState(false);
  const [dependentName, setDependentName] = useState("");
  const [dependentDob, setDependentDob] = useState("");
  const [dependentRel, setDependentRel] = useState("Child");
  const [selectedDependentId, setSelectedDependentId] = useState("");
  const [formError, setFormError] = useState<string | null>(null);

  const historyQuery = useQuery({
    queryKey: ["patient-bookings", tab],
    queryFn: () => listMyAppointments(tab),
  });

  const dependentsQuery = useQuery({
    queryKey: ["dependents", clinicId],
    queryFn: () => listDependents(clinicId),
    enabled: Boolean(clinicId),
  });

  const searchQuery = useQuery({
    queryKey: ["clinic-search", specialty, name, location, searchSubmitted],
    queryFn: () =>
      searchClinics({
        specialty: specialty || undefined,
        name: name || undefined,
        location: location || undefined,
      }),
    enabled: searchSubmitted,
  });

  const cancelMutation = useMutation({
    mutationFn: cancelAppointment,
    onSuccess: async () => {
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

  const bookDependentMutation = useMutation({
    mutationFn: createAppointment,
    onSuccess: async () => {
      setFormError(null);
      await queryClient.invalidateQueries({ queryKey: ["patient-bookings"] });
    },
    onError: (error: Error) => setFormError(error.message),
  });

  const onSearch = (event: FormEvent) => {
    event.preventDefault();
    setSearchSubmitted(true);
    void queryClient.invalidateQueries({ queryKey: ["clinic-search"] });
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

  const onBookDependent = (event: FormEvent) => {
    event.preventDefault();
    if (!selectedDependentId) {
      setFormError(t("patientBookings.selectDependent"));
      return;
    }
    bookDependentMutation.mutate({
      clinicId,
      day: todayInClinicTimezone(clinicTimezone),
      method: "WalkIn",
      companionsCount: 0,
      dependentId: selectedDependentId,
    });
  };

  const appointments = historyQuery.data ?? [];
  const dependents = dependentsQuery.data ?? [];
  const clinics = searchQuery.data ?? [];

  return (
    <section className="cq-page">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("patientBookings.title")}</h1>
        <p className="cq-page-subtitle">{t("patientBookings.subtitle")}</p>
      </header>

      {formError ? (
        <p className="cq-alert" role="alert">
          {formError}
        </p>
      ) : null}

      <section className="cq-section">
        <h2 className="cq-section-title">{t("patientBookings.searchTitle")}</h2>
        <form onSubmit={onSearch} className="flex flex-col gap-3">
          <label className="cq-label">
            <span>{t("patientBookings.specialty")}</span>
            <input
              value={specialty}
              onChange={(e) => setSpecialty(e.target.value)}
              className="cq-field"
            />
          </label>
          <label className="cq-label">
            <span>{t("patientBookings.clinicName")}</span>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="cq-field"
            />
          </label>
          <label className="cq-label">
            <span>{t("patientBookings.location")}</span>
            <input
              value={location}
              onChange={(e) => setLocation(e.target.value)}
              className="cq-field"
            />
          </label>
          <Button type="submit">{t("patientBookings.search")}</Button>
        </form>

        {searchSubmitted ? (
          searchQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">{t("patientBookings.loading")}</p>
          ) : clinics.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t("patientBookings.noClinics")}</p>
          ) : (
            <ul className="cq-list-plain">
              {clinics.map((clinic) => (
                <li key={clinic.clinicId} className="flex flex-col gap-0.5 py-3">
                  <span className="font-medium">{clinic.name}</span>
                  <span className="text-sm text-muted-foreground">
                    {clinic.specialty}
                    {clinic.location ? ` · ${clinic.location}` : ""}
                    {clinic.aggregateRating != null
                      ? ` · ★ ${clinic.aggregateRating.toFixed(1)}`
                      : ""}
                  </span>
                </li>
              ))}
            </ul>
          )
        ) : null}
      </section>

      <section className="cq-section">
        <h2 className="cq-section-title">{t("patientBookings.historyTitle")}</h2>
        <div className="flex flex-wrap gap-2">
          {(["upcoming", "completed", "cancelled"] as HistoryTab[]).map((key) => (
            <Button
              key={key}
              type="button"
              size="sm"
              variant={tab === key ? "default" : "outline"}
              onClick={() => setTab(key)}
            >
              {t(`patientBookings.tabs.${key}`)}
            </Button>
          ))}
        </div>

        {historyQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">{t("patientBookings.loading")}</p>
        ) : appointments.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            {t("patientBookings.emptyHistory")}
          </p>
        ) : (
          <ul className="cq-list-plain">
            {appointments.map((appt) => (
              <li
                key={appt.appointmentId}
                className="flex flex-wrap items-center justify-between gap-3 py-3"
              >
                <div className="flex flex-col gap-0.5 text-sm">
                  <span className="font-medium">
                    {t("patientBookings.tempNumber", {
                      number: appt.temporaryNumber,
                    })}
                  </span>
                  <span className="font-mono text-xs text-muted-foreground">
                    {appt.clinicId}
                  </span>
                  <span className="text-muted-foreground">
                    {appt.status}
                    {appt.dependentId
                      ? ` · ${t("patientBookings.forDependent")}`
                      : ""}
                  </span>
                </div>
                {tab === "upcoming" && appt.status === "Booked" ? (
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={cancelMutation.isPending}
                    onClick={() => cancelMutation.mutate(appt.appointmentId)}
                  >
                    {t("patientBookings.cancel")}
                  </Button>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="cq-section">
        <h2 className="cq-section-title">{t("patientBookings.dependentsTitle")}</h2>

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
            <input
              required
              value={dependentRel}
              onChange={(e) => setDependentRel(e.target.value)}
              className="cq-field"
            />
          </label>
          <Button type="submit" disabled={registerMutation.isPending}>
            {registerMutation.isPending
              ? t("patientBookings.registering")
              : t("patientBookings.registerDependent")}
          </Button>
        </form>

        <form onSubmit={onBookDependent} className="flex flex-col gap-3">
          <label className="cq-label">
            <span>{t("patientBookings.bookFor")}</span>
            <select
              value={selectedDependentId}
              onChange={(e) => setSelectedDependentId(e.target.value)}
              className="cq-field"
            >
              <option value="">{t("patientBookings.selectDependent")}</option>
              {dependents.map((d) => (
                <option key={d.dependentId} value={d.dependentId}>
                  {d.name} ({d.relationshipToGuardian})
                </option>
              ))}
            </select>
          </label>
          <Button type="submit" disabled={bookDependentMutation.isPending}>
            {bookDependentMutation.isPending
              ? t("patientBookings.booking")
              : t("patientBookings.bookDependent")}
          </Button>
        </form>
      </section>
    </section>
  );
}
