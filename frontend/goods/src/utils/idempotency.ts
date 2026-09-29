/** Idempotency key for `POST /storefront/{slug}/orders` (§395.3). `crypto.randomUUID` exists only in secure
 *  contexts; a phone opening the dev server over plain http still needs a key. */
export function newIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c && typeof c.randomUUID === 'function') return c.randomUUID()
  const bytes = new Uint8Array(16)
  if (c && typeof c.getRandomValues === 'function') c.getRandomValues(bytes)
  else for (let i = 0; i < 16; i++) bytes[i] = Math.floor(Math.random() * 256)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const h = Array.from(bytes, (b) => b.toString(16).padStart(2, '0'))
  return `${h.slice(0, 4).join('')}-${h.slice(4, 6).join('')}-${h.slice(6, 8).join('')}-${h.slice(8, 10).join('')}-${h.slice(10).join('')}`
}

/** `orderUrl` from the API → an in-app path. Same-origin URLs become their path; anything else falls back
 *  to `/o/<token>` (the client never builds absolute links itself, §423, but must not follow a foreign one). */
export function orderPath(orderUrl: string | null | undefined, token: string): string {
  try {
    if (orderUrl) {
      const u = new URL(orderUrl, window.location.origin)
      if (u.origin === window.location.origin && u.pathname.startsWith('/o/')) return u.pathname
    }
  } catch {
    // fall through
  }
  return `/o/${encodeURIComponent(token)}`
}
