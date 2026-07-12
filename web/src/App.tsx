import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";

import { AuthProvider } from "@/features/auth/AuthProvider";
import { LoginPage } from "@/features/auth/LoginPage";
import { RegisterPage } from "@/features/auth/RegisterPage";
import { AdminConsolePage } from "@/features/admin-console/AdminConsolePage";
import { DoctorConsolePage } from "@/features/doctor-console/DoctorConsolePage";
import { OwnerReportsPage } from "@/features/owner-reports/OwnerReportsPage";
import { OwnerSettingsPage } from "@/features/owner-settings/OwnerSettingsPage";
import { PatientBookingsPage } from "@/features/patient-bookings/PatientBookingsPage";
import { PatientMedicalPage } from "@/features/patient-medical/PatientMedicalPage";
import { PatientQueuePage } from "@/features/patient-queue/PatientQueuePage";
import { QueueStatesPreviewPage } from "@/features/patient-queue/QueueStatesPreviewPage";
import { QueueHubProvider } from "@/features/realtime/QueueHubProvider";
import { ReceptionConsolePage } from "@/features/reception-console/ReceptionConsolePage";
import { ReceptionDayClosePage } from "@/features/reception-dayclose/ReceptionDayClosePage";
import { AdminLayout } from "@/layouts/AdminLayout";
import { PatientLayout } from "@/layouts/PatientLayout";
import { PublicLayout } from "@/layouts/PublicLayout";
import { RequireRole } from "@/layouts/RequireRole";
import { StaffLayout } from "@/layouts/StaffLayout";
import { ChooseClinicPage } from "@/pages/ChooseClinicPage";
import { ClinicEntryPage } from "@/pages/ClinicEntryPage";
import { FindClinicPage } from "@/pages/FindClinicPage";
import { ForbiddenPage } from "@/pages/ForbiddenPage";
import { LandingPage } from "@/pages/LandingPage";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
    },
  },
});

function AppRoutes() {
  return (
    <Routes>
      <Route element={<PublicLayout />}>
        <Route index element={<LandingPage />} />
        <Route path="login" element={<LoginPage />} />
        <Route path="register" element={<RegisterPage />} />
        <Route path="find-clinic" element={<FindClinicPage />} />
        <Route path="choose-clinic" element={<ChooseClinicPage />} />
        <Route path="r/:clinicSlug" element={<ClinicEntryPage />} />
      </Route>

      {import.meta.env.DEV ? (
        <Route path="dev/queue-states" element={<QueueStatesPreviewPage />} />
      ) : null}

      <Route path="app" element={<PatientLayout />}>
        <Route index element={<Navigate to="queue" replace />} />
        <Route path="queue" element={<PatientQueuePage />} />
        <Route path="bookings" element={<PatientBookingsPage />} />
        <Route path="medical" element={<PatientMedicalPage />} />
      </Route>

      <Route path="reception" element={<StaffLayout />}>
        <Route element={<RequireRole roles={["Receptionist"]} />}>
          <Route index element={<ReceptionConsolePage />} />
          <Route path="day-close" element={<ReceptionDayClosePage />} />
        </Route>
      </Route>

      <Route path="doctor" element={<StaffLayout />}>
        <Route element={<RequireRole roles={["Doctor"]} />}>
          <Route index element={<DoctorConsolePage />} />
        </Route>
      </Route>

      <Route path="owner" element={<StaffLayout />}>
        <Route element={<RequireRole roles={["ClinicOwner"]} />}>
          <Route index element={<Navigate to="reports" replace />} />
          <Route path="reports" element={<OwnerReportsPage />} />
          <Route path="settings" element={<OwnerSettingsPage />} />
        </Route>
      </Route>

      <Route path="admin" element={<AdminLayout />}>
        <Route index element={<AdminConsolePage />} />
      </Route>

      <Route path="forbidden" element={<ForbiddenPage />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <QueueHubProvider>
            <AppRoutes />
          </QueueHubProvider>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
