/* ClinicQueue Web Push service worker (FR-075) */
self.addEventListener("push", (event) => {
  const pushEvent = /** @type {PushEvent} */ (event);
  let title = "ClinicQueue";
  let body = "";

  try {
    const data = pushEvent.data ? pushEvent.data.json() : {};
    title = data.title || title;
    body = data.body || data.payloadJson || "";
  } catch {
    body = pushEvent.data ? pushEvent.data.text() : "";
  }

  pushEvent.waitUntil(
    self.registration.showNotification(title, {
      body,
      icon: "/favicon.svg",
      badge: "/favicon.svg",
    }),
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  event.waitUntil(clients.openWindow("/"));
});
