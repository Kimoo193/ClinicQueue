import { Link, Navigate } from "react-router";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useAuth } from "@/features/auth/AuthProvider";
import { homePathForRoles } from "@/features/auth/roleHome";

export function LandingPage() {
  const { t } = useTranslation();
  const { user, activeClinic, isLoading } = useAuth();

  if (isLoading) {
    return (
      <p className="cq-banner-warning" role="status">
        {t("auth.loading")}
      </p>
    );
  }

  if (user && activeClinic) {
    return <Navigate to={homePathForRoles(activeClinic.roles)} replace />;
  }

  if (user?.roles.includes("SuperAdmin") && !activeClinic) {
    return <Navigate to="/admin" replace />;
  }

  if (user && !activeClinic) {
    return <Navigate to="/find-clinic" replace />;
  }

  return (
    <section className="cq-page mx-auto max-w-lg">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("landing.title")}</h1>
        <p className="cq-page-subtitle">{t("landing.subtitle")}</p>
      </header>
      <div className="flex flex-wrap gap-3">
        <Button asChild>
          <Link to="/login">{t("auth.login")}</Link>
        </Button>
        <Button asChild variant="outline">
          <Link to="/register">{t("auth.register")}</Link>
        </Button>
      </div>
    </section>
  );
}
