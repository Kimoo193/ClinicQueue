import { useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";

import { useAuth } from "./AuthProvider";
import { distinctClinicIds } from "./roleHome";

export function LoginPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { login, memberships, activeClinic, user } = useAuth();

  const preselectedClinicId = searchParams.get("clinicId") ?? undefined;
  const returnTo = searchParams.get("returnTo") ?? undefined;

  const [emailOrPhone, setEmailOrPhone] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setPending(true);
    try {
      const result = await login(emailOrPhone, password, preselectedClinicId);
      if (result.kind === "choose-clinic") {
        const q = returnTo
          ? `?returnTo=${encodeURIComponent(returnTo)}`
          : "";
        navigate(`/choose-clinic${q}`, { replace: true });
        return;
      }
      if (result.kind === "find-clinic") {
        navigate("/find-clinic", { replace: true });
        return;
      }
      navigate(returnTo ?? result.homePath, { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : t("auth.loginFailed"));
    } finally {
      setPending(false);
    }
  };

  // Logged in without an active clinic and multiple clinics → dedicated picker.
  if (user && !activeClinic && distinctClinicIds(memberships).length > 1) {
    const q = returnTo ? `?returnTo=${encodeURIComponent(returnTo)}` : "";
    return <Navigate to={`/choose-clinic${q}`} replace />;
  }

  return (
    <section className="cq-page mx-auto max-w-md">
      <header className="cq-page-header">
        <p className="cq-brand-mark">{t("app.name")}</p>
        <h1 className="cq-page-title">{t("auth.loginTitle")}</h1>
        <p className="cq-page-subtitle">{t("auth.loginSubtitle")}</p>
      </header>

      <form onSubmit={(e) => void onSubmit(e)} className="cq-section gap-4">
        <label className="cq-label">
          <span>{t("auth.emailOrPhone")}</span>
          <input
            type="text"
            autoComplete="username"
            value={emailOrPhone}
            onChange={(e) => setEmailOrPhone(e.target.value)}
            className="cq-field"
            required
          />
        </label>
        <label className="cq-label">
          <span>{t("auth.password")}</span>
          <input
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="cq-field"
            required
          />
        </label>

        {error ? (
          <p className="cq-alert" role="alert">
            {error}
          </p>
        ) : null}

        <Button type="submit" disabled={pending} className="w-full">
          {pending ? t("auth.loggingIn") : t("auth.login")}
        </Button>
      </form>

      <p className="mt-4 text-sm text-muted-foreground">
        {t("auth.noAccount")}{" "}
        <Link
          to={
            preselectedClinicId
              ? `/register?clinicId=${encodeURIComponent(preselectedClinicId)}`
              : "/register"
          }
          className="text-primary underline-offset-2 hover:underline"
        >
          {t("auth.registerLink")}
        </Link>
      </p>
    </section>
  );
}
