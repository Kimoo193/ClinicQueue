import { useEffect } from "react";
import { Link, NavLink, useNavigate } from "react-router";
import { useTranslation } from "react-i18next";

import { Button, buttonVariants } from "@/components/ui/button";
import { useAuth } from "@/features/auth/AuthProvider";
import {
  clinicName,
  distinctClinicIds,
  navItemsForRoles,
} from "@/features/auth/roleHome";
import { applyDocumentDirection } from "@/i18n";
import { cn } from "@/lib/utils";

type AppHeaderProps = {
  /** Roles used for nav filtering (active clinic roles, or SuperAdmin-only). */
  navRoles: readonly string[];
};

export function AppHeader({ navRoles }: AppHeaderProps) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { memberships, activeClinic, switchClinic, logout, user } = useAuth();

  useEffect(() => {
    applyDocumentDirection(i18n.language);
  }, [i18n.language]);

  const toggleLanguage = () => {
    const next = i18n.language.startsWith("ar") ? "en" : "ar";
    void i18n.changeLanguage(next);
  };

  const items = navItemsForRoles(navRoles);
  const clinicIds = distinctClinicIds(memberships);
  const showSwitcher = clinicIds.length > 1;

  const onClinicChange = async (clinicId: string) => {
    if (!clinicId || clinicId === activeClinic?.id) {
      return;
    }
    const homePath = await switchClinic(clinicId);
    navigate(homePath, { replace: true });
  };

  return (
    <header className="flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0 space-y-1">
        <Link to="/" className="cq-brand-mark inline-block">
          {t("app.name")}
        </Link>
        <p className="text-sm text-muted-foreground">
          {activeClinic
            ? t("app.activeClinic", { name: activeClinic.name })
            : t("app.tagline")}
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        {showSwitcher ? (
          <label className="cq-label !mb-0 flex-row items-center gap-2">
            <span className="sr-only">{t("auth.clinicSwitcher")}</span>
            <select
              className="cq-field max-w-[12rem]"
              value={activeClinic?.id ?? ""}
              aria-label={t("auth.clinicSwitcher")}
              onChange={(e) => void onClinicChange(e.target.value)}
            >
              {clinicIds.map((id) => (
                <option key={id} value={id}>
                  {clinicName(memberships, id)}
                </option>
              ))}
            </select>
          </label>
        ) : null}

        <Button
          type="button"
          variant="outline"
          onClick={toggleLanguage}
          aria-label={t("language.toggle")}
          className="min-h-11 sm:min-h-9"
        >
          {i18n.language.startsWith("ar") ? t("language.en") : t("language.ar")}
        </Button>

        {user ? (
          <Button
            type="button"
            variant="ghost"
            onClick={() => void logout()}
            className="min-h-11 sm:min-h-9"
          >
            {t("auth.logout")}
          </Button>
        ) : null}
      </div>

      {items.length > 0 ? (
        <nav className="cq-nav w-full" aria-label={t("a11y.primaryNav")}>
          {items.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) =>
                cn(
                  buttonVariants({
                    size: "sm",
                    variant: isActive ? "default" : "ghost",
                  }),
                  // Comfortable touch target on phones; compact on desktop.
                  "min-h-11 sm:min-h-8",
                )
              }
            >
              {t(item.labelKey)}
            </NavLink>
          ))}
        </nav>
      ) : null}
    </header>
  );
}
