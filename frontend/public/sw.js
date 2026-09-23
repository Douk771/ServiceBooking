// ARCHITECTURE_CYCLE9.md §105.9 — staff Web Push service worker.
//
// Exactly two event listeners, and NEVER a listener that intercepts network requests or any use of
// the browser's response-caching storage API. That is not a style preference: a service worker without
// such a listener is PHYSICALLY unable to intercept a navigation or an asset request, which is what
// makes `skipWaiting()` + `clients.claim()` safe below (R11 — "SW breaks the next frontend deploy"). CI
// greps this exact file for the disallowed tokens and fails the build if any of them shows up again
// (.github/workflows/ci.yml, job `frontend`).
//
// Scope is `/` (Vite copies `public/` into `dist` verbatim, so this is served from `/sw.js`), no
// build-time hashing, no extra headers beyond `Cache-Control: no-cache` on the nginx side (deploy/nginx).

self.addEventListener('install', () => {
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim())
})

// API_CONTRACT_CYCLE9.md §115.6 — payload shape: { title, body, tag, url }. `url` is a relative path;
// this worker adds the origin itself, so the server can never smuggle an absolute cross-origin URL in.
self.addEventListener('push', (event) => {
  // §115.6 default: no bookingId is known yet, so we can only land the master on the bookings list,
  // not a specific booking — matches the shape the server sends (`/cabinet?tab=bookings&booking=<id>`)
  // rather than the unrelated client-facing `/my-bookings` route.
  let data = { title: 'Новая запись', body: '', tag: undefined, url: '/cabinet?tab=bookings' }
  try {
    if (event.data) data = { ...data, ...event.data.json() }
  } catch {
    // Malformed payload (not valid JSON): still show a bare notification rather than silently
    // dropping the push — the browser punishes a service worker that receives a push and shows
    // nothing for it. The fields above are the fallback shown in that case.
  }

  event.waitUntil(
    self.registration.showNotification(data.title || 'Новая запись', {
      body: data.body || '',
      // §115.6 — `tag` collapses repeat notifications for the same booking (`b-<bookingId>`) into one.
      tag: data.tag,
      data: { url: data.url || '/cabinet?tab=bookings' },
    }),
  )
})

// US-116 — clicking the notification focuses an already-open tab of the product instead of always
// opening a new one.
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const url = event.notification.data?.url || '/cabinet?tab=bookings'

  event.waitUntil(
    (async () => {
      const clientsList = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
      for (const client of clientsList) {
        if ('focus' in client) {
          await client.focus()
          if ('navigate' in client) {
            try {
              await client.navigate(url)
            } catch {
              // Cross-origin or otherwise unnavigable — the focused tab is still better than nothing.
            }
          }
          return
        }
      }
      await self.clients.openWindow(url)
    })(),
  )
})

// §105.9 — deliberately NO `pushsubscriptionchange` listener here. A previous version tried to
// re-subscribe and POST the new subscription straight from the service worker, but that can never
// succeed: the JWT lives in `authStore`/localStorage, which a service worker has no access to, and
// `POST /api/push/subscriptions` sits behind `[Authorize]` — every such call 401s and was silently
// swallowed by its own `catch`, i.e. dead code with a misleading "safety net" comment. The real
// reconciliation path is useWebPush's own effect, which runs with the app's normal authenticated
// `api` client on every cabinet open (rubezh 1, §105.9) — that is sufficient on its own. Revisit this
// only if/when the platform gains cookie-based auth that a service worker could actually use.
