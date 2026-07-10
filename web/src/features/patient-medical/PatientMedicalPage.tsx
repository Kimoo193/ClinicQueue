import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";

import {
  getPatientProfile,
  listMyClinics,
  listMyVisits,
  updatePatientProfile,
} from "./api";

type Props = {
  clinicId: string;
};

export function PatientMedicalPage({ clinicId }: Props) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [actionError, setActionError] = useState<string | null>(null);
  const [bloodType, setBloodType] = useState("");
  const [chronicConditions, setChronicConditions] = useState("");
  const [allergies, setAllergies] = useState("");
  const [currentMedications, setCurrentMedications] = useState("");

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

  useEffect(() => {
    if (!profileQuery.data) return;
    setBloodType(profileQuery.data.bloodType ?? "");
    setChronicConditions(profileQuery.data.chronicConditions ?? "");
    setAllergies(profileQuery.data.allergies ?? "");
    setCurrentMedications(profileQuery.data.currentMedications ?? "");
  }, [profileQuery.data]);

  const saveMutation = useMutation({
    mutationFn: () =>
      updatePatientProfile(membershipId!, {
        bloodType: bloodType || undefined,
        chronicConditions: chronicConditions || undefined,
        allergies: allergies || undefined,
        currentMedications: currentMedications || undefined,
      }),
    onSuccess: async () => {
      setActionError(null);
      await queryClient.invalidateQueries({
        queryKey: ["patient-profile", membershipId],
      });
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const onSave = (event: FormEvent) => {
    event.preventDefault();
    if (!membershipId) return;
    saveMutation.mutate();
  };

  return (
    <div className="cq-page">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("patientMedical.title")}</h1>
        <p className="cq-page-subtitle">{t("patientMedical.subtitle")}</p>
      </header>

      {actionError ? (
        <p className="cq-alert" role="alert">
          {actionError}
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
        {profileQuery.isLoading ? (
          <p className="cq-status">{t("patientMedical.loading")}</p>
        ) : (
          <form className="space-y-3" onSubmit={onSave}>
            <label className="cq-label">
              <span>{t("patientMedical.bloodType")}</span>
              <input
                value={bloodType}
                onChange={(e) => setBloodType(e.target.value)}
                className="cq-field"
                disabled={!membershipId}
              />
            </label>
            <label className="cq-label">
              <span>{t("patientMedical.chronic")}</span>
              <textarea
                value={chronicConditions}
                onChange={(e) => setChronicConditions(e.target.value)}
                className="cq-field"
                rows={2}
                disabled={!membershipId}
              />
            </label>
            <label className="cq-label">
              <span>{t("patientMedical.allergies")}</span>
              <textarea
                value={allergies}
                onChange={(e) => setAllergies(e.target.value)}
                className="cq-field"
                rows={2}
                disabled={!membershipId}
              />
            </label>
            <label className="cq-label">
              <span>{t("patientMedical.medications")}</span>
              <textarea
                value={currentMedications}
                onChange={(e) => setCurrentMedications(e.target.value)}
                className="cq-field"
                rows={2}
                disabled={!membershipId}
              />
            </label>
            <Button
              type="submit"
              disabled={!membershipId || saveMutation.isPending}
            >
              {saveMutation.isPending
                ? t("patientMedical.saving")
                : t("patientMedical.save")}
            </Button>
          </form>
        )}
      </section>

      <section className="cq-section" aria-labelledby="medical-history">
        <h2 id="medical-history" className="cq-section-title">
          {t("patientMedical.history")}
        </h2>
        {visitsQuery.isLoading ? (
          <p className="cq-status">{t("patientMedical.loading")}</p>
        ) : !visitsQuery.data?.length ? (
          <p className="cq-status">{t("patientMedical.emptyHistory")}</p>
        ) : (
          <ul className="cq-list">
            {visitsQuery.data.map((visit) => (
              <li key={visit.visitId} className="space-y-1">
                <p>
                  <strong>{new Date(visit.date).toLocaleDateString()}</strong> —{" "}
                  {visit.diagnosis}
                </p>
                {visit.notes ? <p className="text-sm">{visit.notes}</p> : null}
                {visit.prescription ? (
                  <p className="text-sm text-muted-foreground">
                    {t("patientMedical.prescription", {
                      drug: visit.prescription.drug,
                      dose: visit.prescription.dose,
                      frequency: visit.prescription.frequency,
                      duration: visit.prescription.duration,
                    })}
                  </p>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
