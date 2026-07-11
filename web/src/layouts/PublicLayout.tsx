import { useEffect } from "react";
import { Link, Outlet } from "react-router";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { applyDocumentDirection } from "@/i18n";

/** Shell for public routes (landing, login, register, QR entry). */
export function PublicLayout() {
  const { t, i18n } = useTranslation();

  useEffect(() => {
    applyDocumentDirection(i18n.language);
  }, [i18n.language]);

  const toggleLanguage = () => {
    const next = i18n.language.startsWith("ar") ? "en" : "ar";
    void i18n.changeLanguage(next);
  };

  return (
    <main id="main-content" className="cq-app-shell">
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:absolute focus:start-4 focus:top-4 focus:z-50 focus:rounded-md focus:border focus:border-border focus:bg-card focus:px-3 focus:py-2 focus:shadow-sm"
      >
        {t("a11y.skipToContent")}
      </a>
      <div className="cq-app-frame">
        <header className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0 space-y-1">
            <Link to="/" className="cq-brand-mark inline-block">
              {t("app.name")}
            </Link>
            <p className="text-sm text-muted-foreground">{t("app.tagline")}</p>
          </div>
          <Button
            type="button"
            variant="outline"
            onClick={toggleLanguage}
            aria-label={t("language.toggle")}
          >
            {i18n.language.startsWith("ar")
              ? t("language.en")
              : t("language.ar")}
          </Button>
        </header>
        <Outlet />
      </div>
    </main>
  );
}
