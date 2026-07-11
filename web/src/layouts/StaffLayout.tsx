import { Navigate, Outlet } from "react-router";
import { useTranslation } from "react-i18next";

import { useAuth } from "@/features/auth/AuthProvider";
import { hasAnyRole } from "@/features/auth/roleHome";
import { AppHeader } from "@/layouts/AppHeader";

const STAFF_ROLES = ["Receptionist", "Doctor", "ClinicOwner"] as const;

export function StaffLayout() {
  const { t } = useTranslation();
  const { user, activeClinic, isLoading } = useAuth();

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

  if (!activeClinic) {
    return <Navigate to="/find-clinic" replace />;
  }

  if (!hasAnyRole(activeClinic.roles, STAFF_ROLES)) {
    return <Navigate to="/forbidden" replace />;
  }

  return (
    <main id="main-content" className="cq-app-shell">
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:absolute focus:start-4 focus:top-4 focus:z-50 focus:rounded-md focus:border focus:border-border focus:bg-card focus:px-3 focus:py-2 focus:shadow-sm"
      >
        {t("a11y.skipToContent")}
      </a>
      <div className="cq-app-frame">
        <AppHeader navRoles={activeClinic.roles} />
        <Outlet />
      </div>
    </main>
  );
}
