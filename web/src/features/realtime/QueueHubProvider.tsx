import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import {
  createContext,
  createElement,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";

import { useAuth } from "@/features/auth/AuthProvider";

export type QueueHubContextValue = {
  /** Live hub connection when authenticated with an active clinic; otherwise null. */
  connection: HubConnection | null;
};

const QueueHubContext = createContext<QueueHubContextValue>({
  connection: null,
});

async function stopHub(hub: HubConnection | null): Promise<void> {
  if (!hub) {
    return;
  }
  try {
    if (hub.state !== HubConnectionState.Disconnected) {
      await hub.stop();
    }
  } catch {
    // Best-effort teardown — session is already gone.
  }
}

/**
 * Owns the queue SignalR connection for the session.
 * Connects only when AuthProvider has a user + active clinic; stops on logout / 401
 * (auth clear) or when the active clinic is cleared. Cookies via withCredentials
 * (backend JwtBearer OnMessageReceived reads the access cookie for /hubs).
 */
export function QueueHubProvider({ children }: { children: ReactNode }) {
  const { user, activeClinic } = useAuth();
  const [connection, setConnection] = useState<HubConnection | null>(null);

  const userId = user?.userId ?? null;
  const clinicId = activeClinic?.id ?? null;

  useEffect(() => {
    if (!userId || !clinicId) {
      setConnection(null);
      return;
    }

    let disposed = false;
    const hub = new HubConnectionBuilder()
      .withUrl("/hubs/queue", { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    void (async () => {
      try {
        await hub.start();
        if (disposed) {
          await stopHub(hub);
          return;
        }
        await hub.invoke("JoinClinic", clinicId);
        if (disposed) {
          await stopHub(hub);
          return;
        }
        setConnection(hub);
      } catch (err: unknown) {
        console.warn("SignalR connect failed", err);
        if (!disposed) {
          setConnection(null);
        }
      }
    })();

    return () => {
      disposed = true;
      setConnection(null);
      void stopHub(hub);
    };
  }, [userId, clinicId]);

  return createElement(
    QueueHubContext.Provider,
    { value: { connection } },
    children,
  );
}

export function useQueueHub(): QueueHubContextValue {
  return useContext(QueueHubContext);
}
