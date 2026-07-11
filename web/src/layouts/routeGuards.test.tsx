import {
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  createMemoryRouter,
  Outlet,
  RouterProvider,
  useLocation,
  useNavigate,
} from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  AuthProviderForTest,
  type AuthContextValue,
} from "@/features/auth/AuthProvider";
import type {
  ActiveClinic,
  AuthUser,
  ClinicMembership,
} from "@/features/auth/types";
import { AppHeader } from "@/layouts/AppHeader";
import { AdminLayout } from "@/layouts/AdminLayout";
import { PatientLayout } from "@/layouts/PatientLayout";
import { RequireRole } from "@/layouts/RequireRole";
import { StaffLayout } from "@/layouts/StaffLayout";
import { apiFetch, setUnauthorizedHandler } from "@/lib/api";
import { ForbiddenPage } from "@/pages/ForbiddenPage";

vi.mock("@/features/realtime/QueueHubProvider", () => ({
  QueueHubProvider: ({ children }: { children: ReactNode }) => children,
  useQueueHub: () => ({ connection: null }),
}));

const clinicA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const clinicB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

function LocationProbe({ label }: { label: string }) {
  const location = useLocation();
  return (
    <div>
      <span data-testid="route-path">{location.pathname}</span>
      <span data-testid="page-label">{label}</span>
    </div>
  );
}

function stubAuth(
  overrides: Partial<AuthContextValue> & {
    user?: AuthUser | null;
    activeClinic?: ActiveClinic | null;
    memberships?: ClinicMembership[];
  },
): AuthContextValue {
  return {
    user: overrides.user ?? null,
    memberships: overrides.memberships ?? [],
    activeClinic: overrides.activeClinic ?? null,
    isLoading: overrides.isLoading ?? false,
    login: overrides.login ?? vi.fn(),
    register: overrides.register ?? vi.fn(),
    logout: overrides.logout ?? vi.fn(),
    switchClinic: overrides.switchClinic ?? vi.fn(),
  };
}

function renderGuarded(initialPath: string, auth: AuthContextValue) {
  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: (
          <AuthProviderForTest value={auth}>
            <Outlet />
          </AuthProviderForTest>
        ),
        children: [
          {
            path: "login",
            element: <LocationProbe label="login" />,
          },
          {
            path: "app",
            element: <PatientLayout />,
            children: [
              {
                path: "queue",
                element: <LocationProbe label="patient-queue" />,
              },
            ],
          },
          {
            path: "reception",
            element: <StaffLayout />,
            children: [
              {
                element: <RequireRole roles={["Receptionist"]} />,
                children: [
                  {
                    index: true,
                    element: <LocationProbe label="reception" />,
                  },
                ],
              },
            ],
          },
          {
            path: "doctor",
            element: <StaffLayout />,
            children: [
              {
                element: <RequireRole roles={["Doctor"]} />,
                children: [
                  { index: true, element: <LocationProbe label="doctor" /> },
                ],
              },
            ],
          },
          {
            path: "owner",
            element: <StaffLayout />,
            children: [
              {
                element: <RequireRole roles={["ClinicOwner"]} />,
                children: [
                  {
                    path: "reports",
                    element: <LocationProbe label="owner-reports" />,
                  },
                ],
              },
            ],
          },
          {
            path: "admin",
            element: <AdminLayout />,
            children: [
              { index: true, element: <LocationProbe label="admin" /> },
            ],
          },
          { path: "forbidden", element: <ForbiddenPage /> },
        ],
      },
    ],
    { initialEntries: [initialPath] },
  );

  return render(<RouterProvider router={router} />);
}

describe("route guards", () => {
  afterEach(() => {
    setUnauthorizedHandler(null);
    vi.restoreAllMocks();
  });

  it.each([
    "/reception",
    "/doctor",
    "/owner/reports",
    "/admin",
    "/app/queue",
  ])("unauthenticated user at %s is redirected to /login", async (path) => {
    renderGuarded(path, stubAuth({ user: null, activeClinic: null }));

    await waitFor(() => {
      expect(screen.getByTestId("route-path")).toHaveTextContent("/login");
    });
    expect(screen.getByTestId("page-label")).toHaveTextContent("login");
  });

  it("patient hitting /reception goes to /forbidden", async () => {
    renderGuarded(
      "/reception",
      stubAuth({
        user: { userId: "u1", roles: ["Patient"] },
        activeClinic: {
          id: clinicA,
          name: "Clinic A",
          roles: ["Patient"],
        },
      }),
    );

    await waitFor(() => {
      expect(
        screen.getByRole("heading", { name: /not authorized/i }),
      ).toBeInTheDocument();
    });
  });

  it("patient hitting /admin goes to /forbidden", async () => {
    renderGuarded(
      "/admin",
      stubAuth({
        user: { userId: "u1", roles: ["Patient"] },
        activeClinic: {
          id: clinicA,
          name: "Clinic A",
          roles: ["Patient"],
        },
      }),
    );

    await waitFor(() => {
      expect(
        screen.getByRole("heading", { name: /not authorized/i }),
      ).toBeInTheDocument();
    });
  });

  it("receptionist hitting /admin goes to /forbidden", async () => {
    renderGuarded(
      "/admin",
      stubAuth({
        user: { userId: "u2", roles: ["Receptionist"] },
        activeClinic: {
          id: clinicA,
          name: "Clinic A",
          roles: ["Receptionist"],
        },
      }),
    );

    await waitFor(() => {
      expect(
        screen.getByRole("heading", { name: /not authorized/i }),
      ).toBeInTheDocument();
    });
  });

  it("receptionist in A / patient in B: active B forbids reception; switchClinic(A) allows it", async () => {
    function DualClinicAuth({ children }: { children: ReactNode }) {
      const [activeClinic, setActiveClinic] = useState<ActiveClinic>({
        id: clinicB,
        name: "Clinic B",
        roles: ["Patient"],
      });
      const navigate = useNavigate();

      const value = useMemo<AuthContextValue>(
        () =>
          stubAuth({
            user: { userId: "u3", roles: ["Patient"] },
            memberships: [
              {
                clinicId: clinicA,
                clinicName: "Clinic A",
                role: "Receptionist",
              },
              {
                clinicId: clinicB,
                clinicName: "Clinic B",
                role: "Patient",
              },
            ],
            activeClinic,
            switchClinic: async (clinicId: string) => {
              if (clinicId === clinicA) {
                setActiveClinic({
                  id: clinicA,
                  name: "Clinic A",
                  roles: ["Receptionist"],
                });
                return "/reception";
              }
              setActiveClinic({
                id: clinicB,
                name: "Clinic B",
                roles: ["Patient"],
              });
              return "/app/queue";
            },
          }),
        [activeClinic],
      );

      return (
        <AuthProviderForTest value={value}>
          <button
            type="button"
            data-testid="do-switch-a"
            onClick={() => {
              void value.switchClinic(clinicA).then((home) => {
                navigate(home, { replace: true });
              });
            }}
          >
            switch-a
          </button>
          {children}
        </AuthProviderForTest>
      );
    }

    const router = createMemoryRouter(
      [
        {
          path: "/",
          element: (
            <DualClinicAuth>
              <Outlet />
            </DualClinicAuth>
          ),
          children: [
            {
              path: "reception",
              element: <StaffLayout />,
              children: [
                {
                  element: <RequireRole roles={["Receptionist"]} />,
                  children: [
                    {
                      index: true,
                      element: <LocationProbe label="reception" />,
                    },
                  ],
                },
              ],
            },
            { path: "forbidden", element: <ForbiddenPage /> },
            {
              path: "app/queue",
              element: <LocationProbe label="patient-queue" />,
            },
          ],
        },
      ],
      { initialEntries: ["/reception"] },
    );

    const user = userEvent.setup();
    render(<RouterProvider router={router} />);

    await waitFor(() => {
      expect(
        screen.getByRole("heading", { name: /not authorized/i }),
      ).toBeInTheDocument();
    });

    await user.click(screen.getByTestId("do-switch-a"));

    await waitFor(() => {
      expect(screen.getByTestId("page-label")).toHaveTextContent("reception");
    });
    expect(screen.getByTestId("route-path")).toHaveTextContent("/reception");
  });

  it("401 from any API call clears auth state and lands on /login", async () => {
    function SessionAuth({ children }: { children: ReactNode }) {
      const navigate = useNavigate();
      const [user, setUser] = useState<AuthUser | null>({
        userId: "u1",
        roles: ["Patient"],
      });
      const [activeClinic, setActiveClinic] = useState<ActiveClinic | null>({
        id: clinicA,
        name: "Clinic A",
        roles: ["Patient"],
      });

      useEffect(() => {
        setUnauthorizedHandler(() => {
          setUser(null);
          setActiveClinic(null);
          navigate("/login", { replace: true });
        });
        return () => setUnauthorizedHandler(null);
      }, [navigate]);

      const value = useMemo(
        () =>
          stubAuth({
            user,
            activeClinic,
            memberships: [],
          }),
        [user, activeClinic],
      );

      return (
        <AuthProviderForTest value={value}>{children}</AuthProviderForTest>
      );
    }

    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(null, { status: 401, statusText: "Unauthorized" }),
    );

    const router = createMemoryRouter(
      [
        {
          path: "/",
          element: (
            <SessionAuth>
              <Outlet />
            </SessionAuth>
          ),
          children: [
            {
              index: true,
              element: (
                <button
                  type="button"
                  onClick={() => {
                    void apiFetch("/api/v1/secure");
                  }}
                >
                  hit-api
                </button>
              ),
            },
            {
              path: "login",
              element: <LocationProbe label="login" />,
            },
          ],
        },
      ],
      { initialEntries: ["/"] },
    );

    const user = userEvent.setup();
    render(<RouterProvider router={router} />);

    expect(screen.getByRole("button", { name: "hit-api" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "hit-api" }));

    await waitFor(() => {
      expect(screen.getByTestId("route-path")).toHaveTextContent("/login");
    });
    expect(screen.getByTestId("page-label")).toHaveTextContent("login");
  });

  it("nav renders only routes permitted for the active clinic role", () => {
    function renderNav(
      roles: readonly string[],
      auth: AuthContextValue,
    ): void {
      const router = createMemoryRouter(
        [
          {
            path: "/",
            element: (
              <AuthProviderForTest value={auth}>
                <AppHeader navRoles={roles} />
              </AuthProviderForTest>
            ),
          },
        ],
        { initialEntries: ["/"] },
      );
      render(<RouterProvider router={router} />);
    }

    renderNav(
      ["Patient"],
      stubAuth({
        user: { userId: "u1", roles: ["Patient"] },
        activeClinic: {
          id: clinicB,
          name: "Clinic B",
          roles: ["Patient"],
        },
        memberships: [
          {
            clinicId: clinicA,
            clinicName: "Clinic A",
            role: "Receptionist",
          },
          {
            clinicId: clinicB,
            clinicName: "Clinic B",
            role: "Patient",
          },
        ],
      }),
    );

    expect(screen.getByRole("link", { name: /patient/i })).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /^reception$/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /admin/i })).not.toBeInTheDocument();

    cleanup();

    renderNav(
      ["Receptionist"],
      stubAuth({
        user: { userId: "u1", roles: ["Receptionist"] },
        activeClinic: {
          id: clinicA,
          name: "Clinic A",
          roles: ["Receptionist"],
        },
        memberships: [
          {
            clinicId: clinicA,
            clinicName: "Clinic A",
            role: "Receptionist",
          },
          {
            clinicId: clinicB,
            clinicName: "Clinic B",
            role: "Patient",
          },
        ],
      }),
    );

    expect(
      screen.getByRole("link", { name: /^reception$/i }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /day close/i })).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /^patient$/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /admin/i })).not.toBeInTheDocument();
  });
});

