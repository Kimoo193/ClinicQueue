import { useEffect, useMemo, useState } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router";
import { useTranslation } from "react-i18next";

import { useAuth } from "@/features/auth/AuthProvider";
import { homePathForRoles, rolesInClinic } from "@/features/auth/roleHome";

/**
 * Dedicated multi-clinic picker (post-login and header deep-link).
 * Never shows raw clinic UUIDs — names + roles only.
 */
export function ChooseClinicPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { user, memberships, activeClinic, isLoading, switchClinic } =
    useAuth();
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const returnTo = searchParams.get("returnTo") ?? undefined;

  const uniqueClinics = useMemo(
    () =>
      Array.from(
        new Map(memberships.map((m) => [m.clinicId.toLowerCase(), m])).values(),
      ),
    [memberships],
  );

  useEffect(() => {
    if (isLoading || !user) return;
    if (uniqueClinics.length === 0) {
      navigate("/find-clinic", { replace: true });
      return;
    }
    if (uniqueClinics.length === 1 && !activeClinic) {
      const only = uniqueClinics[0]!;
      setPending(true);
      void switchClinic(only.clinicId)
        .then((homePath) => {
          navigate(returnTo ?? homePath, { replace: true });
        })
        .catch((err: unknown) => {
          setError(
            err instanceof Error ? err.message : t("auth.switchFailed"),
          );
          setPending(false);
        });
    }
  }, [
    activeClinic,
    isLoading,
    navigate,
    returnTo,
    switchClinic,
    t,
    uniqueClinics,
    user,
  ]);

  if (isLoading) {
    return (
      <section className="cq-page mx-auto max-w-md">
        <p className="cq-status">{t("auth.loading")}</p>
      </section>
    );
  }

  if (!user) {
    return <Navigate to="/login" replace />;
  }

  if (uniqueClinics.length === 0) {
    return null;
  }

  const onPickClinic = async (clinicId: string) => {
    setError(null);
    setPending(true);
    try {
      const homePath = await switchClinic(clinicId);
      const roles = rolesInClinic(memberships, clinicId);
      navigate(returnTo ?? homePathForRoles(roles) ?? homePath, {
        replace: true,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : t("auth.switchFailed"));
      setPending(false);
    }
  };

  return (
    <section className="cq-page mx-auto max-w-md">
      <header className="cq-page-header">
        <p className="cq-brand-mark">{t("app.name")}</p>
        <h1 className="cq-page-title">{t("auth.chooseClinicTitle")}</h1>
        <p className="cq-page-subtitle">{t("auth.chooseClinicSubtitle")}</p>
      </header>
      {error ? (
        <p className="cq-alert" role="alert">
          {error}
        </p>
      ) : null}
      <ul className="flex flex-col gap-2">
        {uniqueClinics.map((clinic) => (
          <li key={clinic.clinicId}>
            <button
              type="button"
              className="cq-clinic-pick"
              disabled={pending}
              onClick={() => void onPickClinic(clinic.clinicId)}
            >
              <span className="font-semibold text-foreground">
                {clinic.clinicName || t("auth.unnamedClinic")}
              </span>
              <span className="text-xs text-muted-foreground">
                {t(`auth.role.${clinic.role}`, {
                  defaultValue: clinic.role,
                })}
                {activeClinic?.id.toLowerCase() ===
                clinic.clinicId.toLowerCase()
                  ? ` · ${t("auth.currentClinic")}`
                  : ""}
              </span>
            </button>
          </li>
        ))}
      </ul>
      <p className="mt-4 text-sm text-muted-foreground">
        <Link to="/find-clinic" className="underline">
          {t("auth.findClinicTitle")}
        </Link>
      </p>
    </section>
  );
}
