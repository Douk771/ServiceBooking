// ARCHITECTURE_CYCLE9.md §105.9 — staff Web Push service worker.
//
// Exactly two event listeners, and NEVER a `fetch` listener or any use of `caches`/`CacheStorage`.
// That is not a style preference: a service worker without a `fetch` listener is PHYSICALLY unable to
// intercept a navigation or an asset request, which is what makes `skipWaiting()` + `clients.claim()`
// safe below (R11 — "SW breaks the next frontend deploy"). CI greps this exact file for those three
// tokens and fails the build if any of them shows up again (.github/workflows/ci.yml, job `frontend`).
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
  let data = { title: 'Новая запись', body: '', tag: undefined, url: '/my-bookings' }
  try {
    if (event.data) data = { ...data, ...event.data.json() }
  } catch {
    // Malformed payload: still show a bare notification rather than silently dropping the push —
    // the browser punishes a service worker that receives a push and shows nothing for it.
  }

  event.waitUntil(
    self.registration.showNotification(data.title || 'Новая запись', {
      body: data.body || '',
      tag: data.tag,
      data: { url: data.url || '/my-bookings' },
    }),
  )
})

// US-116 — clicking the notification focuses an already-open tab of the product instead of always
// opening a new one.
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const url = event.notification.data?.url || '/my-bookings'

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

// §105.9 — best-effort re-subscription safety net. Browser support for this event is uneven, so the
// primary path stays useWebPush's own reconciliation on cabinet open; this just covers the gap when
// that reconciliation hasn't run yet.
self.addEventListener('pushsubscriptionchange', (event) => {
  event.waitUntil(
    (async () => {
      try {
        const options = event.oldSubscription
          ? { applicationServerKey: event.oldSubscription.options.applicationServerKey, userVisibleOnly: true }
          : null
        if (!options) return
        const subscription = await self.registration.pushManager.subscribe(options)
        await fetch('/api/push/subscriptions', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            endpoint: subscription.endpoint,
            keys: {
              p256dh: arrayBufferToBase64Url(subscription.getKey('p256dh')),
              auth: arrayBufferToBase64Url(subscription.getKey('auth')),
            },
          }),
        })
      } catch {
        // Best-effort only — the app itself reconciles on next open (§105.9).
      }
    })(),
  )
})

function arrayBufferToBase64Url(buffer) {
  const bytes = new Uint8Array(buffer)
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}
