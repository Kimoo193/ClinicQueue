import { persistPushSubscription } from "@/features/patient-queue/api";

const VAPID_PUBLIC_KEY = import.meta.env.VITE_VAPID_PUBLIC_KEY as string | undefined;

function urlBase64ToUint8Array(base64String: string): Uint8Array {
  const padding = "=".repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, "+").replace(/_/g, "/");
  const rawData = window.atob(base64);
  const output = new Uint8Array(rawData.length);
  for (let i = 0; i < rawData.length; i += 1) {
    output[i] = rawData.charCodeAt(i);
  }
  return output;
}

export async function registerPushServiceWorker(): Promise<ServiceWorkerRegistration | null> {
  if (!("serviceWorker" in navigator) || !("PushManager" in window)) {
    return null;
  }

  return navigator.serviceWorker.register("/sw.js", { scope: "/" });
}

export async function subscribeToPush(
  registration: ServiceWorkerRegistration,
): Promise<PushSubscription | null> {
  if (!VAPID_PUBLIC_KEY) {
    console.warn("VITE_VAPID_PUBLIC_KEY not set; push subscription skipped");
    return null;
  }

  // Only prompt when permission is still undecided — avoid nagging on every page load.
  if (Notification.permission === "denied") {
    return null;
  }
  if (Notification.permission === "default") {
    const permission = await Notification.requestPermission();
    if (permission !== "granted") {
      return null;
    }
  }

  const existing = await registration.pushManager.getSubscription();
  if (existing) {
    return existing;
  }

  return registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: urlBase64ToUint8Array(VAPID_PUBLIC_KEY) as BufferSource,
  });
}

function extractKeys(subscription: PushSubscription): { p256dh: string; auth: string } | null {
  const json = subscription.toJSON();
  const p256dh = json.keys?.p256dh;
  const auth = json.keys?.auth;
  if (!p256dh || !auth || !json.endpoint) {
    return null;
  }
  return { p256dh, auth };
}

/**
 * Subscribe in the browser and POST the subscription to the API so outbox Web Push can find it.
 */
export async function ensurePushSubscription(clinicId: string): Promise<PushSubscription | null> {
  const registration = await registerPushServiceWorker();
  if (!registration) {
    return null;
  }
  await navigator.serviceWorker.ready;
  const subscription = await subscribeToPush(registration);
  if (!subscription) {
    return null;
  }

  const keys = extractKeys(subscription);
  if (!keys) {
    return subscription;
  }

  try {
    await persistPushSubscription({
      clinicId,
      endpoint: subscription.endpoint,
      p256dh: keys.p256dh,
      auth: keys.auth,
    });
  } catch (err) {
    console.warn("Failed to persist push subscription", err);
  }

  return subscription;
}
