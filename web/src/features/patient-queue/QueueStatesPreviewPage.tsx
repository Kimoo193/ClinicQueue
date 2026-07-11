import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";

import { applyDocumentDirection } from "@/i18n";

import {
  LiveQueueCard,
  LiveQueueEmpty,
  LiveQueueError,
  LiveQueueSkeleton,
} from "./LiveQueueCard";
import type { LiveQueueViewModel, QueueVisualState } from "./liveQueueTypes";

/**
 * Dev-only gallery of patient queue visual states for design review / screenshots.
 * Not linked from production navigation.
 */
const STATES: Array<{
  id: string;
  state: QueueVisualState;
  countAhead: number | null;
  number: number;
  kind: "temporary" | "real";
  type: "Normal" | "Urgent";
  live?: boolean;
}> = [
  {
    id: "wait",
    state: "waitHome",
    countAhead: 7,
    number: 42,
    kind: "temporary",
    type: "Normal",
  },
  {
    id: "go",
    state: "headToClinic",
    countAhead: 3,
    number: 18,
    kind: "real",
    type: "Normal",
  },
  {
    id: "turn",
    state: "yourTurn",
    countAhead: 0,
    number: 12,
    kind: "real",
    type: "Urgent",
  },
  {
    id: "pause",
    state: "paused",
    countAhead: 5,
    number: 27,
    kind: "real",
    type: "Normal",
  },
];

function toModel(
  row: (typeof STATES)[number],
): LiveQueueViewModel {
  return {
    appointmentId: row.id,
    displayNumber: row.number,
    numberKind: row.kind,
    countAhead: row.countAhead,
    visualState: row.state,
    appointmentType: row.type,
    isLive: row.live ?? true,
  };
}

export function QueueStatesPreviewPage() {
  const { t, i18n } = useTranslation();

  useEffect(() => {
    applyDocumentDirection(i18n.language);
  }, [i18n.language]);

  const toggleLanguage = () => {
    const next = i18n.language.startsWith("ar") ? "en" : "ar";
    void i18n.changeLanguage(next);
  };

  return (
    <main className="cq-app-shell min-h-screen">
      <div className="cq-app-frame max-w-lg gap-8 py-8">
        <header className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <p className="cq-brand-mark">{t("app.name")}</p>
            <h1 className="mt-1 text-lg font-semibold text-foreground">
              {t("patientQueue.previewTitle")}
            </h1>
          </div>
          <div className="flex gap-2">
            <button
              type="button"
              className="rounded-lg border border-border bg-surface px-3 py-2 text-sm focus-visible:ring-3 focus-visible:ring-ring/40 focus-visible:outline-none"
              onClick={toggleLanguage}
            >
              {i18n.language.startsWith("ar")
                ? t("language.en")
                : t("language.ar")}
            </button>
            <Link
              to="/"
              className="rounded-lg border border-border bg-surface px-3 py-2 text-sm focus-visible:ring-3 focus-visible:ring-ring/40 focus-visible:outline-none"
            >
              {t("auth.backHome")}
            </Link>
          </div>
        </header>

        {STATES.map((row) => (
          <section key={row.id} className="flex flex-col gap-2">
            <h2 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
              {row.state}
            </h2>
            <div data-preview-state={row.state}>
              <LiveQueueCard model={toModel(row)} />
            </div>
          </section>
        ))}

        <section className="flex flex-col gap-2">
          <h2 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
            loading
          </h2>
          <div data-preview-state="loading">
            <LiveQueueSkeleton />
          </div>
        </section>

        <section className="flex flex-col gap-2">
          <h2 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
            empty
          </h2>
          <div data-preview-state="empty">
            <LiveQueueEmpty />
          </div>
        </section>

        <section className="flex flex-col gap-2">
          <h2 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
            error
          </h2>
          <div data-preview-state="error">
            <LiveQueueError onRetry={() => undefined} />
          </div>
        </section>
      </div>
    </main>
  );
}
