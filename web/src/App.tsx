import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
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

type View = "patient" | "bookings" | "reception" | "doctor" | "owner";

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
    <main className="min-h-screen bg-gradient-to-b from-emerald-50 via-stone-50 to-amber-50">
      <div className="mx-auto flex max-w-3xl flex-col gap-4 px-4 py-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-2xl font-semibold tracking-tight">{t("app.name")}</p>
            <p className="text-muted-foreground text-sm">{t("app.tagline")}</p>
          </div>
          <Button type="button" variant="outline" onClick={toggleLanguage}>
            {i18n.language.startsWith("ar") ? t("language.en") : t("language.ar")}
          </Button>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant={view === "patient" ? "default" : "outline"}
            onClick={() => onViewChange("patient")}
          >
            {t("nav.patient")}
          </Button>
          <Button
            type="button"
            variant={view === "bookings" ? "default" : "outline"}
            onClick={() => onViewChange("bookings")}
          >
            {t("nav.bookings")}
          </Button>
          <Button
            type="button"
            variant={view === "reception" ? "default" : "outline"}
            onClick={() => onViewChange("reception")}
          >
            {t("nav.reception")}
          </Button>
          <Button
            type="button"
            variant={view === "doctor" ? "default" : "outline"}
            onClick={() => onViewChange("doctor")}
          >
            {t("nav.doctor")}
          </Button>
          <Button
            type="button"
            variant={view === "owner" ? "default" : "outline"}
            onClick={() => onViewChange("owner")}
          >
            {t("nav.owner")}
          </Button>
        </div>

        <label className="flex flex-col gap-1 text-sm">
          <span>{t("app.clinicId")}</span>
          <input
            value={clinicId}
            onChange={(e) => onClinicIdChange(e.target.value)}
            placeholder="00000000-0000-0000-0000-000000000000"
            className="rounded-md border border-input bg-background px-3 py-2 font-mono text-xs"
          />
        </label>
      </div>

      {clinicId ? (
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
        <p className="px-6 text-sm text-muted-foreground" role="status">
          {t("status.ready")}
        </p>
      )}
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
