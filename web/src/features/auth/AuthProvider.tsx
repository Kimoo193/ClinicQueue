import {
  createContext,
  createElement,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useNavigate } from "react-router";

import { setUnauthorizedHandler } from "@/lib/api";

import {
  loginRequest,
  logoutRequest,
  meRequest,
  refreshRequest,
  registerRequest,
  switchClinicRequest,
} from "./api";
import {
  clinicName,
  distinctClinicIds,
  homePathForRoles,
  rolesInClinic,
} from "./roleHome";
import type {
  ActiveClinic,
  AuthUser,
  AuthUserResponse,
  ClinicMembership,
  LoginResult,
} from "./types";

export type AuthContextValue = {
  user: AuthUser | null;
  memberships: ClinicMembership[];
  activeClinic: ActiveClinic | null;
  isLoading: boolean;
  login: (
    emailOrPhone: string,
    password: string,
    clinicId?: string,
  ) => Promise<LoginResult>;
  register: (
    emailOrPhone: string,
    password: string,
    clinicId?: string,
  ) => Promise<void>;
  logout: () => Promise<void>;
  switchClinic: (clinicId: string) => Promise<string>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

/** Test-only provider that injects a fixed auth value (route-guard suites). */
export function AuthProviderForTest({
  value,
  children,
}: {
  value: AuthContextValue;
  children: ReactNode;
}) {
  return createElement(AuthContext.Provider, { value }, children);
}

function applyAuthResponse(
  data: AuthUserResponse,
  setUser: (u: AuthUser | null) => void,
  setMemberships: (m: ClinicMembership[]) => void,
  setActiveClinic: (c: ActiveClinic | null) => void,
): void {
  const memberships = data.memberships ?? [];
  setMemberships(memberships);
  setUser({ userId: data.userId, roles: data.roles });

  if (data.clinicId) {
    const clinicRoles = rolesInClinic(memberships, data.clinicId);
    const merged = Array.from(
      new Set([
        ...clinicRoles,
        ...data.roles.filter((r) => r === "SuperAdmin"),
      ]),
    );
    setActiveClinic({
      id: data.clinicId,
      name: clinicName(memberships, data.clinicId),
      roles: merged.length > 0 ? merged : data.roles,
    });
  } else {
    setActiveClinic(null);
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const [user, setUser] = useState<AuthUser | null>(null);
  const [memberships, setMemberships] = useState<ClinicMembership[]>([]);
  const [activeClinic, setActiveClinic] = useState<ActiveClinic | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const clearAuth = useCallback(() => {
    setUser(null);
    setMemberships([]);
    setActiveClinic(null);
  }, []);

  const handleUnauthorized = useCallback(() => {
    clearAuth();
    navigate("/login", { replace: true });
  }, [clearAuth, navigate]);

  useEffect(() => {
    setUnauthorizedHandler(handleUnauthorized);
    return () => setUnauthorizedHandler(null);
  }, [handleUnauthorized]);

  useEffect(() => {
    let cancelled = false;

    async function bootstrap(): Promise<void> {
      try {
        let me = await meRequest();
        if (!me) {
          const refreshed = await refreshRequest();
          if (refreshed) {
            me = await meRequest();
          }
        }
        if (cancelled) {
          return;
        }
        if (me) {
          applyAuthResponse(me, setUser, setMemberships, setActiveClinic);
        } else {
          clearAuth();
        }
      } catch {
        if (!cancelled) {
          clearAuth();
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    }

    void bootstrap();
    return () => {
      cancelled = true;
    };
  }, [clearAuth]);

  const login = useCallback(
    async (
      emailOrPhone: string,
      password: string,
      clinicId?: string,
    ): Promise<LoginResult> => {
      const data = await loginRequest(emailOrPhone, password, clinicId);
      applyAuthResponse(data, setUser, setMemberships, setActiveClinic);

      const clinics = distinctClinicIds(data.memberships ?? []);
      if (!data.clinicId && clinics.length > 1) {
        return { kind: "choose-clinic" };
      }
      if (!data.clinicId && clinics.length === 0) {
        // SuperAdmin with no clinic memberships, or empty account.
        if (data.roles.includes("SuperAdmin")) {
          return { kind: "ready", homePath: "/admin" };
        }
        return { kind: "find-clinic" };
      }

      return {
        kind: "ready",
        homePath: homePathForRoles(data.roles),
      };
    },
    [],
  );

  const register = useCallback(
    async (
      emailOrPhone: string,
      password: string,
      clinicId?: string,
    ): Promise<void> => {
      await registerRequest(emailOrPhone, password, clinicId);
    },
    [],
  );

  const logout = useCallback(async () => {
    try {
      await logoutRequest();
    } finally {
      clearAuth();
      navigate("/login", { replace: true });
    }
  }, [clearAuth, navigate]);

  const switchClinic = useCallback(async (clinicId: string) => {
    const data = await switchClinicRequest(clinicId);
    applyAuthResponse(data, setUser, setMemberships, setActiveClinic);
    return homePathForRoles(data.roles);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      memberships,
      activeClinic,
      isLoading,
      login,
      register,
      logout,
      switchClinic,
    }),
    [
      user,
      memberships,
      activeClinic,
      isLoading,
      login,
      register,
      logout,
      switchClinic,
    ],
  );

  return createElement(AuthContext.Provider, { value }, children);
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within AuthProvider");
  }
  return ctx;
}

/** Active clinic from JWT/session only — never from client UUID input. */
export function useActiveClinic(): ActiveClinic {
  const { activeClinic } = useAuth();
  if (!activeClinic) {
    throw new Error("No active clinic in session");
  }
  return activeClinic;
}
