import { Navigate, Outlet } from "react-router";
import { useTranslation } from "react-i18next";

import { useAuth } from "@/features/auth/AuthProvider";
import { AppHeader } from "@/layouts/AppHeader";

export function AdminLayout() {
  const { t } = useTranslation();
  const { user, isLoading } = useAuth();

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

  if (!user.roles.includes("SuperAdmin")) {
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
        <AppHeader navRoles={["SuperAdmin"]} />
        <Outlet />
      </div>
    </main>
  );
}
