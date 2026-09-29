// ARCHITECTURE_CYCLE24.md §454 — goods.ezbook.ru Web Push service worker (staff and customers of a shop).
//
// Copy of the ezbook worker (public/sw.js, ARCHITECTURE_CYCLE9.md §105.9) with goods defaults. Exactly two
// kinds of listeners besides install/activate, and NEVER a listener that intercepts network requests or any
// use of the browser's response-caching storage API: a worker without one physically cannot strand a browser on
// a stale build, which is what makes `skipWaiting()` + `clients.claim()` safe. CI greps this file for the
// disallowed tokens (.github/workflows/ci.yml, job `frontend`).
//
// Payload contract (API_CONTRACT_CYCLE24.md §486): { title, body, tag, url }.
// `url` is expected to be a relative path, but that is enforced by the worker, not assumed: safeUrl() below
// falls back to the default when the resolved origin differs from this one.
// No `pushsubscriptionchange` listener: the JWT lives in localStorage, a worker cannot reach it (§105.9);
// reconciliation happens in useWebPush on the next open.

function safeUrl(url, fallback) {
  try {
    const u = new URL(url, self.location.origin)
    return u.origin === self.location.origin ? u.pathname + u.search + u.hash : fallback
  } catch {
    return fallback
  }
}

self.addEventListener('install', () => {
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim())
})

self.addEventListener('push', (event) => {
  let data = { title: 'Новый заказ', body: '', tag: undefined, url: '/cabinet' }
  try {
    if (event.data) data = { ...data, ...event.data.json() }
  } catch {
    // Malformed payload: still show a bare notification — a push that shows nothing is punished by the browser.
  }

  event.waitUntil(
    self.registration.showNotification(data.title || 'Новый заказ', {
      body: data.body || '',
      // `tag` collapses repeats for the same order (`o-<orderId>`, `co-<orderId>`) into one notification.
      tag: data.tag,
      data: { url: safeUrl(data.url, '/cabinet') },
    }),
  )
})

// Clicking focuses an already-open tab instead of always opening a new one.
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const url = safeUrl(event.notification.data?.url, '/cabinet')

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
              // Unnavigable — the focused tab is still better than nothing.
            }
          }
          return
        }
      }
      await self.clients.openWindow(url)
    })(),
  )
})
