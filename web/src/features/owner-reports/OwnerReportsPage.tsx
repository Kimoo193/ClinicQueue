import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  useEffect,
  useId,
  useRef,
  useState,
  type FormEvent,
  type ReactNode,
} from "react";
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

function peakDayLabel(day: string, t: (key: string) => string): string {
  const key = `ownerReports.weekday.${day}`;
  const labeled = t(key);
  return labeled === key ? day : labeled;
}

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
  const [revokeTarget, setRevokeTarget] = useState<{
    membershipId: string;
    label: string;
  } | null>(null);

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
      setRevokeTarget(null);
      await queryClient.invalidateQueries({ queryKey: ["owner-staff"] });
    },
    onError: (error: Error) => setActionError(error.message),
  });

  const onInvite = (event: FormEvent) => {
    event.preventDefault();
    inviteMutation.mutate();
  };

  const reportsLoading =
    volumeQuery.isLoading ||
    absenceQuery.isLoading ||
    peakQuery.isLoading ||
    revenueQuery.isLoading;

  const reportsError =
    volumeQuery.isError ||
    absenceQuery.isError ||
    peakQuery.isError ||
    revenueQuery.isError;

  const volumeSeries = volumeQuery.data?.series ?? [];
  const volumeTotal = volumeSeries.reduce((sum, p) => sum + p.count, 0);
  const volumeMax = Math.max(1, ...volumeSeries.map((p) => p.count));
  const hasVolumeHistory = volumeSeries.length >= 2 && volumeTotal > 0;

  const absencePct =
    absenceQuery.data != null
      ? Math.round(absenceQuery.data.absenceRate * 1000) / 10
      : null;

  const peakDays = peakQuery.data?.peakDays ?? [];
  const peakHours = peakQuery.data?.peakHours ?? [];
  const hasPeakHistory = peakDays.length > 0 || peakHours.length > 0;

  const depositRevenue = revenueQuery.data?.depositRevenue ?? 0;
  const hasRevenueHistory = depositRevenue > 0;

  const volumeInsight = !hasVolumeHistory
    ? null
    : (() => {
        const peak = volumeSeries.reduce((best, point) =>
          point.count > best.count ? point : best,
        );
        return t("ownerReports.insightVolume", {
          date: peak.date,
          count: peak.count,
        });
      })();

  const absenceInsight =
    absencePct == null
      ? null
      : absencePct >= 15
        ? t("ownerReports.insightAbsenceHigh", { rate: absencePct })
        : t("ownerReports.insightAbsenceOk", { rate: absencePct });

  const peakInsight = !hasPeakHistory
    ? null
    : peakDays.length > 0
      ? t("ownerReports.insightPeakDay", {
          day: peakDayLabel(peakDays[0]!, t),
        })
      : t("ownerReports.insightPeakHour", {
          hour: `${peakHours[0]}:00`,
        });

  const revenueInsight = hasRevenueHistory
    ? t("ownerReports.insightRevenue", { count: depositRevenue })
    : null;

  return (
    <div className="space-y-8">
      <div className="cq-page cq-owner-reports">
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
                <option value="monthly">
                  {t("ownerReports.periodMonthly")}
                </option>
              </select>
            </label>
          </div>
        </section>

        {reportsLoading ? (
          <p className="cq-status">{t("ownerReports.loading")}</p>
        ) : reportsError ? (
          <p className="cq-alert" role="alert">
            {t("ownerReports.loadError")}
          </p>
        ) : (
          <div className="flex flex-col gap-6">
            <ChartCard
              title={t("ownerReports.chartVolume")}
              insight={volumeInsight}
              empty={!hasVolumeHistory}
              emptyLabel={t("ownerReports.emptyHistory")}
            >
              {hasVolumeHistory ? (
                <ul className="cq-chart-bars" aria-label={t("ownerReports.chartVolume")}>
                  {volumeSeries.map((point) => (
                    <li key={point.date} className="cq-chart-bar-row">
                      <span className="cq-chart-bar-label">{point.date}</span>
                      <span className="cq-chart-bar-track">
                        <span
                          className="cq-chart-bar-fill"
                          style={{
                            width: `${Math.max(
                              4,
                              (point.count / volumeMax) * 100,
                            )}%`,
                          }}
                        />
                      </span>
                      <span className="cq-chart-bar-value tabular-nums">
                        {point.count}
                      </span>
                    </li>
                  ))}
                </ul>
              ) : null}
            </ChartCard>

            <ChartCard
              title={t("ownerReports.chartAbsence")}
              insight={absenceInsight}
              empty={absencePct == null}
              emptyLabel={t("ownerReports.emptyHistory")}
            >
              {absencePct != null ? (
                <div className="cq-chart-gauge" aria-label={t("ownerReports.chartAbsence")}>
                  <div className="cq-chart-gauge-track">
                    <div
                      className="cq-chart-gauge-fill"
                      style={{
                        width: `${Math.min(100, Math.max(2, absencePct))}%`,
                      }}
                    />
                  </div>
                  <p className="cq-chart-gauge-value tabular-nums">
                    {absencePct}%
                  </p>
                </div>
              ) : null}
            </ChartCard>

            <ChartCard
              title={t("ownerReports.chartPeaks")}
              insight={peakInsight}
              empty={!hasPeakHistory}
              emptyLabel={t("ownerReports.emptyHistory")}
            >
              {hasPeakHistory ? (
                <div className="grid gap-4 sm:grid-cols-2">
                  <div>
                    <p className="mb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">
                      {t("ownerReports.metricPeakDays")}
                    </p>
                    {peakDays.length === 0 ? (
                      <p className="text-sm text-muted-foreground">—</p>
                    ) : (
                      <ul className="cq-chart-bars">
                        {peakDays.map((day, index) => (
                          <li key={day} className="cq-chart-bar-row">
                            <span className="cq-chart-bar-label">
                              {peakDayLabel(day, t)}
                            </span>
                            <span className="cq-chart-bar-track">
                              <span
                                className="cq-chart-bar-fill"
                                data-tone="secondary"
                                style={{
                                  width: `${Math.max(20, 100 - index * 18)}%`,
                                }}
                              />
                            </span>
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                  <div>
                    <p className="mb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">
                      {t("ownerReports.metricPeakHours")}
                    </p>
                    {peakHours.length === 0 ? (
                      <p className="text-sm text-muted-foreground">—</p>
                    ) : (
                      <ul className="cq-chart-bars">
                        {peakHours.map((hour, index) => (
                          <li key={hour} className="cq-chart-bar-row">
                            <span className="cq-chart-bar-label tabular-nums">
                              {hour}:00
                            </span>
                            <span className="cq-chart-bar-track">
                              <span
                                className="cq-chart-bar-fill"
                                data-tone="secondary"
                                style={{
                                  width: `${Math.max(20, 100 - index * 18)}%`,
                                }}
                              />
                            </span>
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                </div>
              ) : null}
            </ChartCard>

            <ChartCard
              title={t("ownerReports.chartRevenue")}
              insight={revenueInsight}
              empty={!hasRevenueHistory}
              emptyLabel={t("ownerReports.emptyRevenue")}
            >
              {hasRevenueHistory ? (
                <p className="cq-chart-big-number tabular-nums">
                  {depositRevenue}
                  <span className="cq-chart-big-number-unit">
                    {t("ownerReports.revenueUnit")}
                  </span>
                </p>
              ) : null}
            </ChartCard>
          </div>
        )}

        <section className="cq-section" aria-labelledby="owner-staff">
          <h2 id="owner-staff" className="cq-section-title">
            {t("ownerReports.staff")}
          </h2>
          <p className="text-sm text-muted-foreground">
            {t("ownerReports.staffHint")}
          </p>

          <form
            className="flex flex-col gap-3 rounded-xl border border-border bg-surface p-4 sm:flex-row sm:flex-wrap sm:items-end"
            onSubmit={onInvite}
          >
            <label className="cq-label min-w-[12rem] flex-1">
              <span>{t("ownerReports.inviteEmail")}</span>
              <input
                type="email"
                required
                autoComplete="email"
                value={inviteEmail}
                onChange={(e) => setInviteEmail(e.target.value)}
                className="cq-field"
              />
            </label>
            <label className="cq-label min-w-[10rem] flex-1">
              <span>{t("ownerReports.inviteName")}</span>
              <input
                required
                autoComplete="name"
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
          ) : staffQuery.isError ? (
            <p className="cq-alert" role="alert">
              {t("ownerReports.staffLoadError")}
            </p>
          ) : !staffQuery.data?.length ? (
            <p className="rounded-xl border border-dashed border-border bg-surface px-4 py-8 text-center text-sm text-muted-foreground">
              {t("ownerReports.noStaff")}
            </p>
          ) : (
            <ul className="cq-list">
              {staffQuery.data.map((row) => (
                <li
                  key={row.membershipId}
                  className="flex flex-wrap items-center justify-between gap-2 py-3"
                >
                  <div className="text-sm">
                    <p className="font-medium">{row.emailOrPhone}</p>
                    <p className="text-muted-foreground">
                      {t(`ownerReports.staffStatus.${row.status}`, {
                        defaultValue: row.status,
                      })}
                    </p>
                  </div>
                  {row.status === "Active" ? (
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      disabled={revokeMutation.isPending}
                      onClick={() =>
                        setRevokeTarget({
                          membershipId: row.membershipId,
                          label: row.emailOrPhone,
                        })
                      }
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

      {revokeTarget ? (
        <ConfirmDialog
          title={t("ownerReports.confirmRevokeTitle")}
          body={t("ownerReports.confirmRevokeBody", {
            name: revokeTarget.label,
          })}
          confirmLabel={t("ownerReports.revoke")}
          cancelLabel={t("ownerReports.confirmCancel")}
          busy={revokeMutation.isPending}
          destructive
          onCancel={() => setRevokeTarget(null)}
          onConfirm={() => revokeMutation.mutate(revokeTarget.membershipId)}
        />
      ) : null}
    </div>
  );
}

function ChartCard({
  title,
  insight,
  empty,
  emptyLabel,
  children,
}: {
  title: string;
  insight: string | null;
  empty: boolean;
  emptyLabel: string;
  children: ReactNode;
}) {
  return (
    <section className="cq-chart-card" aria-label={title}>
      <h3 className="cq-section-title">{title}</h3>
      {empty ? (
        <p className="rounded-lg border border-dashed border-border px-4 py-6 text-center text-sm text-muted-foreground">
          {emptyLabel}
        </p>
      ) : (
        <>
          {children}
          {insight ? <p className="cq-chart-insight">{insight}</p> : null}
        </>
      )}
    </section>
  );
}

function ConfirmDialog({
  title,
  body,
  confirmLabel,
  cancelLabel,
  busy,
  destructive,
  onCancel,
  onConfirm,
}: {
  title: string;
  body: string;
  confirmLabel: string;
  cancelLabel: string;
  busy: boolean;
  destructive?: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const { t } = useTranslation();
  const titleId = useId();
  const panelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const focusable = panelRef.current?.querySelector<HTMLElement>(
      "button[data-confirm-cancel]",
    );
    focusable?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) onCancel();
    };
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("keydown", onKey);
      previouslyFocused?.focus();
    };
  }, [busy, onCancel]);

  return (
    <div
      className="cq-confirm-backdrop"
      role="presentation"
      onClick={() => {
        if (!busy) onCancel();
      }}
    >
      <div
        ref={panelRef}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="cq-confirm-panel"
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id={titleId} className="text-lg font-semibold text-foreground">
          {title}
        </h2>
        <p className="text-sm text-muted-foreground">{body}</p>
        <div className="flex flex-wrap justify-end gap-2">
          <Button
            type="button"
            variant="outline"
            disabled={busy}
            data-confirm-cancel=""
            onClick={onCancel}
          >
            {cancelLabel}
          </Button>
          <Button
            type="button"
            variant={destructive ? "destructive" : "default"}
            disabled={busy}
            onClick={onConfirm}
          >
            {busy ? t("ownerReports.confirmWorking") : confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}
