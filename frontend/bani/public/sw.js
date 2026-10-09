// ARCHITECTURE_CYCLE42.md §42.13.2 — bani.ezbook.ru Web Push service worker (staff of a «Бани» company and guests of a booking).
//
// Copy of the goods worker (goods/public/sw.js, ARCHITECTURE_CYCLE24.md §454) with bani defaults. Exactly two
// kinds of listeners besides install/activate, and NEVER a listener that intercepts network requests or any
// use of the browser's response-caching storage API: a worker without one physically cannot strand a browser on
// a stale build, which is what makes `skipWaiting()` + `clients.claim()` safe. CI greps this file for the
// disallowed tokens (.github/workflows/ci.yml, job `frontend`).
//
// Payload contract (API_CONTRACT_CYCLE24.md §486; bodies of the «Бани» pushes — API_CONTRACT_CYCLE37.md §37.33): { title, body, tag, url }.
// `url` is expected to be a relative path, but that is enforced by the worker, not assumed: resolveTarget() below
// falls back to the default when the resolved origin differs from this one.
// No `pushsubscriptionchange` listener: the JWT lives in localStorage, a worker cannot reach it (§105.9);
// reconciliation happens in useWebPush on the next open.

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

self.addEventListener('push', (event) => {
  let data = { title: 'EZBOOK Бани', body: '', tag: undefined, url: '/cabinet' }
  try {
    if (event.data) data = { ...data, ...event.data.json() }
  } catch {
    // Malformed payload: still show a bare notification — a push that shows nothing is punished by the browser.
  }

  event.waitUntil(
    self.registration.showNotification(data.title || 'EZBOOK Бани', {
      body: data.body || '',
      // `tag` collapses repeats for the same booking (`s-<bookingId>` staff, `sg-<bookingId>` guest) into one notification.
      tag: data.tag,
      data: { url: targetToUrl(resolveTarget(data.url, '/cabinet')) },
    }),
  )
})

// Clicking focuses an already-open tab instead of always opening a new one.
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  // Resolved again: the data may have been stored by a previous version of this worker.
  const target = resolveTarget(event.notification.data?.url, '/cabinet')

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
