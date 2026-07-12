import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useActiveClinic } from "@/features/auth/AuthProvider";

import {
  getClinicBranding,
  getClinicQr,
  getClinicSettings,
  patchClinicBranding,
  patchClinicSettings,
} from "./api";

export function OwnerSettingsPage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { id: clinicId } = useActiveClinic();

  const [urgentRatio, setUrgentRatio] = useState(2);
  const [headToClinicThreshold, setHeadToClinicThreshold] = useState(4);
  const [autoAbsenceTimeoutMinutes, setAutoAbsenceTimeoutMinutes] = useState(15);
  const [repeatedAbsenceThreshold, setRepeatedAbsenceThreshold] = useState(2);
  const [timezone, setTimezone] = useState("Africa/Cairo");
  const [logoUrl, setLogoUrl] = useState("");
  const [primaryColor, setPrimaryColor] = useState("");
  const [secondaryColor, setSecondaryColor] = useState("");
  const [actionError, setActionError] = useState<string | null>(null);
  const [settingsSaved, setSettingsSaved] = useState(false);
  const [brandingSaved, setBrandingSaved] = useState(false);

  const settingsQuery = useQuery({
    queryKey: ["owner-settings", clinicId],
    queryFn: () => getClinicSettings(clinicId),
  });

  const brandingQuery = useQuery({
    queryKey: ["owner-branding", clinicId],
    queryFn: () => getClinicBranding(clinicId),
  });

  const qrQuery = useQuery({
    queryKey: ["owner-qr", clinicId],
    queryFn: () => getClinicQr(clinicId),
  });

  useEffect(() => {
    if (!settingsQuery.data) return;
    const s = settingsQuery.data.settings;
    setUrgentRatio(s.urgentRatio);
    setHeadToClinicThreshold(s.headToClinicThreshold);
    setAutoAbsenceTimeoutMinutes(s.autoAbsenceTimeoutMinutes);
    setRepeatedAbsenceThreshold(s.repeatedAbsenceThreshold);
    setTimezone(s.timezone);
  }, [settingsQuery.data]);

  useEffect(() => {
    if (!brandingQuery.data) return;
    const b = brandingQuery.data.branding;
    setLogoUrl(b.logoUrl ?? "");
    setPrimaryColor(b.primaryColor ?? "");
    setSecondaryColor(b.secondaryColor ?? "");
  }, [brandingQuery.data]);

  const settingsMutation = useMutation({
    mutationFn: () =>
      patchClinicSettings(clinicId, {
        urgentRatio,
        headToClinicThreshold,
        autoAbsenceTimeoutMinutes,
        repeatedAbsenceThreshold,
        timezone: timezone.trim() || "Africa/Cairo",
      }),
    onSuccess: async () => {
      setActionError(null);
      setSettingsSaved(true);
      setBrandingSaved(false);
      await queryClient.invalidateQueries({ queryKey: ["owner-settings"] });
    },
    onError: (error: Error) => {
      setSettingsSaved(false);
      setActionError(error.message);
    },
  });

  const brandingMutation = useMutation({
    mutationFn: () =>
      patchClinicBranding(clinicId, {
        logoUrl: logoUrl.trim() || null,
        primaryColor: primaryColor.trim() || null,
        secondaryColor: secondaryColor.trim() || null,
      }),
    onSuccess: async () => {
      setActionError(null);
      setBrandingSaved(true);
      setSettingsSaved(false);
      await queryClient.invalidateQueries({ queryKey: ["owner-branding"] });
    },
    onError: (error: Error) => {
      setBrandingSaved(false);
      setActionError(error.message);
    },
  });

  const onSaveSettings = (event: FormEvent) => {
    event.preventDefault();
    settingsMutation.mutate();
  };

  const onSaveBranding = (event: FormEvent) => {
    event.preventDefault();
    brandingMutation.mutate();
  };

  const entryPath = qrQuery.data?.subdomainSlug
    ? `/r/${qrQuery.data.subdomainSlug}`
    : settingsQuery.data?.subdomainSlug
      ? `/r/${settingsQuery.data.subdomainSlug}`
      : null;

  const loadError =
    settingsQuery.isError || brandingQuery.isError
      ? t("ownerSettings.loadError")
      : null;

  return (
    <section className="cq-page mx-auto max-w-2xl">
      <header className="cq-page-header">
        <h1 className="cq-page-title">{t("ownerSettings.title")}</h1>
        <p className="cq-page-subtitle">{t("ownerSettings.subtitle")}</p>
        {settingsQuery.data?.name ? (
          <p className="text-sm text-muted-foreground">
            {t("ownerSettings.clinicLabel", { name: settingsQuery.data.name })}
          </p>
        ) : null}
      </header>

      {actionError || loadError ? (
        <p className="cq-alert" role="alert">
          {actionError ?? loadError}
        </p>
      ) : null}
      {settingsSaved ? (
        <p className="cq-status" role="status">
          {t("ownerSettings.settingsSaved")}
        </p>
      ) : null}
      {brandingSaved ? (
        <p className="cq-status" role="status">
          {t("ownerSettings.brandingSaved")}
        </p>
      ) : null}

      {settingsQuery.isLoading || brandingQuery.isLoading ? (
        <p className="cq-status">{t("ownerSettings.loading")}</p>
      ) : null}

      <section className="cq-section" aria-labelledby="owner-ops-settings">
        <h2 id="owner-ops-settings" className="cq-section-title">
          {t("ownerSettings.opsTitle")}
        </h2>
        <p className="mb-3 text-sm text-muted-foreground">
          {t("ownerSettings.opsHint")}
        </p>
        <form onSubmit={onSaveSettings} className="flex flex-col gap-3">
          <label className="cq-label">
            <span>{t("ownerSettings.urgentRatio")}</span>
            <input
              type="number"
              min={1}
              max={10}
              required
              value={urgentRatio}
              onChange={(e) => setUrgentRatio(Number(e.target.value))}
              className="cq-field"
            />
            <span className="text-xs text-muted-foreground">
              {t("ownerSettings.urgentRatioHint")}
            </span>
          </label>
          <label className="cq-label">
            <span>{t("ownerSettings.headToClinicThreshold")}</span>
            <input
              type="number"
              min={1}
              max={50}
              required
              value={headToClinicThreshold}
              onChange={(e) =>
                setHeadToClinicThreshold(Number(e.target.value))
              }
              className="cq-field"
            />
            <span className="text-xs text-muted-foreground">
              {t("ownerSettings.headToClinicThresholdHint")}
            </span>
          </label>
          <label className="cq-label">
            <span>{t("ownerSettings.autoAbsenceTimeout")}</span>
            <input
              type="number"
              min={1}
              max={240}
              required
              value={autoAbsenceTimeoutMinutes}
              onChange={(e) =>
                setAutoAbsenceTimeoutMinutes(Number(e.target.value))
              }
              className="cq-field"
            />
          </label>
          <label className="cq-label">
            <span>{t("ownerSettings.repeatedAbsenceThreshold")}</span>
            <input
              type="number"
              min={1}
              max={20}
              required
              value={repeatedAbsenceThreshold}
              onChange={(e) =>
                setRepeatedAbsenceThreshold(Number(e.target.value))
              }
              className="cq-field"
            />
          </label>
          <label className="cq-label">
            <span>{t("ownerSettings.timezone")}</span>
            <input
              type="text"
              required
              value={timezone}
              onChange={(e) => setTimezone(e.target.value)}
              className="cq-field"
              placeholder="Africa/Cairo"
            />
          </label>
          <Button type="submit" disabled={settingsMutation.isPending}>
            {settingsMutation.isPending
              ? t("ownerSettings.saving")
              : t("ownerSettings.saveSettings")}
          </Button>
        </form>
      </section>

      <section className="cq-section" aria-labelledby="owner-branding">
        <h2 id="owner-branding" className="cq-section-title">
          {t("ownerSettings.brandingTitle")}
        </h2>
        <p className="mb-3 text-sm text-muted-foreground">
          {t("ownerSettings.brandingHint")}
        </p>
        <form onSubmit={onSaveBranding} className="flex flex-col gap-3">
          <label className="cq-label">
            <span>{t("ownerSettings.logoUrl")}</span>
            <input
              type="url"
              value={logoUrl}
              onChange={(e) => setLogoUrl(e.target.value)}
              className="cq-field"
            />
          </label>
          <label className="cq-label">
            <span>{t("ownerSettings.primaryColor")}</span>
            <input
              type="text"
              value={primaryColor}
              onChange={(e) => setPrimaryColor(e.target.value)}
              className="cq-field"
              placeholder="#1B3A6B"
            />
          </label>
          <label className="cq-label">
            <span>{t("ownerSettings.secondaryColor")}</span>
            <input
              type="text"
              value={secondaryColor}
              onChange={(e) => setSecondaryColor(e.target.value)}
              className="cq-field"
              placeholder="#5BB8C1"
            />
          </label>
          <Button type="submit" disabled={brandingMutation.isPending}>
            {brandingMutation.isPending
              ? t("ownerSettings.saving")
              : t("ownerSettings.saveBranding")}
          </Button>
        </form>
      </section>

      <section className="cq-section" aria-labelledby="owner-qr">
        <h2 id="owner-qr" className="cq-section-title">
          {t("ownerSettings.qrTitle")}
        </h2>
        <p className="mb-3 text-sm text-muted-foreground">
          {t("ownerSettings.qrHint")}
        </p>
        {qrQuery.isLoading ? (
          <p className="cq-status">{t("ownerSettings.loading")}</p>
        ) : entryPath ? (
          <div className="flex flex-col gap-2">
            <p className="cq-field-mono text-sm break-all">
              {typeof window !== "undefined"
                ? `${window.location.origin}${entryPath}`
                : entryPath}
            </p>
            {qrQuery.data?.qrCodeUrl ? (
              <a
                href={qrQuery.data.qrCodeUrl}
                target="_blank"
                rel="noreferrer"
                className="text-sm text-primary underline"
              >
                {t("ownerSettings.openQr")}
              </a>
            ) : (
              <p className="text-sm text-muted-foreground">
                {t("ownerSettings.qrLinkOnly")}
              </p>
            )}
          </div>
        ) : (
          <p className="cq-empty-friendly">{t("ownerSettings.qrUnavailable")}</p>
        )}
      </section>
    </section>
  );
}
