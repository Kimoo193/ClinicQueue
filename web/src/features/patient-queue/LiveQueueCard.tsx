import { useTranslation } from "react-i18next";

import { cn } from "@/lib/utils";

import type {
  AppointmentTypeBadge,
  LiveQueueViewModel,
  QueueVisualState,
} from "./liveQueueTypes";

type LiveQueueCardProps = {
  model: LiveQueueViewModel;
  className?: string;
};

const STATUS_I18N: Record<QueueVisualState, string> = {
  waitHome: "patientQueue.instruction.waitHome",
  headToClinic: "patientQueue.instruction.headToClinic",
  yourTurn: "patientQueue.instruction.yourTurn",
  paused: "patientQueue.instruction.paused",
  pending: "patientQueue.instruction.pending",
};

const STATUS_DETAIL_I18N: Partial<Record<QueueVisualState, string>> = {
  waitHome: "patientQueue.instructionDetail.waitHome",
  headToClinic: "patientQueue.instructionDetail.headToClinic",
  yourTurn: "patientQueue.instructionDetail.yourTurn",
  paused: "patientQueue.instructionDetail.paused",
};

export function LiveQueueCard({ model, className }: LiveQueueCardProps) {
  const { t } = useTranslation();
  const isYourTurn = model.visualState === "yourTurn";
  const showCount =
    model.countAhead !== null &&
    model.visualState !== "yourTurn" &&
    model.visualState !== "pending";

  const numberLabel =
    model.numberKind === "real"
      ? t("patientQueue.realNumberLabel")
      : t("patientQueue.tempNumberLabel");

  return (
    <article
      className={cn("cq-queue-hero", className)}
      data-state={model.visualState}
      aria-labelledby={`queue-number-${model.appointmentId}`}
      aria-live={isYourTurn ? "assertive" : "polite"}
      aria-atomic="true"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <LiveIndicator
          isLive={model.isLive}
          inverted={isYourTurn}
          liveLabel={t("patientQueue.live")}
          offlineLabel={t("patientQueue.offline")}
        />
        <TypeBadge type={model.appointmentType} />
      </div>

      <div className="mt-8 flex flex-col items-center text-center sm:mt-10">
        <p
          className={cn(
            "text-sm font-medium tracking-wide",
            isYourTurn
              ? "text-primary-foreground/80"
              : "text-muted-foreground",
          )}
        >
          {numberLabel}
        </p>
        <p
          id={`queue-number-${model.appointmentId}`}
          className="cq-queue-number mt-2"
          aria-label={t("patientQueue.numberAria", {
            number: model.displayNumber,
            kind: numberLabel,
          })}
        >
          {model.displayNumber}
        </p>

        {showCount ? (
          <div
            className="mt-6 flex flex-col items-center gap-1"
            aria-label={t("patientQueue.countAhead", {
              count: model.countAhead,
            })}
          >
            <span
              className={cn(
                "cq-count-ahead",
                model.visualState === "paused" && "text-warning-foreground",
                model.visualState === "headToClinic" && "text-warning-foreground",
              )}
              aria-hidden
            >
              {model.countAhead}
            </span>
            <span
              className={cn(
                "text-sm font-medium sm:text-base",
                isYourTurn
                  ? "text-primary-foreground/85"
                  : "text-muted-foreground",
              )}
              aria-hidden
            >
              {t("patientQueue.aheadCaption", { count: model.countAhead })}
            </span>
          </div>
        ) : null}
      </div>

      <div
        className="cq-queue-status"
        data-state={model.visualState}
        role="status"
      >
        <p>{t(STATUS_I18N[model.visualState])}</p>
        {STATUS_DETAIL_I18N[model.visualState] ? (
          <p
            className={cn(
              "mt-1 text-sm font-normal",
              isYourTurn
                ? "text-primary-foreground/85"
                : "opacity-90",
            )}
          >
            {t(STATUS_DETAIL_I18N[model.visualState]!)}
          </p>
        ) : null}
      </div>
    </article>
  );
}

function LiveIndicator({
  isLive,
  inverted,
  liveLabel,
  offlineLabel,
}: {
  isLive: boolean;
  inverted?: boolean;
  liveLabel: string;
  offlineLabel: string;
}) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-2 text-xs font-medium",
        inverted
          ? "text-primary-foreground/90"
          : isLive
            ? "text-info-foreground"
            : "text-muted-foreground",
      )}
      aria-label={isLive ? liveLabel : offlineLabel}
    >
      <span
        className={cn(
          isLive
            ? inverted
              ? "cq-live-dot cq-live-dot--on-dark"
              : "cq-live-dot"
            : "inline-flex size-2.5 rounded-full bg-muted-foreground/40",
        )}
        aria-hidden
      />
      {isLive ? liveLabel : offlineLabel}
    </span>
  );
}

function TypeBadge({ type }: { type: AppointmentTypeBadge }) {
  const { t } = useTranslation();
  return (
    <span className="cq-type-badge" data-type={type}>
      {type === "Urgent"
        ? t("patientQueue.type.urgent")
        : t("patientQueue.type.normal")}
    </span>
  );
}

export function LiveQueueSkeleton() {
  const { t } = useTranslation();
  return (
    <div
      className="cq-queue-skeleton"
      role="status"
      aria-label={t("patientQueue.loading")}
    >
      <div className="flex justify-between">
        <div className="h-4 w-16 rounded bg-muted" />
        <div className="h-6 w-20 rounded bg-muted" />
      </div>
      <div className="mt-8 flex flex-col items-center gap-3">
        <div className="h-4 w-24 rounded bg-muted" />
        <div className="h-24 w-36 rounded-xl bg-muted sm:h-28" />
        <div className="h-5 w-40 rounded bg-muted" />
      </div>
      <div className="mt-6 h-16 w-full rounded-xl bg-muted" />
      <span className="sr-only">{t("patientQueue.loading")}</span>
    </div>
  );
}

export function LiveQueueEmpty() {
  const { t } = useTranslation();
  return (
    <div
      className="rounded-2xl border border-dashed border-border bg-surface px-6 py-12 text-center"
      role="status"
    >
      <p className="text-lg font-semibold text-foreground">
        {t("patientQueue.emptyTitle")}
      </p>
      <p className="mt-2 text-sm text-muted-foreground">
        {t("patientQueue.empty")}
      </p>
    </div>
  );
}

export function LiveQueueError({ onRetry }: { onRetry: () => void }) {
  const { t } = useTranslation();
  return (
    <div className="cq-alert rounded-2xl p-5" role="alert">
      <p className="font-semibold">{t("patientQueue.errorTitle")}</p>
      <p className="mt-1 text-sm opacity-90">{t("patientQueue.error")}</p>
      <button
        type="button"
        className="mt-4 min-h-11 rounded-lg bg-primary px-4 text-sm font-medium text-primary-foreground focus-visible:ring-3 focus-visible:ring-ring/40 focus-visible:outline-none"
        onClick={onRetry}
      >
        {t("patientQueue.retry")}
      </button>
    </div>
  );
}
