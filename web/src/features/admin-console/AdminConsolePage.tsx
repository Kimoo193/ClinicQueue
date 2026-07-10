import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";

import {
  activateClinic,
  getClinicExport,
  getPlatformAnalytics,
  listAdminClinics,
  startClinicExport,
  suspendClinic,
  updateSubscription,
  type AdminClinic,
} from "./api";

function downloadJson(filename: string, data: unknown) {
  const blob = new Blob([JSON.stringify(data, null, 2)], {
    type: "application/json",
  });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  URL.revokeObjectURL(url);
}

export function AdminConsolePage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [statusFilter, setStatusFilter] = useState<string>("");
  const [actionError, setActionError] = useState<string | null>(null);
  const [selected, setSelected] = useState<AdminClinic | null>(null);
  const [planName, setPlanName] = useState("standard");
  const [analyticsFrom, setAnalyticsFrom] = useState("");
  const [analyticsTo, setAnalyticsTo] = useState("");
  const [exportMessage, setExportMessage] = useState<string | null>(null);
  const [renewalDate, setRenewalDate] = useState(() => {
    const d = new Date();
    d.setUTCDate(d.getUTCDate() + 30);
    return d.toISOString().slice(0, 10);
  });

  const clinicsQuery = useQuery({
    queryKey: ["admin-clinics", statusFilter],
    queryFn: () => listAdminClinics(statusFilter || undefined),
  });

  const analyticsQuery = useQuery({
    queryKey: ["admin-analytics", analyticsFrom, analyticsTo],
    queryFn: () =>
      getPlatformAnalytics({
        from: analyticsFrom ? `${analyticsFrom}T00:00:00.000Z` : undefined,
        to: analyticsTo ? `${analyticsTo}T23:59:59.999Z` : undefined,
      }),
  });

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ["admin-clinics"] });
  };

  const activateMutation = useMutation({
    mutationFn: activateClinic,
    onSuccess: async () => {
      setActionError(null);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const suspendMutation = useMutation({
    mutationFn: suspendClinic,
    onSuccess: async () => {
      setActionError(null);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const subscriptionMutation = useMutation({
    mutationFn: ({
      clinicId,
      plan,
      renewal,
    }: {
      clinicId: string;
      plan: string;
      renewal: string;
    }) => updateSubscription(clinicId, plan, `${renewal}T00:00:00.000Z`),
    onSuccess: async () => {
      setActionError(null);
      await invalidate();
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const exportMutation = useMutation({
    mutationFn: async (clinic: AdminClinic) => {
      const started = await startClinicExport(clinic.clinicId);
      const payload = await getClinicExport(clinic.clinicId, started.exportJobId);
      return { clinic, started, payload };
    },
    onSuccess: ({ clinic, started, payload }) => {
      setActionError(null);
      setExportMessage(
        t("admin.exportReady", {
          name: clinic.name,
          jobId: started.exportJobId,
          patients: payload.patients.length,
          appointments: payload.appointments.length,
          visits: payload.visits.length,
        }),
      );
      downloadJson(
        `clinic-${clinic.clinicId}-export.json`,
        payload,
      );
    },
    onError: (error: Error) => {
      setExportMessage(null);
      setActionError(error.message);
    },
  });

  const onSaveSubscription = (e: FormEvent) => {
    e.preventDefault();
    if (!selected) {
      return;
    }
    subscriptionMutation.mutate({
      clinicId: selected.clinicId,
      plan: planName,
      renewal: renewalDate,
    });
  };

  const selectClinic = (clinic: AdminClinic) => {
    setSelected(clinic);
    setPlanName(clinic.planName ?? "standard");
    if (clinic.renewalDate) {
      setRenewalDate(clinic.renewalDate.slice(0, 10));
    }
  };

  return (
    <section className="cq-page">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("admin.title")}</h1>
        <p className="cq-page-subtitle">{t("admin.subtitle")}</p>
      </header>

      <section className="cq-section">
        <h2 className="cq-section-title">{t("admin.analyticsTitle")}</h2>
        <p className="text-sm text-muted-foreground">
          {t("admin.analyticsSubtitle")}
        </p>
        <div className="flex flex-wrap gap-3">
          <label className="cq-label">
            <span>{t("admin.analyticsFrom")}</span>
            <input
              type="date"
              value={analyticsFrom}
              onChange={(e) => setAnalyticsFrom(e.target.value)}
              className="cq-field"
            />
          </label>
          <label className="cq-label">
            <span>{t("admin.analyticsTo")}</span>
            <input
              type="date"
              value={analyticsTo}
              onChange={(e) => setAnalyticsTo(e.target.value)}
              className="cq-field"
            />
          </label>
          <div className="flex items-end">
            <Button
              type="button"
              variant="outline"
              onClick={() => void analyticsQuery.refetch()}
            >
              {t("admin.refreshAnalytics")}
            </Button>
          </div>
        </div>
        {analyticsQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">
            {t("admin.analyticsLoading")}
          </p>
        ) : analyticsQuery.isError ? (
          <p className="cq-alert" role="alert">
            {(analyticsQuery.error as Error).message}
          </p>
        ) : analyticsQuery.data ? (
          <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <div className="cq-metric">
              <dt>{t("admin.metricClinics")}</dt>
              <dd>
                {analyticsQuery.data.usage.activeClinicCount}/
                {analyticsQuery.data.usage.clinicCount}
              </dd>
            </div>
            <div className="cq-metric">
              <dt>{t("admin.metricAppointments")}</dt>
              <dd>{analyticsQuery.data.usage.appointmentCount}</dd>
            </div>
            <div className="cq-metric">
              <dt>{t("admin.metricVisits")}</dt>
              <dd>{analyticsQuery.data.usage.visitCount}</dd>
            </div>
            <div className="cq-metric">
              <dt>{t("admin.metricPatients")}</dt>
              <dd>{analyticsQuery.data.usage.patientProfileCount}</dd>
            </div>
            <div className="cq-metric">
              <dt>{t("admin.metricDeposits")}</dt>
              <dd>{analyticsQuery.data.revenue.confirmedDepositCount}</dd>
            </div>
            <div className="cq-metric">
              <dt>{t("admin.metricSubscriptions")}</dt>
              <dd>
                {analyticsQuery.data.revenue.activeSubscriptionCount}/
                {analyticsQuery.data.revenue.expiredSubscriptionCount}
              </dd>
            </div>
          </dl>
        ) : null}
      </section>

      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          size="sm"
          variant={statusFilter === "" ? "default" : "outline"}
          onClick={() => setStatusFilter("")}
        >
          {t("admin.filterAll")}
        </Button>
        <Button
          type="button"
          size="sm"
          variant={statusFilter === "Active" ? "default" : "outline"}
          onClick={() => setStatusFilter("Active")}
        >
          {t("admin.filterActive")}
        </Button>
        <Button
          type="button"
          size="sm"
          variant={statusFilter === "Suspended" ? "default" : "outline"}
          onClick={() => setStatusFilter("Suspended")}
        >
          {t("admin.filterSuspended")}
        </Button>
      </div>

      {actionError ? (
        <p className="cq-alert" role="alert">
          {actionError}
        </p>
      ) : null}
      {exportMessage ? (
        <p className="cq-status" role="status">
          {exportMessage}
        </p>
      ) : null}

      {clinicsQuery.isLoading ? (
        <p className="text-sm text-muted-foreground">{t("admin.loading")}</p>
      ) : clinicsQuery.isError ? (
        <p className="cq-alert" role="alert">
          {(clinicsQuery.error as Error).message}
        </p>
      ) : (clinicsQuery.data?.items.length ?? 0) === 0 ? (
        <p className="text-sm text-muted-foreground">{t("admin.empty")}</p>
      ) : (
        <ul className="cq-list-plain">
          {clinicsQuery.data!.items.map((clinic) => (
            <li
              key={clinic.clinicId}
              className="flex flex-col gap-3 py-4"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <button
                  type="button"
                  className="rounded-md text-start focus-visible:ring-3 focus-visible:ring-ring/40 focus-visible:outline-none"
                  onClick={() => selectClinic(clinic)}
                >
                  <p className="font-medium">{clinic.name}</p>
                  <p className="font-mono text-xs text-muted-foreground">
                    {clinic.clinicId}
                  </p>
                  <p className="mt-1 text-sm">
                    {t("admin.clinicStatus", { status: clinic.status })}
                    {" · "}
                    {t("admin.subscriptionStatus", {
                      status: clinic.subscriptionStatus ?? "—",
                    })}
                  </p>
                  {clinic.planName ? (
                    <p className="text-xs text-muted-foreground">
                      {t("admin.plan", {
                        plan: clinic.planName,
                        renewal: clinic.renewalDate
                          ? clinic.renewalDate.slice(0, 10)
                          : "—",
                      })}
                    </p>
                  ) : null}
                </button>
                <div className="flex flex-wrap gap-2">
                  {clinic.status === "Suspended" ? (
                    <Button
                      type="button"
                      size="sm"
                      disabled={activateMutation.isPending}
                      onClick={() => activateMutation.mutate(clinic.clinicId)}
                    >
                      {t("admin.activate")}
                    </Button>
                  ) : (
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      disabled={suspendMutation.isPending}
                      onClick={() => suspendMutation.mutate(clinic.clinicId)}
                    >
                      {t("admin.suspend")}
                    </Button>
                  )}
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() => selectClinic(clinic)}
                  >
                    {t("admin.manageSubscription")}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    disabled={exportMutation.isPending}
                    onClick={() => exportMutation.mutate(clinic)}
                  >
                    {exportMutation.isPending
                      ? t("admin.exporting")
                      : t("admin.exportData")}
                  </Button>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}

      {selected ? (
        <form onSubmit={onSaveSubscription} className="cq-section gap-3">
          <h2 className="cq-section-title">
            {t("admin.subscriptionTitle", { name: selected.name })}
          </h2>
          <label className="cq-label">
            <span>{t("admin.planName")}</span>
            <input
              value={planName}
              onChange={(e) => setPlanName(e.target.value)}
              className="cq-field"
              required
            />
          </label>
          <label className="cq-label">
            <span>{t("admin.renewalDate")}</span>
            <input
              type="date"
              value={renewalDate}
              onChange={(e) => setRenewalDate(e.target.value)}
              className="cq-field"
              required
            />
          </label>
          <Button type="submit" disabled={subscriptionMutation.isPending}>
            {subscriptionMutation.isPending
              ? t("admin.saving")
              : t("admin.saveSubscription")}
          </Button>
        </form>
      ) : null}
    </section>
  );
}
