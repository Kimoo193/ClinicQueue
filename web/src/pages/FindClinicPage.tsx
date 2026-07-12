import { useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate } from "react-router";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useAuth } from "@/features/auth/AuthProvider";

/**
 * Post bare-register (or login with no memberships) — identity exists but no clinic.
 * Path forward: enter clinic slug → /r/:slug, or scan QR guidance.
 */
export function FindClinicPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { user, isLoading, logout } = useAuth();
  const [slug, setSlug] = useState("");
  const [error, setError] = useState<string | null>(null);

  if (isLoading) {
    return (
      <p className="cq-banner-warning" role="status">
        {t("auth.loading")}
      </p>
    );
  }

  if (!user) {
    return <Navigate to="/login" replace />;
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    const normalized = slug.trim().toLowerCase();
    if (!normalized) {
      setError(t("auth.findClinicSlugRequired"));
      return;
    }
    navigate(`/r/${encodeURIComponent(normalized)}`);
  };

  return (
    <section className="cq-page mx-auto max-w-md">
      <header className="cq-page-header">
        <p className="cq-brand-mark">{t("app.name")}</p>
        <h1 className="cq-page-title">{t("auth.findClinicTitle")}</h1>
        <p className="cq-page-subtitle">{t("auth.findClinicSubtitle")}</p>
      </header>

      <div className="cq-callout-info mb-6" role="note">
        {t("auth.findClinicHelp")}
      </div>

      <form onSubmit={onSubmit} className="cq-section gap-4">
        <label className="cq-label">
          <span>{t("auth.findClinicSlugLabel")}</span>
          <input
            type="text"
            value={slug}
            onChange={(e) => setSlug(e.target.value)}
            className="cq-field"
            autoComplete="off"
            placeholder={t("auth.findClinicSlugPlaceholder")}
            required
          />
        </label>

        {error ? (
          <p className="cq-alert" role="alert">
            {error}
          </p>
        ) : null}

        <Button type="submit" className="w-full">
          {t("auth.findClinicContinue")}
        </Button>
      </form>

      <div className="cq-section mt-6 gap-2">
        <h2 className="cq-section-title">{t("auth.findClinicQrTitle")}</h2>
        <p className="text-sm text-muted-foreground">
          {t("auth.findClinicQrGuidance")}
        </p>
        <ol className="mt-2 list-inside list-decimal space-y-1 text-sm text-muted-foreground">
          <li>{t("auth.findClinicStep1")}</li>
          <li>{t("auth.findClinicStep2")}</li>
          <li>{t("auth.findClinicStep3")}</li>
        </ol>
      </div>

      <div className="mt-8 flex flex-wrap items-center gap-3">
        <Button type="button" variant="outline" onClick={() => void logout()}>
          {t("auth.logout")}
        </Button>
        <Link
          to="/"
          className="text-sm text-primary underline-offset-2 hover:underline"
        >
          {t("auth.backHome")}
        </Link>
      </div>
    </section>
  );
}
