import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "react-router";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { getClinicBySlug, joinClinicAsPatient } from "@/features/auth/api";
import { useAuth } from "@/features/auth/AuthProvider";
import { homePathForRoles } from "@/features/auth/roleHome";

export function ClinicEntryPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { clinicSlug } = useParams<{ clinicSlug: string }>();
  const { user, switchClinic, isLoading: authLoading } = useAuth();
  const [joinError, setJoinError] = useState<string | null>(null);
  const [joining, setJoining] = useState(false);

  const profileQuery = useQuery({
    queryKey: ["clinic-by-slug", clinicSlug],
    queryFn: () => getClinicBySlug(clinicSlug ?? ""),
    enabled: Boolean(clinicSlug),
  });

  if (profileQuery.isLoading || authLoading) {
    return (
      <p className="cq-banner-warning" role="status">
        {t("auth.loadingClinic")}
      </p>
    );
  }

  if (!profileQuery.data) {
    return (
      <section className="cq-page mx-auto max-w-md">
        <header className="cq-page-header">
          <h1 className="cq-page-title">{t("auth.clinicNotFoundTitle")}</h1>
          <p className="cq-page-subtitle">{t("auth.clinicNotFoundSubtitle")}</p>
        </header>
        <div className="flex flex-wrap gap-3">
          <Button asChild variant="outline">
            <Link to="/find-clinic">{t("auth.findClinicTitle")}</Link>
          </Button>
          <Button asChild variant="ghost">
            <Link to="/">{t("auth.backHome")}</Link>
          </Button>
        </div>
      </section>
    );
  }

  const clinic = profileQuery.data;
  const brandStyle =
    clinic.primaryColor || clinic.secondaryColor
      ? {
          ...(clinic.primaryColor
            ? { ["--clinic-primary" as string]: clinic.primaryColor }
            : {}),
          ...(clinic.secondaryColor
            ? { ["--clinic-secondary" as string]: clinic.secondaryColor }
            : {}),
        }
      : undefined;

  const loginTo = `/login?clinicId=${encodeURIComponent(clinic.clinicId)}`;
  const registerTo = `/register?clinicId=${encodeURIComponent(clinic.clinicId)}`;

  const onJoin = async () => {
    setJoinError(null);
    setJoining(true);
    try {
      await joinClinicAsPatient(clinic.clinicId);
      const homePath = await switchClinic(clinic.clinicId);
      navigate(homePath || homePathForRoles(["Patient"]), { replace: true });
    } catch (err) {
      setJoinError(
        err instanceof Error ? err.message : t("auth.joinClinicFailed"),
      );
    } finally {
      setJoining(false);
    }
  };

  return (
    <section className="cq-page mx-auto max-w-md" style={brandStyle}>
      <header className="cq-page-header">
        {clinic.logoUrl ? (
          <img
            src={clinic.logoUrl}
            alt=""
            className="mb-3 h-14 w-14 rounded-lg object-cover"
          />
        ) : null}
        <h1 className="cq-page-title">{clinic.name}</h1>
        <p className="cq-page-subtitle">
          {t("auth.clinicEntrySubtitle", { specialty: clinic.specialty })}
        </p>
      </header>

      {joinError ? (
        <p className="cq-alert mb-4" role="alert">
          {joinError}
        </p>
      ) : null}

      {user ? (
        <div className="flex flex-wrap gap-3">
          <Button type="button" disabled={joining} onClick={() => void onJoin()}>
            {joining ? t("auth.joiningClinic") : t("auth.joinClinic")}
          </Button>
          <Button asChild variant="outline">
            <Link to="/find-clinic">{t("auth.findClinicTitle")}</Link>
          </Button>
        </div>
      ) : (
        <div className="flex flex-wrap gap-3">
          <Button asChild>
            <Link to={registerTo}>{t("auth.register")}</Link>
          </Button>
          <Button asChild variant="outline">
            <Link to={loginTo}>{t("auth.login")}</Link>
          </Button>
        </div>
      )}
    </section>
  );
}
