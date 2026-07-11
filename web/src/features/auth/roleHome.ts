import type { ClinicMembership } from "./types";

/** Prefer the most operational staff role, then patient, then admin. */
export function homePathForRoles(roles: readonly string[]): string {
  if (roles.includes("SuperAdmin")) {
    return "/admin";
  }
  if (roles.includes("Receptionist")) {
    return "/reception";
  }
  if (roles.includes("Doctor")) {
    return "/doctor";
  }
  if (roles.includes("ClinicOwner")) {
    return "/owner/reports";
  }
  if (roles.includes("Patient")) {
    return "/app/queue";
  }
  return "/";
}

export function rolesInClinic(
  memberships: readonly ClinicMembership[],
  clinicId: string,
): string[] {
  return memberships
    .filter((m) => m.clinicId.toLowerCase() === clinicId.toLowerCase())
    .map((m) => m.role);
}

export function clinicName(
  memberships: readonly ClinicMembership[],
  clinicId: string,
): string {
  const match = memberships.find(
    (m) => m.clinicId.toLowerCase() === clinicId.toLowerCase(),
  );
  return match?.clinicName ?? clinicId;
}

export function distinctClinicIds(
  memberships: readonly ClinicMembership[],
): string[] {
  const seen = new Set<string>();
  const ids: string[] = [];
  for (const m of memberships) {
    const key = m.clinicId.toLowerCase();
    if (!seen.has(key)) {
      seen.add(key);
      ids.push(m.clinicId);
    }
  }
  return ids;
}

export type NavItem = {
  to: string;
  labelKey: string;
};

export function navItemsForRoles(roles: readonly string[]): NavItem[] {
  const items: NavItem[] = [];
  if (roles.includes("Patient")) {
    items.push(
      { to: "/app/queue", labelKey: "nav.patient" },
      { to: "/app/bookings", labelKey: "nav.bookings" },
      { to: "/app/medical", labelKey: "nav.medical" },
    );
  }
  if (roles.includes("Receptionist")) {
    items.push(
      { to: "/reception", labelKey: "nav.reception" },
      { to: "/reception/day-close", labelKey: "nav.dayclose" },
    );
  }
  if (roles.includes("Doctor")) {
    items.push({ to: "/doctor", labelKey: "nav.doctor" });
  }
  if (roles.includes("ClinicOwner")) {
    items.push({ to: "/owner/reports", labelKey: "nav.owner" });
  }
  if (roles.includes("SuperAdmin")) {
    items.push({ to: "/admin", labelKey: "nav.admin" });
  }
  return items;
}

export function hasAnyRole(
  roles: readonly string[],
  required: readonly string[],
): boolean {
  return required.some((r) => roles.includes(r));
}
