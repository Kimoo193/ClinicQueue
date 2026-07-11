import { Navigate, Outlet } from "react-router";

import { useAuth } from "@/features/auth/AuthProvider";
import { hasAnyRole } from "@/features/auth/roleHome";

type RequireRoleProps = {
  roles: readonly string[];
};

/** Nested under StaffLayout / PatientLayout — checks active clinic roles. */
export function RequireRole({ roles }: RequireRoleProps) {
  const { activeClinic, user } = useAuth();

  if (user?.roles.includes("SuperAdmin") && roles.includes("SuperAdmin")) {
    return <Outlet />;
  }

  if (!activeClinic || !hasAnyRole(activeClinic.roles, roles)) {
    return <Navigate to="/forbidden" replace />;
  }

  return <Outlet />;
}
