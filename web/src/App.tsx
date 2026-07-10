import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { AdminConsolePage } from "@/features/admin-console/AdminConsolePage";
import { DoctorConsolePage } from "@/features/doctor-console/DoctorConsolePage";
import { PatientBookingsPage } from "@/features/patient-bookings/PatientBookingsPage";
import { PatientQueuePage } from "@/features/patient-queue/PatientQueuePage";
import { ReceptionConsolePage } from "@/features/reception-console/ReceptionConsolePage";
import { applyDocumentDirection } from "@/i18n";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
    },
  },
});

type View = "patient" | "bookings" | "reception" | "doctor" | "owner" | "admin";

function AppShell() {
  const { t, i18n } = useTranslation();
  const [clinicId, setClinicId] = useState(
    () => localStorage.getItem("cq_clinic_id") ?? "",
  );
  const [view, setView] = useState<View>(
    () => (localStorage.getItem("cq_view") as View) ?? "patient",
  );

  useEffect(() => {
    applyDocumentDirection(i18n.language);
  }, [i18n.language]);

  const toggleLanguage = () => {
    const next = i18n.language.startsWith("ar") ? "en" : "ar";
    void i18n.changeLanguage(next);
  };

  const onClinicIdChange = (value: string) => {
    setClinicId(value);
    localStorage.setItem("cq_clinic_id", value);
  };

  const onViewChange = (next: View) => {
    setView(next);
    localStorage.setItem("cq_view", next);
  };

  return (
    <main id="main-content" className="cq-app-shell" lang={i18n.language}>
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:absolute focus:start-4 focus:top-4 focus:z-50 focus:rounded-md focus:border focus:border-border focus:bg-card focus:px-3 focus:py-2 focus:shadow-sm"
      >
        {t("a11y.skipToContent")}
      </a>
      <div className="cq-app-frame">
        <header className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0 space-y-1">
            <p className="cq-brand-mark">{t("app.name")}</p>
            <p className="text-sm text-muted-foreground">{t("app.tagline")}</p>
          </div>
          <Button
            type="button"
            variant="outline"
            onClick={toggleLanguage}
            aria-label={t("language.toggle")}
          >
            {i18n.language.startsWith("ar") ? t("language.en") : t("language.ar")}
          </Button>
        </header>

        <nav className="cq-nav" aria-label={t("a11y.primaryNav")}>
          <Button
            type="button"
            size="sm"
            variant={view === "patient" ? "default" : "ghost"}
            onClick={() => onViewChange("patient")}
            aria-current={view === "patient" ? "page" : undefined}
          >
            {t("nav.patient")}
          </Button>
          <Button
            type="button"
            size="sm"
            variant={view === "bookings" ? "default" : "ghost"}
            onClick={() => onViewChange("bookings")}
            aria-current={view === "bookings" ? "page" : undefined}
          >
            {t("nav.bookings")}
          </Button>
          <Button
            type="button"
            size="sm"
            variant={view === "reception" ? "default" : "ghost"}
            onClick={() => onViewChange("reception")}
            aria-current={view === "reception" ? "page" : undefined}
          >
            {t("nav.reception")}
          </Button>
          <Button
            type="button"
            size="sm"
            variant={view === "doctor" ? "default" : "ghost"}
            onClick={() => onViewChange("doctor")}
            aria-current={view === "doctor" ? "page" : undefined}
          >
            {t("nav.doctor")}
          </Button>
          <Button
            type="button"
            size="sm"
            variant={view === "owner" ? "default" : "ghost"}
            onClick={() => onViewChange("owner")}
            aria-current={view === "owner" ? "page" : undefined}
          >
            {t("nav.owner")}
          </Button>
          <Button
            type="button"
            size="sm"
            variant={view === "admin" ? "default" : "ghost"}
            onClick={() => onViewChange("admin")}
            aria-current={view === "admin" ? "page" : undefined}
          >
            {t("nav.admin")}
          </Button>
        </nav>

        {view !== "admin" ? (
          <label className="cq-label max-w-md">
            <span>{t("app.clinicId")}</span>
            <input
              value={clinicId}
              onChange={(e) => onClinicIdChange(e.target.value)}
              placeholder={t("app.clinicIdPlaceholder")}
              aria-label={t("app.clinicId")}
              className="cq-field-mono"
            />
          </label>
        ) : null}

        {view === "admin" ? (
          <AdminConsolePage />
        ) : clinicId ? (
          view === "reception" ? (
            <ReceptionConsolePage clinicId={clinicId} />
          ) : view === "doctor" ? (
            <DoctorConsolePage clinicId={clinicId} />
          ) : view === "owner" ? (
            <DoctorConsolePage clinicId={clinicId} readOnlyClinical />
          ) : view === "bookings" ? (
            <PatientBookingsPage clinicId={clinicId} />
          ) : (
            <PatientQueuePage clinicId={clinicId} />
          )
        ) : (
          <p className="cq-banner-warning" role="status">
            {t("status.enterClinicId")}
          </p>
        )}
      </div>
    </main>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AppShell />
    </QueryClientProvider>
  );
}
