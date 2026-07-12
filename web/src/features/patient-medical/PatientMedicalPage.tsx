import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";

import { useActiveClinic } from "@/features/auth/AuthProvider";

import { getPatientProfile, listMyClinics, listMyVisits } from "./api";

function displayOrEmpty(
  value: string | null | undefined,
  emptyLabel: string,
): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : emptyLabel;
}

export function PatientMedicalPage() {
  const { t } = useTranslation();
  const { id: clinicId } = useActiveClinic();

  const clinicsQuery = useQuery({
    queryKey: ["patient-my-clinics"],
    queryFn: listMyClinics,
  });

  const membershipId =
    clinicsQuery.data?.find(
      (c) => c.clinicId.toLowerCase() === clinicId.toLowerCase(),
    )?.membershipId ?? null;

  const profileQuery = useQuery({
    queryKey: ["patient-profile", membershipId],
    queryFn: () => getPatientProfile(membershipId!),
    enabled: Boolean(membershipId),
  });

  const visitsQuery = useQuery({
    queryKey: ["patient-visits", clinicId],
    queryFn: () => listMyVisits(clinicId),
  });

  const profile = profileQuery.data;
  const visits = visitsQuery.data ?? [];
  const emptyValue = t("patientMedical.notRecorded");

  const clinicsError = clinicsQuery.isError
    ? (clinicsQuery.error as Error).message
    : null;
  const profileError = profileQuery.isError
    ? (profileQuery.error as Error).message
    : null;
  const visitsError = visitsQuery.isError
    ? (visitsQuery.error as Error).message
    : null;
  const loadError = clinicsError ?? profileError ?? visitsError;

  return (
    <div className="cq-page">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("patientMedical.title")}</h1>
        <p className="cq-page-subtitle">{t("patientMedical.subtitle")}</p>
      </header>

      {loadError ? (
        <p className="cq-alert" role="alert">
          {t("patientMedical.loadError")}
        </p>
      ) : null}

      {!membershipId && !clinicsQuery.isLoading ? (
        <p className="cq-banner-warning" role="status">
          {t("patientMedical.noMembership")}
        </p>
      ) : null}

      <section className="cq-section" aria-labelledby="medical-profile">
        <h2 id="medical-profile" className="cq-section-title">
          {t("patientMedical.profile")}
        </h2>
        {profileQuery.isLoading || clinicsQuery.isLoading ? (
          <p className="cq-status">{t("patientMedical.loading")}</p>
        ) : (
          <div className="flex flex-col gap-3">
            <div className="cq-allergy-panel" role="status">
              <p className="cq-allergy-panel__title">
                {t("patientMedical.allergies")}
              </p>
              <p className="mt-1 text-sm font-medium text-destructive">
                {displayOrEmpty(profile?.allergies, t("patientMedical.noAllergies"))}
              </p>
            </div>

            <dl className="cq-profile-grid">
              <div className="cq-profile-field">
                <dt>{t("patientMedical.bloodType")}</dt>
                <dd>{displayOrEmpty(profile?.bloodType, emptyValue)}</dd>
              </div>
              <div className="cq-profile-field">
                <dt>{t("patientMedical.chronic")}</dt>
                <dd>
                  {displayOrEmpty(profile?.chronicConditions, emptyValue)}
                </dd>
              </div>
              <div className="cq-profile-field sm:col-span-2">
                <dt>{t("patientMedical.medications")}</dt>
                <dd>
                  {displayOrEmpty(profile?.currentMedications, emptyValue)}
                </dd>
              </div>
            </dl>
            <p className="text-xs text-muted-foreground">
              {t("patientMedical.readOnlyNote")}
            </p>
          </div>
        )}
      </section>

      <section className="cq-section" aria-labelledby="medical-history">
        <h2 id="medical-history" className="cq-section-title">
          {t("patientMedical.history")}
        </h2>
        {visitsQuery.isLoading ? (
          <p className="cq-status">{t("patientMedical.loading")}</p>
        ) : visits.length === 0 ? (
          <div className="cq-empty-friendly">
            <p className="font-medium text-foreground">
              {t("patientMedical.emptyTitle")}
            </p>
            <p className="mt-1">{t("patientMedical.emptyHistory")}</p>
          </div>
        ) : (
          <ol className="cq-timeline">
            {visits.map((visit) => (
              <li key={visit.visitId} className="cq-timeline__item">
                <p className="text-sm font-semibold text-foreground">
                  {new Date(visit.date).toLocaleDateString()}
                </p>
                <p className="mt-1 text-sm">
                  <span className="text-muted-foreground">
                    {t("patientMedical.diagnosis")}:{" "}
                  </span>
                  {visit.diagnosis}
                </p>
                {visit.notes ? (
                  <p className="mt-1 text-sm text-muted-foreground">
                    <span className="font-medium text-foreground">
                      {t("patientMedical.notes")}:{" "}
                    </span>
                    {visit.notes}
                  </p>
                ) : null}
                {visit.prescription ? (
                  <article
                    className="cq-rx-card mt-3"
                    aria-label={t("patientMedical.prescriptionTitle")}
                  >
                    <h3 className="text-sm font-semibold">
                      {t("patientMedical.prescriptionTitle")}
                    </h3>
                    <dl className="cq-rx-card__grid">
                      <div className="cq-rx-card__field">
                        <dt>{t("patientMedical.drug")}</dt>
                        <dd>{visit.prescription.drug}</dd>
                      </div>
                      <div className="cq-rx-card__field">
                        <dt>{t("patientMedical.dose")}</dt>
                        <dd>{visit.prescription.dose}</dd>
                      </div>
                      <div className="cq-rx-card__field">
                        <dt>{t("patientMedical.frequency")}</dt>
                        <dd>{visit.prescription.frequency}</dd>
                      </div>
                      <div className="cq-rx-card__field">
                        <dt>{t("patientMedical.duration")}</dt>
                        <dd>{visit.prescription.duration}</dd>
                      </div>
                    </dl>
                  </article>
                ) : null}
              </li>
            ))}
          </ol>
        )}
      </section>
    </div>
  );
}
