/**
 * Shared UTC → clinic-timezone display formatter (constitution: convert at presentation layer).
 */

export function formatInClinicTimezone(
  utcIso: string | Date,
  timeZone: string,
  locale = "ar",
  options: Intl.DateTimeFormatOptions = {
    dateStyle: "medium",
    timeStyle: "short",
  },
): string {
  const date = typeof utcIso === "string" ? new Date(utcIso) : utcIso;
  if (Number.isNaN(date.getTime())) {
    return "";
  }

  return new Intl.DateTimeFormat(locale, { ...options, timeZone }).format(date);
}

export function todayInClinicTimezone(timeZone: string): string {
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(new Date());

  const year = parts.find((p) => p.type === "year")?.value ?? "1970";
  const month = parts.find((p) => p.type === "month")?.value ?? "01";
  const day = parts.find((p) => p.type === "day")?.value ?? "01";
  return `${year}-${month}-${day}`;
}
