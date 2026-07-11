import { Link } from "react-router";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";

export function ForbiddenPage() {
  const { t } = useTranslation();

  return (
    <main id="main-content" className="cq-app-shell">
      <div className="cq-app-frame">
        <section className="cq-page mx-auto max-w-md">
          <header className="cq-page-header">
            <h1 className="cq-page-title">{t("auth.forbiddenTitle")}</h1>
            <p className="cq-page-subtitle">{t("auth.forbiddenSubtitle")}</p>
          </header>
          <Button asChild>
            <Link to="/">{t("auth.backHome")}</Link>
          </Button>
        </section>
      </div>
    </main>
  );
}
