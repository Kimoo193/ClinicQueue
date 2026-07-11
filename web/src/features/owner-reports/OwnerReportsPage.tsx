import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useActiveClinic } from "@/features/auth/AuthProvider";
import { DoctorConsolePage } from "@/features/doctor-console/DoctorConsolePage";

import {
  getAbsenceRateReport,
  getPeakHoursReport,
  getRevenueReport,
  getVolumeReport,
  inviteReceptionist,
  listReceptionists,
  revokeReceptionist,
} from "./api";

export function OwnerReportsPage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { id: clinicId } = useActiveClinic();
  const [actionError, setActionError] = useState<string | null>(null);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [period, setPeriod] = useState<"daily" | "weekly" | "monthly">("daily");
  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteName, setInviteName] = useState("");

  const range = { from: from || undefined, to: to || undefined };

  const volumeQuery = useQuery({
    queryKey: ["owner-volume", clinicId, from, to, period],
    queryFn: () => getVolumeReport(clinicId, { ...range, period }),
  });

  const absenceQuery = useQuery({
    queryKey: ["owner-absence", clinicId, from, to],
    queryFn: () => getAbsenceRateReport(clinicId, range),
  });

  const peakQuery = useQuery({
    queryKey: ["owner-peak", clinicId, from, to],
    queryFn: () => getPeakHoursReport(clinicId, range),
  });

  const revenueQuery = useQuery({
    queryKey: ["owner-revenue", clinicId, from, to],
    queryFn: () => getRevenueReport(clinicId, range),
  });

  const staffQuery = useQuery({
    queryKey: ["owner-staff", clinicId],
    queryFn: () => listReceptionists(clinicId),
  });

  const inviteMutation = useMutation({
    mutationFn: () =>
      inviteReceptionist(clinicId, { email: inviteEmail, name: inviteName }),
    onSuccess: async () => {
      setActionError(null);
      setInviteEmail("");
      setInviteName("");
      await queryClient.invalidateQueries({ queryKey: ["owner-staff"] });
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const revokeMutation = useMutation({
    mutationFn: (membershipId: string) =>
      revokeReceptionist(clinicId, membershipId),
    onSuccess: async () => {
      setActionError(null);
      await queryClient.invalidateQueries({ queryKey: ["owner-staff"] });
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const onInvite = (event: FormEvent) => {
    event.preventDefault();
    inviteMutation.mutate();
  };

  const absencePct =
    absenceQuery.data != null
      ? Math.round(absenceQuery.data.absenceRate * 1000) / 10
      : null;

  return (
    <div className="space-y-8">
      <div className="cq-page">
        <header className="cq-page-header">
          <h1 className="cq-page-title">{t("ownerReports.title")}</h1>
          <p className="cq-page-subtitle">{t("ownerReports.subtitle")}</p>
        </header>

        {actionError ? (
          <p className="cq-alert" role="alert">
            {actionError}
          </p>
        ) : null}

        <section className="cq-section" aria-labelledby="owner-filters">
          <h2 id="owner-filters" className="cq-section-title">
            {t("ownerReports.filters")}
          </h2>
          <div className="flex flex-wrap gap-3">
            <label className="cq-label">
              <span>{t("ownerReports.from")}</span>
              <input
                type="date"
                value={from}
                onChange={(e) => setFrom(e.target.value)}
                className="cq-field"
              />
            </label>
            <label className="cq-label">
              <span>{t("ownerReports.to")}</span>
              <input
                type="date"
                value={to}
                onChange={(e) => setTo(e.target.value)}
                className="cq-field"
              />
            </label>
            <label className="cq-label">
              <span>{t("ownerReports.period")}</span>
              <select
                value={period}
                onChange={(e) =>
                  setPeriod(e.target.value as "daily" | "weekly" | "monthly")
                }
                className="cq-field"
              >
                <option value="daily">{t("ownerReports.periodDaily")}</option>
                <option value="weekly">{t("ownerReports.periodWeekly")}</option>
                <option value="monthly">{t("ownerReports.periodMonthly")}</option>
              </select>
            </label>
          </div>
        </section>

        <section className="cq-section" aria-labelledby="owner-metrics">
          <h2 id="owner-metrics" className="cq-section-title">
            {t("ownerReports.metrics")}
          </h2>
          {volumeQuery.isLoading ||
          absenceQuery.isLoading ||
          peakQuery.isLoading ||
          revenueQuery.isLoading ? (
            <p className="cq-status">{t("ownerReports.loading")}</p>
          ) : (
            <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3">
              <div className="cq-metric">
                <dt>{t("ownerReports.metricVolume")}</dt>
                <dd>
                  {volumeQuery.data?.series.reduce((sum, p) => sum + p.count, 0) ??
                    0}
                </dd>
              </div>
              <div className="cq-metric">
                <dt>{t("ownerReports.metricAbsence")}</dt>
                <dd>{absencePct != null ? `${absencePct}%` : "—"}</dd>
              </div>
              <div className="cq-metric">
                <dt>{t("ownerReports.metricRevenue")}</dt>
                <dd>{revenueQuery.data?.depositRevenue ?? 0}</dd>
              </div>
              <div className="cq-metric">
                <dt>{t("ownerReports.metricPeakDays")}</dt>
                <dd>
                  {(peakQuery.data?.peakDays ?? []).join(", ") || "—"}
                </dd>
              </div>
              <div className="cq-metric">
                <dt>{t("ownerReports.metricPeakHours")}</dt>
                <dd>
                  {(peakQuery.data?.peakHours ?? [])
                    .map((h) => `${h}:00`)
                    .join(", ") || "—"}
                </dd>
              </div>
            </dl>
          )}

          {volumeQuery.data && volumeQuery.data.series.length > 0 ? (
            <ul className="cq-list mt-4">
              {volumeQuery.data.series.map((point) => (
                <li key={point.date}>
                  {point.date}: {point.count}
                </li>
              ))}
            </ul>
          ) : null}
        </section>

        <section className="cq-section" aria-labelledby="owner-staff">
          <h2 id="owner-staff" className="cq-section-title">
            {t("ownerReports.staff")}
          </h2>

          <form className="mb-4 flex flex-wrap gap-3" onSubmit={onInvite}>
            <label className="cq-label">
              <span>{t("ownerReports.inviteEmail")}</span>
              <input
                type="email"
                required
                value={inviteEmail}
                onChange={(e) => setInviteEmail(e.target.value)}
                className="cq-field"
              />
            </label>
            <label className="cq-label">
              <span>{t("ownerReports.inviteName")}</span>
              <input
                required
                value={inviteName}
                onChange={(e) => setInviteName(e.target.value)}
                className="cq-field"
              />
            </label>
            <Button type="submit" disabled={inviteMutation.isPending}>
              {inviteMutation.isPending
                ? t("ownerReports.inviting")
                : t("ownerReports.invite")}
            </Button>
          </form>

          {staffQuery.isLoading ? (
            <p className="cq-status">{t("ownerReports.loading")}</p>
          ) : !staffQuery.data?.length ? (
            <p className="cq-status">{t("ownerReports.noStaff")}</p>
          ) : (
            <ul className="cq-list">
              {staffQuery.data.map((row) => (
                <li
                  key={row.membershipId}
                  className="flex flex-wrap items-center gap-2"
                >
                  <span>
                    {row.emailOrPhone} · {row.status}
                  </span>
                  {row.status === "Active" ? (
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      disabled={revokeMutation.isPending}
                      onClick={() => revokeMutation.mutate(row.membershipId)}
                    >
                      {t("ownerReports.revoke")}
                    </Button>
                  ) : null}
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <DoctorConsolePage readOnlyClinical />
    </div>
  );
}
