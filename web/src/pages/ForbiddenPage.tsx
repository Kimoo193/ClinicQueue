import { Link, useNavigate } from "react-router";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/ui/button";
import { useAuth } from "@/features/auth/AuthProvider";
import {
  clinicName,
  distinctClinicIds,
  homePathForRoles,
} from "@/features/auth/roleHome";

export function ForbiddenPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { user, memberships, activeClinic, switchClinic, logout } = useAuth();

  const clinicIds = distinctClinicIds(memberships);
  const showSwitcher = clinicIds.length > 1;

  const onSwitchClinic = async (clinicId: string) => {
    const homePath = await switchClinic(clinicId);
    navigate(homePath || homePathForRoles(activeClinic?.roles ?? []), {
      replace: true,
    });
  };

  const homeTarget = activeClinic
    ? homePathForRoles(activeClinic.roles)
    : "/";

  return (
    <main id="main-content" className="cq-app-shell">
      <div className="cq-app-frame">
        <section className="cq-page mx-auto max-w-md">
          <header className="cq-page-header">
            <p className="cq-brand-mark">{t("app.name")}</p>
            <h1 className="cq-page-title">{t("auth.forbiddenTitle")}</h1>
            <p className="cq-page-subtitle">{t("auth.forbiddenSubtitle")}</p>
          </header>

          <p className="cq-callout-info mb-6" role="status">
            {t("auth.forbiddenHelp")}
          </p>

          <div className="flex flex-col gap-3">
            <Button asChild>
              <Link to={homeTarget}>{t("auth.goHomeRole")}</Link>
            </Button>
            <Button asChild variant="outline">
              <Link to="/">{t("auth.backHome")}</Link>
            </Button>

            {showSwitcher ? (
              <div className="cq-section gap-2">
                <h2 className="cq-section-title">
                  {t("auth.forbiddenSwitchTitle")}
                </h2>
                <ul className="flex flex-col gap-2">
                  {clinicIds.map((id) => (
                    <li key={id}>
                      <button
                        type="button"
                        className="cq-clinic-pick"
                        onClick={() => void onSwitchClinic(id)}
                      >
                        <span className="font-semibold">
                          {clinicName(memberships, id) ||
                            t("auth.unnamedClinic")}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}

            {user ? (
              <Button
                type="button"
                variant="ghost"
                onClick={() => void logout()}
              >
                {t("auth.logout")}
              </Button>
            ) : (
              <Button asChild variant="ghost">
                <Link to="/login">{t("auth.login")}</Link>
              </Button>
            )}
          </div>
        </section>
      </div>
    </main>
  );
}
