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

// ARCHITECTURE_CYCLE33.md §33.5.2 — closed list of two origins: this site and its sibling (`/sw.js?peer=<origin>`, set by
// the page from the server's PublicSites config, API_CONTRACT_CYCLE33.md §33.28). The same block is in both workers.
const PEER_ORIGIN = readPeerOrigin()

function readPeerOrigin() {
  try {
    const raw = new URL(self.location.href).searchParams.get('peer')
    if (!raw) return null
    const u = new URL(raw)
    // Exact origin only: no path, query, fragment or trailing slash.
    if (raw !== u.origin || u.origin === self.location.origin) return null
    if (u.protocol === 'https:' || (u.protocol === 'http:' && self.location.protocol === 'http:')) return u.origin
  } catch {
    // Not a URL → no sibling.
  }
  return null
}

// → { kind: 'same', path } | { kind: 'peer', href }. Anything outside the two origins falls back to the default page.
function resolveTarget(url, fallbackPath) {
  if (typeof url !== 'string' || !url) return { kind: 'same', path: fallbackPath }
  try {
    const u = new URL(url, self.location.origin)
    if (u.origin === self.location.origin) return { kind: 'same', path: u.pathname + u.search + u.hash }
    if (PEER_ORIGIN && u.origin === PEER_ORIGIN) return { kind: 'peer', href: u.href }
  } catch {
    // Unparseable → default page.
  }
  return { kind: 'same', path: fallbackPath }
}

function targetToUrl(target) {
  return target.kind === 'peer' ? target.href : target.path
}

self.addEventListener('install', () => {
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim())
})

// API_CONTRACT_CYCLE9.md §115.6 — payload shape: { title, body, tag, url }.
// `url` is expected to be a relative path, but that is enforced by the worker, not assumed: resolveTarget() below
// falls back to the default when the resolved origin differs from this one.
self.addEventListener('push', (event) => {
  // §115.6 default: no bookingId is known yet, so we can only land the master on the bookings list.
  // `/my-bookings` (ProtectedRoute roles Master/CompanyOwner/SuperAdmin, frontend/src/App.tsx) is the
  // master's own bookings screen — not to be confused with `/my-visits`, the client-facing one. There
  // is no `bookings` tab on `/cabinet` (CabinetPage's tabs are dashboard/companies/schedule/clients/
  // reports/mailing/notifications, and it does not read the query string), so a `/cabinet?tab=bookings`
  // URL would silently strand the master on whatever the default tab is.
  let data = { title: 'Новая запись', body: '', tag: undefined, url: '/my-bookings' }
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
      data: { url: targetToUrl(resolveTarget(data.url, '/my-bookings')) },
    }),
  )
})

// US-116 — clicking the notification focuses an already-open tab of the product instead of always
// opening a new one.
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  // Resolved again: the data may have been stored by a previous version of this worker.
  const target = resolveTarget(event.notification.data?.url, '/my-bookings')

  event.waitUntil(
    (async () => {
      // The sibling's tabs are invisible to this worker (matchAll returns this origin only) and focusing one of ours is wrong.
      if (target.kind === 'peer') {
        await self.clients.openWindow(target.href)
        return
      }
      const url = target.path
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

// §105.9 — deliberately NO `pushsubscriptionchange` listener here. A previous version tried to
// re-subscribe and POST the new subscription straight from the service worker, but that can never
// succeed: the JWT lives in `authStore`/localStorage, which a service worker has no access to, and
// `POST /api/push/subscriptions` sits behind `[Authorize]` — every such call 401s and was silently
// swallowed by its own `catch`, i.e. dead code with a misleading "safety net" comment. The real
// reconciliation path is useWebPush's own effect, which runs with the app's normal authenticated
// `api` client on every cabinet open (rubezh 1, §105.9) — that is sufficient on its own. Revisit this
// only if/when the platform gains cookie-based auth that a service worker could actually use.
