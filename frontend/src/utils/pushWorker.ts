// ARCHITECTURE_CYCLE33.md §33.5.3, API_CONTRACT_CYCLE33.md §33.28 — the ONLY place a push service worker is registered.
// The worker learns the origin of the sibling site from the registration URL (`/sw.js?peer=<origin>`): it reads
// `self.location` synchronously, with no network and no storage.

const BASE_SCRIPT = '/sw.js'

/** `'/sw.js'` or `'/sw.js?peer=<encoded origin>'`; the origin is normalised through `new URL(x).origin`. */
export function pushWorkerScriptUrl(peerOrigin: string | null | undefined): string {
  if (!peerOrigin) return BASE_SCRIPT
  try {
    const origin = new URL(peerOrigin).origin
    if (origin === 'null') return BASE_SCRIPT
    return `${BASE_SCRIPT}?peer=${encodeURIComponent(origin)}`
  } catch {
    return BASE_SCRIPT
  }
}

function scriptPathOf(registration: ServiceWorkerRegistration | undefined): string | null {
  const url = (registration?.active ?? registration?.waiting ?? registration?.installing)?.scriptURL
  if (!url) return null
  try {
    const u = new URL(url)
    return u.pathname + u.search
  } catch {
    return null
  }
}

/**
 * `peerOrigin` given → register with it. Not given (the buyer path, where the sibling is unknown) → keep the script URL of the
 * already active worker, so a buyer on `/o/:token` does not wipe the sibling recorded earlier by a staff member in this browser.
 */
export async function registerPushWorker(peerOrigin?: string | null): Promise<ServiceWorkerRegistration> {
  let script: string
  if (peerOrigin !== undefined) {
    script = pushWorkerScriptUrl(peerOrigin)
  } else {
    const existing = await navigator.serviceWorker.getRegistration('/')
    script = scriptPathOf(existing) ?? BASE_SCRIPT
  }
  return navigator.serviceWorker.register(script)
}

/**
 * Re-registers an EXISTING worker when its `peer` differs from the wanted one. Does nothing when there is no registration
 * (never installs a worker silently and never asks for permission). Same scope `/` → the PushManager subscription is kept.
 */
export async function refreshPushWorkerPeer(peerOrigin: string): Promise<void> {
  const existing = await navigator.serviceWorker.getRegistration('/')
  if (!existing) return
  const wanted = pushWorkerScriptUrl(peerOrigin)
  if (scriptPathOf(existing) === wanted) return
  await navigator.serviceWorker.register(wanted)
}
