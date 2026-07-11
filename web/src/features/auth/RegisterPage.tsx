import { useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";

import { useAuth } from "./AuthProvider";

export function RegisterPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { register, login } = useAuth();

  const preselectedClinicId = searchParams.get("clinicId") ?? undefined;

  const [emailOrPhone, setEmailOrPhone] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setPending(true);
    try {
      await register(emailOrPhone, password, preselectedClinicId);
      const result = await login(emailOrPhone, password, preselectedClinicId);
      if (result.kind === "choose-clinic") {
        navigate("/login", { replace: true });
        return;
      }
      if (result.kind === "find-clinic") {
        navigate("/find-clinic", { replace: true });
        return;
      }
      navigate(result.homePath, { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : t("auth.registerFailed"));
    } finally {
      setPending(false);
    }
  };

  return (
    <section className="cq-page mx-auto max-w-md">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("auth.registerTitle")}</h1>
        <p className="cq-page-subtitle">{t("auth.registerSubtitle")}</p>
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
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="cq-field"
            minLength={8}
            required
          />
        </label>

        {error ? (
          <p className="cq-alert" role="alert">
            {error}
          </p>
        ) : null}

        <Button type="submit" disabled={pending}>
          {pending ? t("auth.registering") : t("auth.register")}
        </Button>
      </form>

      <p className="mt-4 text-sm text-muted-foreground">
        {t("auth.hasAccount")}{" "}
        <Link
          to={
            preselectedClinicId
              ? `/login?clinicId=${encodeURIComponent(preselectedClinicId)}`
              : "/login"
          }
          className="text-primary underline-offset-2 hover:underline"
        >
          {t("auth.loginLink")}
        </Link>
      </p>
    </section>
  );
}
